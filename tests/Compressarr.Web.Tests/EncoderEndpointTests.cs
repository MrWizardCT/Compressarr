using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Compressarr.Core.Presets;
using Microsoft.Extensions.DependencyInjection;

namespace Compressarr.Web.Tests;

/// <summary>The Encoder and Profiles pages through the real HTTP surface: the tool settings that
/// moved out of Settings, the profile list (Compressarr's own built-ins plus the user's), the preset
/// names lanes and the queue pick from, and the old presets.json path/Install/Reload endpoints being
/// gone.</summary>
public class EncoderEndpointTests
{
    private static readonly LaneSpec Lane1 = new("lane1", "Lane One");

    private static JsonObject UserProfile(string name) => new()
    {
        ["PresetName"] = name,
        ["FileFormat"] = "av_mp4",
        ["Folder"] = false
    };

    [Fact]
    public async Task GetEncoder_ReportsTheToolSettings_TheProfileCounts_AndAMissingCli()
    {
        await using var host = await QueueHost.StartAsync(Lane1);
        // whether the default HandBrakeCLI location exists depends on the machine running the tests
        var missing = (await host.Client.GetFromJsonAsync<JsonObject>("/api/encoder"))!;
        missing["handBrakeCliPath"] = Path.Combine(host.Root, "nowhere", "HandBrakeCLI.exe");
        (await host.Client.PutAsJsonAsync("/api/encoder", missing)).EnsureSuccessStatusCode();

        var dto = (await host.Client.GetFromJsonAsync<JsonObject>("/api/encoder"))!;

        Assert.False(string.IsNullOrWhiteSpace(dto["handBrakeCliPath"]!.GetValue<string>()));
        Assert.Equal("", dto["handBrakeOptions"]!.GetValue<string>());
        Assert.Equal(2, dto["builtInProfileCount"]!.GetValue<int>());
        Assert.Equal(0, dto["userProfileCount"]!.GetValue<int>());
        Assert.Contains("Lane One", dto["handBrakeLanes"]!.AsArray().Select(n => n!.GetValue<string>()));
        // the CLI isn't where the settings say, and the page must say so
        Assert.Contains(dto["validationIssues"]!.AsArray(), i => i!["field"]!.GetValue<string>() == "handBrakeCliPath");
    }

    [Fact]
    public async Task PutEncoder_SavesThePathAndOptions_AndValidatesThem()
    {
        await using var host = await QueueHost.StartAsync(Lane1);
        var cli = Path.Combine(host.Root, "HandBrakeCLI.exe");
        File.WriteAllText(cli, "");

        var dto = (await host.Client.GetFromJsonAsync<JsonObject>("/api/encoder"))!;
        dto["handBrakeCliPath"] = cli;
        dto["handBrakeOptions"] = "--two-pass";
        using var response = await host.Client.PutAsJsonAsync("/api/encoder", dto);
        var saved = (await response.Content.ReadFromJsonAsync<JsonObject>())!;

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(saved["validationIssues"]!.AsArray());
        var reread = (await host.Client.GetFromJsonAsync<JsonObject>("/api/encoder"))!;
        Assert.Equal(cli, reread["handBrakeCliPath"]!.GetValue<string>());
        Assert.Equal("--two-pass", reread["handBrakeOptions"]!.GetValue<string>());

        dto["handBrakeOptions"] = "--custom \"unterminated";
        using var bad = await host.Client.PutAsJsonAsync("/api/encoder", dto);
        var flagged = (await bad.Content.ReadFromJsonAsync<JsonObject>())!;
        Assert.Contains(flagged["validationIssues"]!.AsArray(), i => i!["field"]!.GetValue<string>() == "handBrakeOptions");
    }

    [Fact]
    public async Task Settings_NoLongerCarriesTheHandBrakeFields_AndSavingItLeavesThemAlone()
    {
        await using var host = await QueueHost.StartAsync(Lane1);
        var encoder = (await host.Client.GetFromJsonAsync<JsonObject>("/api/encoder"))!;
        encoder["handBrakeCliPath"] = @"C:\Tools\HandBrakeCLI.exe";
        encoder["handBrakeOptions"] = "--two-pass";
        (await host.Client.PutAsJsonAsync("/api/encoder", encoder)).EnsureSuccessStatusCode();

        var settings = (await host.Client.GetFromJsonAsync<JsonObject>("/api/settings"))!;
        Assert.False(settings.ContainsKey("presetsPath"));
        Assert.False(settings.ContainsKey("handBrakeCliPath"));
        Assert.False(settings.ContainsKey("handBrakeOptions"));
        Assert.DoesNotContain(settings["validationIssues"]!.AsArray(), i => i!["field"]!.GetValue<string>() == "handBrakeCliPath");

        // Saving the Settings page (which no longer sends those fields) must not reset them
        (await host.Client.PutAsJsonAsync("/api/settings", settings)).EnsureSuccessStatusCode();
        var after = (await host.Client.GetFromJsonAsync<JsonObject>("/api/encoder"))!;
        Assert.Equal(@"C:\Tools\HandBrakeCLI.exe", after["handBrakeCliPath"]!.GetValue<string>());
        Assert.Equal("--two-pass", after["handBrakeOptions"]!.GetValue<string>());
    }

    [Fact]
    public async Task Presets_AreTheBuiltInsPlusYours_WithNoPathToGive()
    {
        await using var host = await QueueHost.StartAsync(Lane1);
        host.Services.GetRequiredService<IHandBrakeProfileStore>().AddUserProfiles(new[] { UserProfile("Cartoons x265") });

        var names = (await host.Client.GetFromJsonAsync<string[]>("/api/presets"))!;
        var withStaleQuery = (await host.Client.GetFromJsonAsync<string[]>("/api/presets?path=C%3A%5Cold%5Cpresets.json"))!;

        Assert.Equal(new[] { "Cartoons x265", "Compressarr SD-HD", "Compressarr UHD AV1" }, names);
        Assert.Equal(names, withStaleQuery); // a cached 2.1 page still sending ?path= just gets the answer
    }

