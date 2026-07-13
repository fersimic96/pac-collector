using PacCollector.Api.Services;
using PacCollector.Domain.Ports;

namespace PacCollector.Api.Endpoints;

public sealed record SetPluginEnabledRequest(bool Enabled);

public static class PluginEndpoints
{
    public static void MapPluginEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/plugins", (IPluginRegistry plugins) => Results.Ok(plugins.List()));

        app.MapPatch("/api/plugins/{id}/enabled",
            (string id, SetPluginEnabledRequest req, IPluginRegistry plugins) =>
            {
                plugins.SetEnabled(id, req.Enabled);
                return Results.NoContent();
            });

        // ── upload / delete / reload ──

        // POST /api/plugins/lims  body = JSON spec  (Content-Type: application/json)
        app.MapPost("/api/plugins/lims", async (HttpRequest req, PluginUploadService uploads) =>
        {
            using var reader = new StreamReader(req.Body);
            var raw = await reader.ReadToEndAsync();
            var result = uploads.Upload(PluginKind.Lims, raw);
            return result.Ok
                ? Results.Created(result.SavedPath ?? "/api/plugins", result)
                : Results.BadRequest(result);
        });

        // POST /api/plugins/print
        app.MapPost("/api/plugins/print", async (HttpRequest req, PluginUploadService uploads) =>
        {
            using var reader = new StreamReader(req.Body);
            var raw = await reader.ReadToEndAsync();
            var result = uploads.Upload(PluginKind.Print, raw);
            return result.Ok
                ? Results.Created(result.SavedPath ?? "/api/plugins", result)
                : Results.BadRequest(result);
        });

        // DELETE /api/plugins/lims/{id}  — solo override-dir, los embedded no se pueden borrar
        app.MapDelete("/api/plugins/lims/{id}", (string id, PluginUploadService uploads) =>
        {
            var result = uploads.Delete(PluginKind.Lims, id);
            return result.Ok ? Results.Ok(result) : Results.NotFound(result);
        });

        app.MapDelete("/api/plugins/print/{id}", (string id, PluginUploadService uploads) =>
        {
            var result = uploads.Delete(PluginKind.Print, id);
            return result.Ok ? Results.Ok(result) : Results.NotFound(result);
        });

        // POST /api/plugins/reload  — recarga el registry desde los override dirs
        app.MapPost("/api/plugins/reload", (PluginUploadService uploads) =>
        {
            var result = uploads.Reload();
            return result.Ok ? Results.Ok(result) : Results.StatusCode(StatusCodes.Status500InternalServerError);
        });
    }
}
