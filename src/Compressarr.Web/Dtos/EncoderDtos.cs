namespace Compressarr.Web.Dtos;

/// <summary>The Encoder page's settings: where the encoder tools are and the extra options passed
/// to every encode. The read-only fields (profile counts, lanes using HandBrake) are ignored on PUT.</summary>
public sealed record EncoderSettingsDto(
    string HandBrakeCliPath,
    string HandBrakeOptions,
    int BuiltInProfileCount,
    int UserProfileCount,
    List<string> HandBrakeLanes,
    List<ValidationIssueDto> ValidationIssues);

/// <summary>One row of the Profiles page. Engine is "handbrake" today; "ffmpeg" arrives with the
/// ffmpeg encoder. UsedBy lists the lanes whose TV or Movie preset is this profile.</summary>
public sealed record ProfileDto(
    string Engine,
    string Name,
    bool BuiltIn,
    string Container,
    string Video,
    string Audio,
    string Description,
    List<string> UsedBy);

public sealed record ProfileListDto(
    List<ProfileDto> Profiles,
    string UserFilePath,
    string? UserFileError);
