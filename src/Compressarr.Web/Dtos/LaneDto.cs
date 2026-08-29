namespace Compressarr.Web.Dtos;

public sealed record LaneDto(
    string Id,
    string DisplayName,
    bool Enabled,
    string Input,
    string Output,
    string TvPreset,
    string MoviePreset,
    string TvShowBasePath,
    string MovieBasePath);
