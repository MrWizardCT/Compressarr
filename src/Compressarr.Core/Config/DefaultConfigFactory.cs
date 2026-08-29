namespace Compressarr.Core.Config;

/// <summary>
/// Builds a fresh default CompressarrConfig instance. Always construct a new instance rather
/// than caching/sharing one, mirroring v1's Get-CompressarrDefaultConfig contract that callers
/// can never accidentally mutate a shared default by reference.
/// </summary>
public static class DefaultConfigFactory
{
    public static CompressarrConfig Create()
    {
        return new CompressarrConfig
        {
            Lanes = new List<LaneConfig>
            {
                new()
                {
                    Id = "hdsd",
                    DisplayName = "HD/SD",
                    Enabled = true
                },
                new()
                {
                    Id = "uhd",
                    DisplayName = "UHD",
                    Enabled = true
                }
            }
        };
    }
}
