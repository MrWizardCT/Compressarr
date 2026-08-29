using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Compressarr.Core.Config;

public interface IConfigStore
{
    /// <summary>Loads config from the given path, merged over the built-in defaults so a partial
    /// or missing file still yields a fully-populated config. If the file doesn't exist, returns
    /// the defaults unchanged — the caller decides whether to write them out.</summary>
    CompressarrConfig Load(string path);

    void Save(CompressarrConfig config, string path);
}

public sealed class JsonConfigStore : IConfigStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public CompressarrConfig Load(string path)
    {
        var defaults = DefaultConfigFactory.Create();

        if (!File.Exists(path))
        {
            return defaults;
        }

        JsonNode overrideNode;
        try
        {
            var raw = File.ReadAllText(path);
            overrideNode = JsonNode.Parse(raw)
                ?? throw new InvalidDataException($"Compressarr: config file '{path}' parsed to an empty document.");
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"Compressarr: failed to parse config file '{path}' as JSON. {ex.Message}", ex);
        }

        var defaultsNode = JsonSerializer.SerializeToNode(defaults, SerializerOptions)!;
        var mergedNode = ConfigMerger.Merge(defaultsNode, overrideNode);

        return mergedNode.Deserialize<CompressarrConfig>(SerializerOptions)
            ?? throw new InvalidDataException($"Compressarr: merged config for '{path}' deserialized to null.");
    }

    public void Save(CompressarrConfig config, string path)
    {
        var folder = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(folder) && !Directory.Exists(folder))
        {
            Directory.CreateDirectory(folder);
        }

        var json = JsonSerializer.Serialize(config, SerializerOptions);
        File.WriteAllText(path, json);
    }
}
