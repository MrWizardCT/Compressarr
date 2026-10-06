using Compressarr.Core.FFmpeg;

namespace Compressarr.Core.Tests.FFmpeg;

/// <summary>ffprobe-shaped fixtures: what the planner and command builder are tested against.</summary>
internal static class FFmpegFixtures
{
    /// <summary>A UHD HDR10 rip: HEVC video with mastering-display and content-light data, two English
    /// audio tracks (TrueHD 7.1 and AC3 5.1) and a French one, English text + PGS subtitles and a French
    /// text track, a font attachment, cover art, and two chapters.</summary>
    public const string UhdHdr10Rip = """
        {
          "streams": [
            { "index": 0, "codec_name": "hevc", "codec_type": "video", "profile": "Main 10", "width": 3840, "height": 2160,
              "pix_fmt": "yuv420p10le", "field_order": "progressive", "color_space": "bt2020nc", "color_transfer": "smpte2084",
              "color_primaries": "bt2020", "color_range": "tv", "disposition": { "default": 1, "forced": 0, "attached_pic": 0 },
              "tags": { "language": "eng" },
              "side_data_list": [
                { "side_data_type": "Mastering display metadata",
                  "red_x": "34000/50000", "red_y": "16000/50000", "green_x": "13250/50000", "green_y": "34500/50000",
                  "blue_x": "7500/50000", "blue_y": "3000/50000", "white_point_x": "15635/50000", "white_point_y": "16450/50000",
                  "min_luminance": "50/10000", "max_luminance": "10000000/10000" },
                { "side_data_type": "Content light level metadata", "max_content": 1000, "max_average": 400 } ] },
            { "index": 1, "codec_name": "truehd", "codec_type": "audio", "channels": 8, "disposition": { "default": 1 }, "tags": { "language": "eng", "title": "TrueHD Atmos 7.1" } },
            { "index": 2, "codec_name": "ac3", "codec_type": "audio", "channels": 6, "disposition": { "default": 0 }, "tags": { "language": "eng", "title": "Compatibility" } },
            { "index": 3, "codec_name": "ac3", "codec_type": "audio", "channels": 6, "disposition": { "default": 0 }, "tags": { "language": "fra" } },
            { "index": 4, "codec_name": "subrip", "codec_type": "subtitle", "disposition": { "default": 1, "forced": 0 }, "tags": { "language": "eng" } },
            { "index": 5, "codec_name": "hdmv_pgs_subtitle", "codec_type": "subtitle", "disposition": { "default": 0, "forced": 0 }, "tags": { "language": "eng" } },
            { "index": 6, "codec_name": "subrip", "codec_type": "subtitle", "disposition": { "default": 0, "forced": 0 }, "tags": { "language": "fra" } },
            { "index": 7, "codec_name": "ttf", "codec_type": "attachment", "disposition": { "default": 0 }, "tags": { "filename": "font.ttf", "mimetype": "font/ttf" } },
            { "index": 8, "codec_name": "mjpeg", "codec_type": "video", "width": 600, "height": 900, "disposition": { "default": 0, "attached_pic": 1 } }
          ],
          "chapters": [ { "id": 1 }, { "id": 2 } ],
          "format": { "duration": "7200.500000" }
        }
        """;

    /// <summary>An old interlaced DVD-style rip: MPEG-2 480i, a single untagged AC3 track, one DVD
    /// (picture) subtitle, no chapters.</summary>
    public const string InterlacedDvdRip = """
        {
          "streams": [
            { "index": 0, "codec_name": "mpeg2video", "codec_type": "video", "width": 720, "height": 480, "pix_fmt": "yuv420p",
              "field_order": "tt", "color_space": "smpte170m", "color_transfer": "smpte170m", "color_primaries": "smpte170m",
              "disposition": { "default": 1 }, "tags": {} },
            { "index": 1, "codec_name": "ac3", "codec_type": "audio", "channels": 2, "disposition": { "default": 1 }, "tags": {} },
            { "index": 2, "codec_name": "dvd_subtitle", "codec_type": "subtitle", "disposition": { "default": 0, "forced": 0 }, "tags": { "language": "eng" } }
          ],
          "chapters": [],
          "format": { "duration": "5400.0" }
        }
        """;

    /// <summary>A Dolby Vision profile 8 file.</summary>
    public const string DolbyVisionRip = """
        {
          "streams": [
            { "index": 0, "codec_name": "hevc", "codec_type": "video", "width": 3840, "height": 2160, "pix_fmt": "yuv420p10le",
              "field_order": "progressive", "color_space": "bt2020nc", "color_transfer": "smpte2084", "color_primaries": "bt2020",
              "disposition": { "default": 1 }, "tags": { "language": "eng" },
              "side_data_list": [ { "side_data_type": "DOVI configuration record", "dv_profile": 8, "dv_level": 6 } ] },
            { "index": 1, "codec_name": "eac3", "codec_type": "audio", "channels": 6, "disposition": { "default": 1 }, "tags": { "language": "eng" } }
          ],
          "chapters": [],
          "format": { "duration": "3600.0" }
        }
        """;

    public const string Hdr10PlusFrames = """
        { "frames": [ { "side_data_list": [ { "side_data_type": "HDR Dynamic Metadata SMPTE2094-40 (HDR10+)" } ] } ] }
        """;

    public const string PlainFrames = """
        { "frames": [ { "side_data_list": [ { "side_data_type": "Mastering display metadata" } ] }, {} ] }
        """;

    public static MediaProbeResult Uhd() => FFprobeParser.Parse(UhdHdr10Rip);
    public static MediaProbeResult Dvd() => FFprobeParser.Parse(InterlacedDvdRip);
}
