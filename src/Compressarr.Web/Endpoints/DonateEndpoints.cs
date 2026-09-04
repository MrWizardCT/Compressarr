using Compressarr.Core.Donations;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Compressarr.Web.Endpoints;

public static class DonateEndpoints
{
    public static void MapDonateEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/donate/addresses", () => Results.Json(DonationAddresses.All));
    }
}
