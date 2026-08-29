using System.Collections.ObjectModel;
using Compressarr.Core.Config;
using Compressarr.Core.Presets;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Compressarr.Desktop.ViewModels;

/// <summary>Wraps one LaneConfig for editing. Iterated generically from MainViewModel.Lanes (an
/// ObservableCollection, same shape as Core's List&lt;LaneConfig&gt;) rather than binding to two
/// hardcoded fields — the cheapest possible proof the UI layer doesn't assume exactly two lanes,
/// since Phase 2's add/remove/rename controls will operate on this same collection.</summary>
public partial class LaneViewModel : ObservableObject
{
    public string Id { get; }

    [ObservableProperty]
    public partial string DisplayName { get; set; }

    [ObservableProperty]
    public partial bool Enabled { get; set; }

    [ObservableProperty]
    public partial string Input { get; set; }

    [ObservableProperty]
    public partial string Output { get; set; }

    [ObservableProperty]
    public partial string TvPreset { get; set; }

    [ObservableProperty]
    public partial string MoviePreset { get; set; }

    [ObservableProperty]
    public partial string TvShowBasePath { get; set; }

    [ObservableProperty]
    public partial string MovieBasePath { get; set; }

    public ObservableCollection<string> AvailablePresets { get; } = new();

    public LaneViewModel(LaneConfig config)
    {
        Id = config.Id;
        DisplayName = config.DisplayName;
        Enabled = config.Enabled;
        Input = config.Input;
        Output = config.Output;
        TvPreset = config.TvPreset;
        MoviePreset = config.MoviePreset;
        TvShowBasePath = config.TvShowBasePath;
        MovieBasePath = config.MovieBasePath;
    }

    public LaneConfig ToConfig() => new()
    {
        Id = Id,
        DisplayName = DisplayName,
        Enabled = Enabled,
        Input = Input,
        Output = Output,
        TvPreset = TvPreset,
        MoviePreset = MoviePreset,
        TvShowBasePath = TvShowBasePath,
        MovieBasePath = MovieBasePath
    };

    public void RefreshPresets(IHandBrakePresetService service, string presetsPath)
    {
        AvailablePresets.Clear();
        if (string.IsNullOrWhiteSpace(presetsPath) || !File.Exists(presetsPath)) return;

        try
        {
            foreach (var name in service.GetPresetNames(presetsPath))
            {
                AvailablePresets.Add(name);
            }
        }
        catch
        {
            // Best-effort - an unreadable/invalid presets.json just leaves the dropdown empty.
        }
    }
}
