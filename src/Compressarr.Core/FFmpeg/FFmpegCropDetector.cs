using System.Diagnostics;
using System.Globalization;

namespace Compressarr.Core.FFmpeg;

/// <summary>Finds a file's black bars by sampling it with ffmpeg's cropdetect (also used by the editor's
/// "Preview decisions").</summary>
public static class FFmpegCropDetector
{
    public static async Task<CropRect?> DetectAsync(string ffmpegPath, string source, double? duration, MediaStream video, CancellationToken ct)
    {
        // A few short samples spread through the file; a dark scene or a credits roll in one of them
        // can't narrow the crop, because the result is the box that contains every sample.
        var positions = duration is > 60
            ? new[] { duration.Value * 0.15, duration.Value * 0.45, duration.Value * 0.75 }
            : new[] { Math.Max(0, (duration ?? 10) * 0.3) };

        var samples = new List<CropRect>();
        foreach (var position in positions)
        {
            var output = await RunCaptureAsync(ffmpegPath, new[]
            {
                "-hide_banner", "-nostdin",
                "-ss", position.ToString("0.###", CultureInfo.InvariantCulture),
                "-i", source, "-t", "3", "-an", "-sn", "-dn",
                "-vf", "cropdetect=limit=24:round=2:reset=0",
                "-f", "null", "-"
            }, ct);
            if (output is null) continue;
            if (CropDetection.ParseLast(output) is { } rect) samples.Add(rect);
        }

        return CropDetection.Combine(samples, video.Width, video.Height);
    }

    /// <summary>Runs a short ffmpeg command and returns its log (stderr), or null when it can't run.</summary>
    private static async Task<string?> RunCaptureAsync(string path, IEnumerable<string> args, CancellationToken ct)
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = path,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            foreach (var a in args) startInfo.ArgumentList.Add(a);

            using var process = Process.Start(startInfo);
            if (process is null) return null;
            var stdout = process.StandardOutput.ReadToEndAsync(ct);
            var stderr = process.StandardError.ReadToEndAsync(ct);
            await process.WaitForExitAsync(ct);
            await stdout;
            return await stderr;
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch (Exception)
        {
            return null;
        }
    }

}