    [Fact]
    public async Task Profiles_ListsBuiltInsLocked_YoursUnlocked_AndWhichLanesUseEach()
    {
        await using var host = await QueueHost.StartAsync(Lane1);
        host.Services.GetRequiredService<IHandBrakeProfileStore>().AddUserProfiles(new[] { UserProfile("Cartoons x265") });
        var lanes = (await host.Client.GetFromJsonAsync<JsonArray>("/api/lanes"))!;
        var lane = lanes[0]!.AsObject();
        lane["moviePreset"] = "Compressarr SD-HD";
        lane["tvPreset"] = "Cartoons x265";
        (await host.Client.PutAsJsonAsync("/api/lanes/lane1", lane)).EnsureSuccessStatusCode();

        var dto = (await host.Client.GetFromJsonAsync<JsonObject>("/api/profiles"))!;
        var rows = dto["profiles"]!.AsArray().Select(r => r!.AsObject()).ToList();

        Assert.Equal(3, rows.Count);
        var builtIn = rows.Single(r => r["name"]!.GetValue<string>() == "Compressarr SD-HD");
        Assert.True(builtIn["builtIn"]!.GetValue<bool>());
        Assert.Equal("handbrake", builtIn["engine"]!.GetValue<string>());
        Assert.Equal("MKV", builtIn["container"]!.GetValue<string>());
        Assert.StartsWith("x265 10-bit", builtIn["video"]!.GetValue<string>());
        Assert.Equal(new[] { "Lane One" }, builtIn["usedBy"]!.AsArray().Select(n => n!.GetValue<string>()));

        var mine = rows.Single(r => r["name"]!.GetValue<string>() == "Cartoons x265");
        Assert.False(mine["builtIn"]!.GetValue<bool>());
        Assert.Equal("MP4", mine["container"]!.GetValue<string>());
        Assert.Equal(new[] { "Lane One" }, mine["usedBy"]!.AsArray().Select(n => n!.GetValue<string>()));

        Assert.Empty(rows.Single(r => r["name"]!.GetValue<string>() == "Compressarr UHD AV1")["usedBy"]!.AsArray());
        Assert.EndsWith("handbrake-profiles.json", dto["userFilePath"]!.GetValue<string>());
        Assert.Null(dto["userFileError"]);
    }

    [Theory]
    [InlineData("POST", "/api/presets/install")]
    [InlineData("POST", "/api/presets/reload")]
    [InlineData("GET", "/api/presets/status")]
    public async Task TheOldPresetsJsonPathEndpoints_AreGone(string method, string url)
    {
        await using var host = await QueueHost.StartAsync(Lane1);

        using var response = await host.Client.SendAsync(new HttpRequestMessage(new HttpMethod(method), url));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task LaneValidation_FlagsAnUnknownPreset_AgainstCompressarrsOwnProfiles()
    {
        await using var host = await QueueHost.StartAsync(Lane1); // its lane names presets that don't exist

        var lanes = (await host.Client.GetFromJsonAsync<JsonArray>("/api/lanes"))!;
        var issues = lanes[0]!["validationIssues"]!.AsArray().Select(i => i!["message"]!.GetValue<string>()).ToList();

        Assert.Contains(issues, m => m.Contains("Compressarr's profiles"));
        Assert.DoesNotContain(issues, m => m.Contains("presets.json"));
    }

    [Fact]
    public async Task ExportCarriesYourProfiles_AndImportingItOnANewInstallBringsThemBack()
    {
        string export;
        await using (var first = await QueueHost.StartAsync(Lane1))
        {
            first.Services.GetRequiredService<IHandBrakeProfileStore>().AddUserProfiles(new[] { UserProfile("Cartoons x265") });
            export = await first.Client.GetStringAsync("/api/settings/export");
        }

        var exported = JsonNode.Parse(export)!.AsObject();
        Assert.Equal("Cartoons x265", exported["handBrakeUserProfiles"]![0]!["PresetName"]!.GetValue<string>());

        await using var second = await QueueHost.StartAsync(Lane1);
        Assert.DoesNotContain("Cartoons x265", await second.Client.GetFromJsonAsync<string[]>("/api/presets") ?? Array.Empty<string>());

        using var response = await second.Client.PostAsync("/api/settings/import", new StringContent(export, Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Cartoons x265", (await second.Client.GetFromJsonAsync<string[]>("/api/presets"))!);
    }

    [Fact]
    public async Task ImportingAnOldExportWithoutProfiles_StillWorks()
    {
        await using var host = await QueueHost.StartAsync(Lane1);
        var export = JsonNode.Parse(await host.Client.GetStringAsync("/api/settings/export"))!.AsObject();
        export.Remove("handBrakeUserProfiles");

        using var response = await host.Client.PostAsync("/api/settings/import", new StringContent(export.ToJsonString(), Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [InlineData("/api/encoder")]
    [InlineData("/api/profiles")]
    [InlineData("/api/presets")]
    public async Task NewGetEndpoints_ReturnValidJson(string url)
    {
        await using var host = await QueueHost.StartAsync(Lane1);

        using var response = await host.Client.GetAsync(url);
        var body = await response.Content.ReadAsStringAsync();

        Assert.True(response.StatusCode == HttpStatusCode.OK, $"{url} returned {(int)response.StatusCode}: {body}");
        using var _ = JsonDocument.Parse(body);
    }
}
