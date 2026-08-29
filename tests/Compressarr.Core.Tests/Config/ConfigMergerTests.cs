using System.Text.Json.Nodes;
using Compressarr.Core.Config;

namespace Compressarr.Core.Tests.Config;

public class ConfigMergerTests
{
    [Fact]
    public void Merge_MissingOverrideProperty_KeepsBaseValue()
    {
        var baseNode = JsonNode.Parse("""{"a":1,"b":{"c":2,"d":3}}""")!;
        var overrideNode = JsonNode.Parse("""{"b":{"c":99}}""")!;

        var result = ConfigMerger.Merge(baseNode, overrideNode);

        Assert.Equal(1, result["a"]!.GetValue<int>());
        Assert.Equal(99, result["b"]!["c"]!.GetValue<int>());
        Assert.Equal(3, result["b"]!["d"]!.GetValue<int>());
    }

    [Fact]
    public void Merge_NestedObjectAtEveryLevel_MergesRecursively()
    {
        var baseNode = JsonNode.Parse("""{"x":{"y":{"z":1,"w":2}}}""")!;
        var overrideNode = JsonNode.Parse("""{"x":{"y":{"z":42}}}""")!;

        var result = ConfigMerger.Merge(baseNode, overrideNode);

        Assert.Equal(42, result["x"]!["y"]!["z"]!.GetValue<int>());
        Assert.Equal(2, result["x"]!["y"]!["w"]!.GetValue<int>());
    }

    [Fact]
    public void Merge_ArrayInOverride_ReplacesBaseArrayWholesale()
    {
        var baseNode = JsonNode.Parse("""{"items":[1,2,3]}""")!;
        var overrideNode = JsonNode.Parse("""{"items":[9]}""")!;

        var result = ConfigMerger.Merge(baseNode, overrideNode);

        var items = result["items"]!.AsArray();
        Assert.Single(items);
        Assert.Equal(9, items[0]!.GetValue<int>());
    }

    [Fact]
    public void Merge_PropertyMissingFromBase_IsAdded()
    {
        var baseNode = JsonNode.Parse("""{"a":1}""")!;
        var overrideNode = JsonNode.Parse("""{"b":2}""")!;

        var result = ConfigMerger.Merge(baseNode, overrideNode);

        Assert.Equal(1, result["a"]!.GetValue<int>());
        Assert.Equal(2, result["b"]!.GetValue<int>());
    }

    [Fact]
    public void Merge_DoesNotMutateInputNodes()
    {
        var baseNode = JsonNode.Parse("""{"a":1}""")!;
        var overrideNode = JsonNode.Parse("""{"a":2}""")!;

        ConfigMerger.Merge(baseNode, overrideNode);

        Assert.Equal(1, baseNode["a"]!.GetValue<int>());
        Assert.Equal(2, overrideNode["a"]!.GetValue<int>());
    }
}
