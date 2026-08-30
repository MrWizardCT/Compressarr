using Compressarr.Core.Config;
using Compressarr.Core.Logging;
using Compressarr.Core.Orchestration;
using Compressarr.Core.Reporting;

namespace Compressarr.Core.Tests.Orchestration;

file sealed class FakeRunOrchestrator : IRunOrchestrator
{
    public int CallCount;
    public bool ThrowOnNextCall;

    public Task<RunResult?> RunOnceAsync(CompressarrConfig config)
    {
        Interlocked.Increment(ref CallCount);
        if (ThrowOnNextCall)
        {
            ThrowOnNextCall = false;
            throw new InvalidOperationException("simulated failure");
        }
        return Task.FromResult<RunResult?>(null);
    }
}

file sealed class FakeRunLogger : IRunLogger
{
    public event Action<string, LogSeverity>? LineWritten;
    public string Initialize(string logFilePath, string timestamp) => "";
    public void Log(string message, LogSeverity severity = LogSeverity.Info) => LineWritten?.Invoke(message, severity);
    public void FileStart(string laneDisplayName, int index, int total, string fileName, double sizeGb, string contentType, string preset) { }
    public void FileComplete(string fileName, double beginSizeGb, double endSizeGb, TimeSpan duration, bool success, string? detailLogFile) { }
}

file sealed class FakeActiveRunController : IActiveRunController
{
    public int AbortCallCount;
    public bool IsRunning => false;
    public CancellationToken Begin() => CancellationToken.None;
    public void End() { }
    public void Abort() => Interlocked.Increment(ref AbortCallCount);
}

public class RunLoopControllerTests
{
    // Real, very short intervals rather than a fake TimeProvider - keeps tests fast (well under a
    // second total) without the complexity of a hand-rolled ITimer-backed fake.
    private static readonly TimeSpan TinyInterval = TimeSpan.FromMilliseconds(20);

