using PacCollector.Infrastructure.Config;

namespace PacCollector.Api.Endpoints;

public static class ConfigEndpoints
{
    public static void MapConfigEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/config", (ConfigStore store) => Results.Ok(store.Snapshot()));

        app.MapPost("/api/config", (AppConfig cfg, ConfigStore store) =>
        {
            var errors = cfg.Validate();
            if (errors.Count > 0)
                return Results.BadRequest(new { errors });
            store.Replace(cfg);
            return Results.NoContent();
        });
    }
}
