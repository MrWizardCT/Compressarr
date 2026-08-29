using System.Collections.ObjectModel;
using Avalonia.Threading;
using Compressarr.Core.Config;
using Compressarr.Core.Logging;
using Compressarr.Core.Orchestration;
using Compressarr.Core.Presets;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Compressarr.Desktop.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    private readonly IConfigStore _configStore;
    private readonly IRunOrchestrator _runOrchestrator;
    private readonly IHandBrakePresetService _presetService;
    private readonly IRunLogger _logger;

    [ObservableProperty]
    public partial string HandBrakeCliPath { get; set; } = "";

    [ObservableProperty]
    public partial string PresetsPath { get; set; } = "";

    [ObservableProperty]
    public partial string ExtraOptions { get; set; } = "";

    [ObservableProperty]
    public partial string VidTypes { get; set; } = "";

    [ObservableProperty]
    public partial string MinSize { get; set; } = "0gb";

    [ObservableProperty]
    public partial int Limit { get; set; }

    [ObservableProperty]
    public partial bool OutSameAsIn { get; set; }

    [ObservableProperty]
    public partial bool MoveFiles { get; set; }

    [ObservableProperty]
    public partial DeleteAfterConvertMode DeleteAfterConvert { get; set; }

    [ObservableProperty]
    public partial string StatusMessage { get; set; } = "";

    [ObservableProperty]
    public partial bool IsRunning { get; set; }

    public ObservableCollection<LaneViewModel> Lanes { get; } = new();
    public ObservableCollection<string> LogLines { get; } = new();
    public IReadOnlyList<DeleteAfterConvertMode> DeleteModes { get; } = Enum.GetValues<DeleteAfterConvertMode>();

    public MainViewModel(IConfigStore configStore, IRunOrchestrator runOrchestrator, IHandBrakePresetService presetService, IRunLogger logger)
    {
        _configStore = configStore;
        _runOrchestrator = runOrchestrator;
        _presetService = presetService;
        _logger = logger;

        _logger.LineWritten += OnLineWritten;

        LoadConfig();
    }

    // Parameterless constructor for the XAML previewer only.
    public MainViewModel() : this(new JsonConfigStore(), null!, new HandBrakePresetService(), new FileRunLogger()) { }

    private void OnLineWritten(string line, LogSeverity severity)
    {
        Dispatcher.UIThread.Post(() =>
        {
            LogLines.Add(line);
            while (LogLines.Count > 2000) LogLines.RemoveAt(0);
        });
    }

    [RelayCommand]
    private void LoadConfig()
    {
        var configPath = AppPaths.GetConfigFilePath();
        var isFirstRun = !File.Exists(configPath);

        var config = _configStore.Load(configPath);
        if (isFirstRun)
        {
            // Matches v1: if no config file exists yet, write the defaults out immediately
            // rather than only persisting on the user's first explicit Save.
            _configStore.Save(config, configPath);
        }

        ApplyConfig(config);
        RefreshAllPresets();
        StatusMessage = isFirstRun ? "Welcome! Default settings created - review and Save." : "Settings loaded.";
    }

    [RelayCommand]
    private void Save()
    {
        var config = BuildConfig();
        _configStore.Save(config, AppPaths.GetConfigFilePath());
        StatusMessage = "Settings saved.";
    }

    [RelayCommand]
    private void RefreshPresets() => RefreshAllPresets();

    [RelayCommand]
    private void AddLane()
    {
        var lane = new LaneViewModel(new LaneConfig
        {
            DisplayName = $"New Lane {Lanes.Count + 1}",
            Enabled = true
        });
        lane.RefreshPresets(_presetService, PresetsPath);
        Lanes.Add(lane);
    }

    /// <summary>Called from MainWindow's code-behind after the user has confirmed removal via
    /// ConfirmDialog - the confirmation prompt itself lives in the view, not here, since it's
    /// purely a UI concern (nothing about the Core data model needs to know about it).</summary>
    public void RemoveLane(LaneViewModel lane) => Lanes.Remove(lane);

    [RelayCommand(CanExecute = nameof(CanRunOnce))]
    private async Task RunOnceAsync()
    {
        IsRunning = true;
        StatusMessage = "Running...";
        try
        {
            var config = BuildConfig();
            var result = await _runOrchestrator.RunOnceAsync(config);
            StatusMessage = result is null
                ? "Run aborted - check the HandBrakeCLI and presets.json paths above."
                : $"Done: {result.TotalFiles} file(s) processed.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Run failed: {ex.Message}";
        }
        finally
        {
            IsRunning = false;
        }
    }

    private bool CanRunOnce() => !IsRunning;

    partial void OnIsRunningChanged(bool value) => RunOnceCommand.NotifyCanExecuteChanged();

    private void RefreshAllPresets()
    {
        foreach (var lane in Lanes)
        {
            lane.RefreshPresets(_presetService, PresetsPath);
        }
    }

    private void ApplyConfig(CompressarrConfig config)
    {
        HandBrakeCliPath = config.HandBrake.CliPath;
        PresetsPath = config.HandBrake.PresetsPath;
        ExtraOptions = config.HandBrake.Options;
        VidTypes = string.Join(", ", config.Processing.VidTypes);
        MinSize = ByteSize.Format(config.Processing.MinSizeBytes);
        Limit = config.Processing.Limit;
        OutSameAsIn = config.Processing.OutSameAsIn;
        MoveFiles = config.Processing.MoveFiles;
        DeleteAfterConvert = config.Processing.DeleteAfterConvert;

        Lanes.Clear();
        foreach (var lane in config.Lanes)
        {
            Lanes.Add(new LaneViewModel(lane));
        }
    }

    private CompressarrConfig BuildConfig()
    {
        var config = new CompressarrConfig
        {
            HandBrake = new HandBrakeSettings
            {
                CliPath = HandBrakeCliPath,
                PresetsPath = PresetsPath,
                Options = ExtraOptions
            },
            Processing = new ProcessingSettings
            {
                VidTypes = VidTypes.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList(),
                MinSizeBytes = TryParseByteSize(MinSize),
                Limit = Limit,
                OutSameAsIn = OutSameAsIn,
                MoveFiles = MoveFiles,
                DeleteAfterConvert = DeleteAfterConvert
            },
            Lanes = Lanes.Select(l => l.ToConfig()).ToList()
        };

        return config;
    }

    private static long TryParseByteSize(string value)
    {
        try { return ByteSize.Parse(value); }
        catch (FormatException) { return 0; }
    }
}