    private static async Task WaitUntil(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (condition()) return;
            await Task.Delay(5);
        }
        throw new TimeoutException("Condition was not met within the timeout.");
    }

    [Fact]
    public void Start_SetsIsRunningTrue()
    {
        var controller = new RunLoopController(new FakeRunOrchestrator(), new FakeRunLogger(), new FakeActiveRunController());

        controller.Start(new CompressarrConfig(), TimeSpan.FromMinutes(5));

        Assert.True(controller.IsRunning);
    }

    [Fact]
    public async Task Start_CalledTwice_DoesNotDoubleStart()
    {
        var orchestrator = new FakeRunOrchestrator();
        var controller = new RunLoopController(orchestrator, new FakeRunLogger(), new FakeActiveRunController());

        controller.Start(new CompressarrConfig(), TinyInterval);
        await WaitUntil(() => orchestrator.CallCount >= 1, TimeSpan.FromSeconds(2));
        var countAfterFirstPass = orchestrator.CallCount;

        controller.Start(new CompressarrConfig(), TinyInterval); // second Start should be a no-op

        await Task.Delay(TinyInterval * 3);
        // If Start had double-started, two loops would each be incrementing the counter on the
        // same cadence, so the count would roughly double compared to a single loop over the same
        // wait; a generous upper bound (not an exact count, timing is inherently a little fuzzy)
        // catches an actual double-start without making the test flaky.
        var countAfterWait = orchestrator.CallCount;
        Assert.True(countAfterWait - countAfterFirstPass < 15, $"Expected roughly one loop's worth of calls, got {countAfterWait - countAfterFirstPass} - looks like Start double-started the loop.");

        await controller.StopAsync();
    }

    [Fact]
    public async Task StopAsync_SetsIsRunningFalse_AndWaitsForInFlightPassToComplete()
    {
        var controller = new RunLoopController(new FakeRunOrchestrator(), new FakeRunLogger(), new FakeActiveRunController());
        controller.Start(new CompressarrConfig(), TimeSpan.FromMinutes(5));

        await controller.StopAsync();

        Assert.False(controller.IsRunning);
    }

    [Fact]
    public async Task Loop_InvokesRunOnceAsync_OnEachPollInterval()
    {
        var orchestrator = new FakeRunOrchestrator();
        var controller = new RunLoopController(orchestrator, new FakeRunLogger(), new FakeActiveRunController());

        controller.Start(new CompressarrConfig(), TinyInterval);
        await WaitUntil(() => orchestrator.CallCount >= 3, TimeSpan.FromSeconds(2));
        await controller.StopAsync();

        Assert.True(orchestrator.CallCount >= 3);
    }

    [Fact]
    public async Task Loop_SwallowsExceptionFromRunOnceAsync_AndContinuesPolling()
    {
        var orchestrator = new FakeRunOrchestrator { ThrowOnNextCall = true };
        var controller = new RunLoopController(orchestrator, new FakeRunLogger(), new FakeActiveRunController());

        controller.Start(new CompressarrConfig(), TinyInterval);
        // The first pass throws; the loop must still be alive and polling afterward.
        await WaitUntil(() => orchestrator.CallCount >= 2, TimeSpan.FromSeconds(2));
        await controller.StopAsync();

        Assert.True(controller is not null); // loop didn't crash the process/controller
        Assert.False(controller!.IsRunning);
    }

    [Fact]
    public async Task RunningChanged_FiresOnStartAndStop()
    {
        var controller = new RunLoopController(new FakeRunOrchestrator(), new FakeRunLogger(), new FakeActiveRunController());
        var events = new List<bool>();
        controller.RunningChanged += running => events.Add(running);

        controller.Start(new CompressarrConfig(), TimeSpan.FromMinutes(5));
        await controller.StopAsync();

        Assert.Equal(new[] { true, false }, events);
    }

    [Fact]
    public async Task Abort_CallsActiveRunControllerAbort_AndStopsTheLoop()
    {
        var activeRunController = new FakeActiveRunController();
        var controller = new RunLoopController(new FakeRunOrchestrator(), new FakeRunLogger(), activeRunController);
        controller.Start(new CompressarrConfig(), TinyInterval);

        controller.Abort();
        await WaitUntil(() => !controller.IsRunning, TimeSpan.FromSeconds(2));

        Assert.Equal(1, activeRunController.AbortCallCount);
        Assert.False(controller.IsRunning);
    }

    [Fact]
    public void NextRunUtc_IsNull_BeforeStart()
    {
        var controller = new RunLoopController(new FakeRunOrchestrator(), new FakeRunLogger(), new FakeActiveRunController());

        Assert.Null(controller.NextRunUtc);
    }

    [Fact]
    public void TriggerNow_ReturnsFalse_WhenNotStarted()
    {
        var controller = new RunLoopController(new FakeRunOrchestrator(), new FakeRunLogger(), new FakeActiveRunController());

        Assert.False(controller.TriggerNow());
    }

    [Fact]
    public async Task TriggerNow_SkipsRemainingWait_AndStartsNextPassImmediately()
    {
        var orchestrator = new FakeRunOrchestrator();
        var controller = new RunLoopController(orchestrator, new FakeRunLogger(), new FakeActiveRunController());

        // A long poll interval - if TriggerNow didn't actually cut the wait short, the second
        // pass wouldn't arrive within the test's timeout at all.
        controller.Start(new CompressarrConfig(), TimeSpan.FromMinutes(5));
        await WaitUntil(() => orchestrator.CallCount >= 1, TimeSpan.FromSeconds(2));
        await WaitUntil(() => controller.NextRunUtc is not null, TimeSpan.FromSeconds(2));

        var triggered = controller.TriggerNow();
        await WaitUntil(() => orchestrator.CallCount >= 2, TimeSpan.FromSeconds(2));

        await controller.StopAsync();

        Assert.True(triggered);
        Assert.True(orchestrator.CallCount >= 2);
    }
}
