using PacCollector.Api.Services;

namespace PacCollector.Api.Endpoints;

public static class NetworkEndpoints
{
    public static void MapNetworkEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/network/local-ips", (NetworkInfoService net) =>
            Results.Ok(net.ListLocalIps()));

        app.MapGet("/api/network/interfaces", (NetworkInfoService net) =>
            Results.Ok(net.ListInterfaces()));
    }
}
