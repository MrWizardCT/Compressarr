namespace Compressarr.Core.Conversion;

public interface IMetadataService
{
    bool Enabled { get; set; }

    /// <summary>Strips the embedded title tag from a media file. No-op if Enabled is false or
    /// the file doesn't exist; never throws — a failure here must never fail an otherwise
    /// successful conversion, matching v1's Clear-CompressarrTitleMetadata try/catch.</summary>
    void ClearTitle(string filePath);
}

/// <summary>Ported from Clear-CompressarrTitleMetadata, using TagLibSharp (a cross-platform,
/// actively maintained .NET port of the taglib-sharp.dll v1 loaded via Import-Module) instead of
/// a raw DLL load.</summary>
public sealed class MetadataService : IMetadataService
{
    public bool Enabled { get; set; }

    public void ClearTitle(string filePath)
    {
        if (!Enabled) return;
        if (!File.Exists(filePath)) return;

        try
        {
            using var file = TagLib.File.Create(filePath);
            var customTag = (TagLib.Mpeg4.AppleTag)file.GetTag(TagLib.TagTypes.Apple, true);
            customTag.Title = "";
            file.Save();
        }
        catch
        {
            // Best-effort only, matches v1's Write-Warning-and-continue behavior.
        }
    }
}
