using System.Text.RegularExpressions;

namespace Compressarr.Core.Config;

public interface IPathExpander
{
    /// <summary>Expands %VAR% tokens in a path field. Config files store paths with literal
    /// tokens (e.g. %ProgramFiles%, %CompressarrAppData%) so the same JSON is portable across
    /// machines; callers expand at the point of use, never at load time.</summary>
    string Expand(string value);

    bool PathExists(string value);
}

/// <summary>
/// Environment.ExpandEnvironmentVariables only does real work on Windows (it's a no-op on
/// Linux/macOS), so this implements %VAR% expansion ourselves on all three platforms, plus a
/// Compressarr-defined virtual token, %CompressarrAppData%, for defaults that should point at the
/// per-OS app-data directory regardless of platform.
/// </summary>
public sealed partial class PathExpander : IPathExpander
{
    [GeneratedRegex(@"%(\w+)%")]
    private static partial Regex TokenPattern();

    public string Expand(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return value;

        return TokenPattern().Replace(value, match =>
        {
            var name = match.Groups[1].Value;

            if (string.Equals(name, "CompressarrAppData", StringComparison.OrdinalIgnoreCase))
            {
                return AppPaths.GetAppDataDirectory();
            }

            var envValue = Environment.GetEnvironmentVariable(name);
            return envValue ?? match.Value;
        });
    }

    public bool PathExists(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        var expanded = Expand(value);
        return Directory.Exists(expanded) || File.Exists(expanded);
    }
}
