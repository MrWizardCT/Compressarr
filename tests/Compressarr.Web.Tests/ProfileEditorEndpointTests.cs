using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Compressarr.Core.Config;
using Compressarr.Core.Presets;
using Microsoft.Extensions.DependencyInjection;

namespace Compressarr.Web.Tests;

/// <summary>Create / edit / duplicate / delete / import HandBrake profiles through the real HTTP
/// surface, as the Profiles page and its editor use them.</summary>
public class ProfileEditorEndpointTests
{
    private static readonly LaneSpec Lane1 = new("lane1", "Lane One");

    private static string Url(string name) => "/api/profiles/handbrake/" + Uri.EscapeDataString(name);

    private static async Task<JsonObject> GetFormAsync(QueueHost host, string name)
    {
        var dto = (await host.Client.GetFromJsonAsync<JsonObject>(Url(name)))!;
        return (JsonObject)dto["form"]!.DeepClone();
    }

    private static async Task<HttpResponseMessage> CreateAsync(QueueHost host, string name, string? baseName = null, Action<JsonObject>? tweak = null)
    {
        var form = await GetFormAsync(host, baseName ?? "Compressarr SD-HD");
        form["name"] = name;
        tweak?.Invoke(form);
        return await host.Client.PostAsJsonAsync("/api/profiles/handbrake", new JsonObject { ["form"] = form, ["baseName"] = baseName });
    }

    [Fact]
    public async Task GetProfile_ReturnsTheFormAndWhetherItIsLocked()
    {
        await using var host = await QueueHost.StartAsync(Lane1);

        var dto = (await host.Client.GetFromJsonAsync<JsonObject>(Url("Compressarr SD-HD")))!;

        Assert.True(dto["builtIn"]!.GetValue<bool>());
        Assert.Equal("x265_10bit", dto["form"]!["videoEncoder"]!.GetValue<string>());
        Assert.Equal(24, dto["form"]!["rf"]!.GetValue<double>());
        Assert.EndsWith("handbrake-active.json", dto["activePresetsPath"]!.GetValue<string>());
        Assert.Equal(HttpStatusCode.NotFound, (await host.Client.GetAsync(Url("Nope"))).StatusCode);
    }

    [Fact]
    public async Task CreateProfile_StartsFromTheBase_AddsIt_AndLanesCanPickIt()
    {
        await using var host = await QueueHost.StartAsync(Lane1);

        using var response = await CreateAsync(host, "Cartoons x265", tweak: f => { f["rf"] = 26; f["videoPreset"] = "slow"; });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var names = (await host.Client.GetFromJsonAsync<string[]>("/api/presets"))!;
        Assert.Contains("Cartoons x265", names);
        var form = await GetFormAsync(host, "Cartoons x265");
        Assert.Equal(26, form["rf"]!.GetValue<double>());
        Assert.Equal("slow", form["videoPreset"]!.GetValue<string>());
        // what the form doesn't show came from the base: the built-in's 7.1 E-AC3 audio
        Assert.Equal("eac3", form["audioEncoder"]!.GetValue<string>());
        // and it is not locked
        Assert.False((await host.Client.GetFromJsonAsync<JsonObject>(Url("Cartoons x265")))!["builtIn"]!.GetValue<bool>());
        // HandBrake will be handed it
        var active = File.ReadAllText(AppPaths.GetHandBrakeActivePresetsFilePath());
        Assert.Contains("Cartoons x265", active);
    }

    [Fact]
    public async Task CreateProfile_RejectsBadInput_WithFieldMessages()
    {
        await using var host = await QueueHost.StartAsync(Lane1);

        using var noName = await CreateAsync(host, "  ");
        Assert.Equal(HttpStatusCode.BadRequest, noName.StatusCode);
        var body = (await noName.Content.ReadFromJsonAsync<JsonObject>())!;
        Assert.Equal("name", body["validationIssues"]![0]!["field"]!.GetValue<string>());

        using var slash = await CreateAsync(host, "a/b");
        Assert.Equal(HttpStatusCode.BadRequest, slash.StatusCode);

        using var clash = await CreateAsync(host, "compressarr sd-hd");
        Assert.Equal(HttpStatusCode.BadRequest, clash.StatusCode);
        Assert.Contains("already exists", (await clash.Content.ReadAsStringAsync()));

        using var badRf = await CreateAsync(host, "Fine", tweak: f => f["rf"] = 200);
        Assert.Equal(HttpStatusCode.BadRequest, badRf.StatusCode);
    }

    [Fact]
    public async Task EditProfile_SavesChanges_AndABuiltInCannotBeEdited()
    {
        await using var host = await QueueHost.StartAsync(Lane1);
        (await CreateAsync(host, "Mine")).EnsureSuccessStatusCode();

        var form = await GetFormAsync(host, "Mine");
        form["rf"] = 19;
        form["subtitleTracks"] = "none";
        using var saved = await host.Client.PutAsJsonAsync(Url("Mine"), new JsonObject { ["form"] = form });
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);

