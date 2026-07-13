using PacCollector.Api.Services;

namespace PacCollector.Api.Endpoints;

public static class SystemEndpoints
{
    public static void MapSystemEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/system/platform", (SystemService sys) =>
            Results.Ok(new { platform = sys.OsPlatform() }));

        app.MapPost("/api/system/open-network-settings", (SystemService sys) =>
        {
            var ok = sys.OpenNetworkSettings();
            return ok ? Results.NoContent() : Results.StatusCode(StatusCodes.Status501NotImplemented);
        });
    }
}
