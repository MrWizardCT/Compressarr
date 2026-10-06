using Compressarr.Core.Presets;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Compressarr.Web.Endpoints;

public static class PresetEndpoints
{
    public static void MapPresetEndpoints(this IEndpointRouteBuilder app)
    {
        // The names a lane or queued file can pick: Compressarr's own profiles (built-ins plus the
        // user's). There is no path to give - Compressarr no longer reads HandBrake's presets.json
        // at run time; the Profiles page's Import is how presets get in from it.
        app.MapGet("/api/presets", (IEncoderPresetService presetService) => Results.Json(presetService.GetPresetNames()));
    }
}
