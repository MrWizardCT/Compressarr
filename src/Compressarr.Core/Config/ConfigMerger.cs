using System.Text.Json.Nodes;

namespace Compressarr.Core.Config;

/// <summary>
/// Literal port of v1's Merge-CompressarrConfigObject: recursively overlays override's properties
/// onto a clone of base. Missing properties in override leave the base value in place, so a
/// partial user config file is valid — only specify what differs. Only JSON objects merge
/// recursively; arrays and scalars in the override simply replace the base value wholesale (this
/// matches v1 exactly — Merge-CompressarrConfigObject only special-cases PSCustomObject nesting).
/// </summary>
public static class ConfigMerger
{
    public static JsonNode Merge(JsonNode baseNode, JsonNode overrideNode)
    {
        if (baseNode is not JsonObject baseObj || overrideNode is not JsonObject overrideObj)
        {
            // Non-object at this level: override replaces base outright.
            return overrideNode.DeepClone();
        }

        var result = (JsonObject)baseObj.DeepClone();

        foreach (var prop in overrideObj)
        {
            var baseHasProp = result.TryGetPropertyValue(prop.Key, out var baseValue);
            var overrideValue = prop.Value;

            var isNestedObject = overrideValue is JsonObject && baseValue is JsonObject;

            if (isNestedObject)
            {
                result[prop.Key] = Merge(baseValue!, overrideValue!);
            }
            else
            {
                result[prop.Key] = overrideValue?.DeepClone();
            }
        }

        return result;
    }
}