        var reread = await GetFormAsync(host, "Mine");
        Assert.Equal(19, reread["rf"]!.GetValue<double>());
        Assert.Equal("none", reread["subtitleTracks"]!.GetValue<string>());

        var builtInForm = await GetFormAsync(host, "Compressarr SD-HD");
        using var locked = await host.Client.PutAsJsonAsync(Url("Compressarr SD-HD"), new JsonObject { ["form"] = builtInForm });
        Assert.Equal(HttpStatusCode.BadRequest, locked.StatusCode);
        Assert.Contains("built-in", await locked.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task RenamingAProfile_RepointsEveryLaneThatUsesIt()
    {
        await using var host = await QueueHost.StartAsync(Lane1);
        (await CreateAsync(host, "Old name")).EnsureSuccessStatusCode();
        var lanes = (await host.Client.GetFromJsonAsync<JsonArray>("/api/lanes"))!;
        var lane = lanes[0]!.AsObject();
        lane["tvPreset"] = "Old name";
        lane["moviePreset"] = "Old name";
        (await host.Client.PutAsJsonAsync("/api/lanes/lane1", lane)).EnsureSuccessStatusCode();

        var form = await GetFormAsync(host, "Old name");
        form["name"] = "New name";
        (await host.Client.PutAsJsonAsync(Url("Old name"), new JsonObject { ["form"] = form })).EnsureSuccessStatusCode();

        var after = (await host.Client.GetFromJsonAsync<JsonArray>("/api/lanes"))![0]!;
        Assert.Equal("New name", after["tvPreset"]!.GetValue<string>());
        Assert.Equal("New name", after["moviePreset"]!.GetValue<string>());
        Assert.Equal(HttpStatusCode.NotFound, (await host.Client.GetAsync(Url("Old name"))).StatusCode);
    }

    [Fact]
    public async Task Rename_ToANameThatIsTaken_IsRefused()
    {
        await using var host = await QueueHost.StartAsync(Lane1);
        (await CreateAsync(host, "One")).EnsureSuccessStatusCode();
        (await CreateAsync(host, "Two")).EnsureSuccessStatusCode();

        var form = await GetFormAsync(host, "One");
        form["name"] = "two";
        using var response = await host.Client.PutAsJsonAsync(Url("One"), new JsonObject { ["form"] = form });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Duplicate_MakesAnUnlockedCopy_WithAUniqueName()
    {
        await using var host = await QueueHost.StartAsync(Lane1);

        var first = (await (await host.Client.PostAsJsonAsync(Url("Compressarr SD-HD") + "/duplicate", new JsonObject())).Content.ReadFromJsonAsync<JsonObject>())!;
        var second = (await (await host.Client.PostAsJsonAsync(Url("Compressarr SD-HD") + "/duplicate", new JsonObject())).Content.ReadFromJsonAsync<JsonObject>())!;

        Assert.Equal("Compressarr SD-HD copy", first["name"]!.GetValue<string>());
        Assert.Equal("Compressarr SD-HD copy 2", second["name"]!.GetValue<string>());
        Assert.False((await host.Client.GetFromJsonAsync<JsonObject>(Url("Compressarr SD-HD copy")))!["builtIn"]!.GetValue<bool>());
    }

    [Fact]
    public async Task Delete_RemovesAnUnusedProfile_RefusesABuiltIn_AndRefusesOneALaneUses()
    {
        await using var host = await QueueHost.StartAsync(Lane1);
        (await CreateAsync(host, "Spare")).EnsureSuccessStatusCode();
        (await CreateAsync(host, "In use")).EnsureSuccessStatusCode();
        var lane = (await host.Client.GetFromJsonAsync<JsonArray>("/api/lanes"))![0]!.AsObject();
        lane["moviePreset"] = "In use";
        (await host.Client.PutAsJsonAsync("/api/lanes/lane1", lane)).EnsureSuccessStatusCode();

        Assert.Equal(HttpStatusCode.OK, (await host.Client.DeleteAsync(Url("Spare"))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await host.Client.GetAsync(Url("Spare"))).StatusCode);

        using var builtIn = await host.Client.DeleteAsync(Url("Compressarr SD-HD"));
        Assert.Equal(HttpStatusCode.BadRequest, builtIn.StatusCode);

        using var used = await host.Client.DeleteAsync(Url("In use"));
        Assert.Equal(HttpStatusCode.Conflict, used.StatusCode);
        Assert.Contains("Lane One", await used.Content.ReadAsStringAsync());
        Assert.NotNull(host.Services.GetRequiredService<IHandBrakeProfileStore>().Find("In use"));
    }

    [Fact]
    public async Task StoredPreset_IsTheRawPresetAsKept()
    {
        await using var host = await QueueHost.StartAsync(Lane1);

        var stored = (await host.Client.GetFromJsonAsync<JsonObject>(Url("Compressarr SD-HD") + "/stored"))!;

        Assert.Equal("Compressarr SD-HD", stored["PresetName"]!.GetValue<string>());
        Assert.NotNull(stored["PictureDeblockPreset"]); // the keys the editor doesn't show are all there
    }

    // ---- import ---------------------------------------------------------------------------------

    private static string WriteSource(QueueHost host, params (string Name, int Rf)[] presets)
    {
        var leaves = new JsonArray(presets.Select(p => (JsonNode)new JsonObject
        {
            ["PresetName"] = p.Name, ["FileFormat"] = "av_mkv", ["Folder"] = false, ["Type"] = 1,
            ["VideoEncoder"] = "x264", ["VideoQualityType"] = 2, ["VideoQualitySlider"] = p.Rf
        }).ToArray());
        var path = Path.Combine(host.Root, "hb-presets.json");
        File.WriteAllText(path, new JsonObject
        {
            ["PresetList"] = new JsonArray(new JsonObject { ["PresetName"] = "General", ["Folder"] = true, ["ChildrenArray"] = leaves })
        }.ToJsonString());
        return path;
    }

    [Fact]
    public async Task Import_FromAFile_ListsThenCopiesTheChosenOnes()
    {
        await using var host = await QueueHost.StartAsync(Lane1);
        var path = WriteSource(host, ("Fast 1080p30", 22), ("Very Fast 480p30", 22));

        var listing = (await (await host.Client.PostAsJsonAsync("/api/profiles/import/read", new JsonObject { ["path"] = path })).Content.ReadFromJsonAsync<JsonObject>())!;
        Assert.True(listing["found"]!.GetValue<bool>());
        Assert.Equal(2, listing["candidates"]!.AsArray().Count);
        Assert.Equal("new", listing["candidates"]![0]!["status"]!.GetValue<string>());
        Assert.Equal("General", listing["candidates"]![0]!["group"]!.GetValue<string>());

        var result = (await (await host.Client.PostAsJsonAsync("/api/profiles/import", new JsonObject
        {
            ["path"] = path, ["names"] = new JsonArray("Fast 1080p30"), ["onConflict"] = "keepBoth"
        })).Content.ReadFromJsonAsync<JsonObject>())!;

        Assert.Equal("Fast 1080p30", result["imported"]![0]!.GetValue<string>());
        var names = (await host.Client.GetFromJsonAsync<string[]>("/api/presets"))!;
        Assert.Contains("Fast 1080p30", names);
        Assert.DoesNotContain("Very Fast 480p30", names);
    }

    [Fact]
    public async Task Import_FromAMissingOrWrongFile_SaysSo()
    {
        await using var host = await QueueHost.StartAsync(Lane1);
        var wrong = Path.Combine(host.Root, "wrong.json");
        File.WriteAllText(wrong, "{\"a\":1}");

        var missing = (await (await host.Client.PostAsJsonAsync("/api/profiles/import/read", new JsonObject { ["path"] = Path.Combine(host.Root, "nope.json") })).Content.ReadFromJsonAsync<JsonObject>())!;
        Assert.False(missing["found"]!.GetValue<bool>());

        var notPresets = (await (await host.Client.PostAsJsonAsync("/api/profiles/import/read", new JsonObject { ["path"] = wrong })).Content.ReadFromJsonAsync<JsonObject>())!;
        Assert.False(notPresets["found"]!.GetValue<bool>());
        Assert.Contains("not a HandBrake presets file", notPresets["error"]!.GetValue<string>());

        using var bad = await host.Client.PostAsJsonAsync("/api/profiles/import", new JsonObject { ["path"] = wrong, ["names"] = new JsonArray("x"), ["onConflict"] = "skip" });
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
    }

    [Fact]
    public async Task Import_FromInstalledHandBrake_ReadsTheOldPresetsPathSetting()
    {
        await using var host = await QueueHost.StartAsync(Lane1);

        var configStore = host.Services.GetRequiredService<IConfigStore>();
        configStore.Update(AppPaths.GetConfigFilePath(), c => { c.HandBrake.PresetsPath = Path.Combine(host.Root, "no-such-presets.json"); return true; });

        var none = (await host.Client.GetFromJsonAsync<JsonObject>("/api/profiles/import/installed"))!;
        Assert.False(none["found"]!.GetValue<bool>()); // nothing at the configured path

        var path = WriteSource(host, ("From the app", 20));
        configStore.Update(AppPaths.GetConfigFilePath(), c => { c.HandBrake.PresetsPath = path; return true; });

        var found = (await host.Client.GetFromJsonAsync<JsonObject>("/api/profiles/import/installed"))!;
        Assert.True(found["found"]!.GetValue<bool>());
        Assert.Equal("From the app", found["candidates"]![0]!["name"]!.GetValue<string>());
    }

    [Fact]
    public async Task ProfilesList_ReportsTheActiveFilePathForTheEditorsCommandPreview()
    {
        await using var host = await QueueHost.StartAsync(Lane1);

        var dto = (await host.Client.GetFromJsonAsync<JsonObject>("/api/profiles"))!;

        Assert.EndsWith("handbrake-active.json", dto["activePresetsPath"]!.GetValue<string>());
    }
}
