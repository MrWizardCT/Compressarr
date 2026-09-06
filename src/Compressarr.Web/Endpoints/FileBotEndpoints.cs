using System.Diagnostics;
using System.Text;
using Microsoft.AspNetCore.Http;
using Compressarr.Core.Config;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;

namespace Compressarr.Web.Endpoints;

public static class FileBotEndpoints
{
    private static readonly TimeSpan CommandTimeout = TimeSpan.FromSeconds(30);

    public static void MapFileBotEndpoints(this IEndpointRouteBuilder app)
    {
        // FileBot's own documented fix for "Unable to establish loopback connection" (a real,
        // machine-specific Java networking issue some users hit - not a Compressarr bug): force
        // IPv4 and switch FileBot's HTTP client to its older, more broadly-compatible
        // implementation. Both are persistent FileBot preferences (fn:properties), set once here
        // and then in effect for every future FileBot invocation, not just this one.
        app.MapPost("/api/filebot/fix-network", (IConfigStore configStore, IPathExpander pathExpander) =>
        {
            var config = configStore.Load(AppPaths.GetConfigFilePath());
            var cliPath = pathExpander.Expand(config.FileBot.CliPath);
            if (string.IsNullOrWhiteSpace(cliPath) || !File.Exists(cliPath))
            {
                return Results.Json(new { success = false, message = $"FileBot was not found at '{cliPath}'." });
            }

            var commands = new[]
            {
                "-script \"fn:properties\" --def java.net.preferIPv4Stack=true",
                "-script \"fn:properties\" --def net.filebot.web.WebRequest.v1=true"
            };

            var output = new StringBuilder();
            foreach (var args in commands)
            {
                var (success, commandOutput) = RunCommand(cliPath, args);
                output.AppendLine(commandOutput);
                if (!success)
                {
                    return Results.Json(new { success = false, message = "One of the fix commands failed - see details below.", details = output.ToString().Trim() });
                }
            }

            return Results.Json(new { success = true, message = "FileBot network fix applied - try your run again.", details = output.ToString().Trim() });
        });
    }

    private static (bool Success, string Output) RunCommand(string cliPath, string args)
    {
        try
        {
            var startInfo = new ProcessStartInfo(cliPath, args)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };

            using var process = new Process { StartInfo = startInfo };
            var output = new StringBuilder();
            process.OutputDataReceived += (_, e) => { if (e.Data is not null) lock (output) output.AppendLine(e.Data); };
            process.ErrorDataReceived += (_, e) => { if (e.Data is not null) lock (output) output.AppendLine(e.Data); };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            if (!process.WaitForExit((int)CommandTimeout.TotalMilliseconds))
            {
                try { process.Kill(entireProcessTree: true); } catch { /* best effort */ }
                return (false, $"\"{args}\" did not finish within {CommandTimeout.TotalSeconds:0} seconds.");
            }

            return (process.ExitCode == 0, output.ToString().Trim());
        }
        catch (Exception ex)
        {
            return (false, $"Failed to run \"{args}\": {ex.Message}");
        }
    }
}
