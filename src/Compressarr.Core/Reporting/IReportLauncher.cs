using System.Diagnostics;

namespace Compressarr.Core.Reporting;

public interface IReportLauncher
{
    void Open(string reportFilePath);
}

/// <summary>Process.Start with UseShellExecute=true opens a file with its OS default handler on
/// Windows, macOS, and Linux alike in modern .NET — unlike v1's Invoke-Item, which relied on
/// Windows-specific shell file-association handling, one implementation covers all three
/// platforms.</summary>
public sealed class ReportLauncher : IReportLauncher
{
    public void Open(string reportFilePath)
    {
        Process.Start(new ProcessStartInfo(reportFilePath) { UseShellExecute = true });
    }
}
