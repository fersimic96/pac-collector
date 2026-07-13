using PacCollector.Application.UseCases;
using PacCollector.Infrastructure.Config;

namespace PacCollector.Api.Endpoints;

public sealed record UpdateAliasRequest(string? Alias);
public sealed record SetRouteRequest(string? HotFolderFormat, string? HotFolderDir, string? Alias);

public static class InstrumentEndpoints
{
    public static void MapInstrumentEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/instruments", async (ListInstrumentsUseCase uc, CancellationToken ct) =>
            Results.Ok(await uc.ExecuteAsync(ct)));

        app.MapPatch("/api/instruments/{serial}/alias",
            async (string serial, UpdateAliasRequest req, UpdateInstrumentAliasUseCase uc, CancellationToken ct) =>
            {
                try
                {
                    await uc.ExecuteAsync(serial, req.Alias, ct);
                    return Results.NoContent();
                }
                catch (PacCollector.Domain.Errors.InstrumentNotFoundException e)
                {
                    return Results.NotFound(new { error = e.Message });
                }
            });

        app.MapPatch("/api/instruments/{serial}/route",
            (string serial, SetRouteRequest req, ConfigStore config) =>
            {
                var cfg = config.Snapshot();
                var route = new InstrumentRoute
                {
                    HotFolderFormat = ParseFormat(req.HotFolderFormat),
                    HotFolderDir = req.HotFolderDir,
                    Alias = req.Alias,
                };
                cfg.InstrumentRoutes[serial] = route;
                config.Replace(cfg);
                return Results.NoContent();
            });
    }

    private static HotFolderFormat? ParseFormat(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        return raw.ToLowerInvariant() switch
        {
            "limsethernet" or "lims_ethernet" or "lims-ethernet" => HotFolderFormat.LimsEthernet,
            "csvall" or "csv_all" or "csv-all" => HotFolderFormat.CsvAll,
            "csv" => HotFolderFormat.Csv,
            _ => null,
        };
    }
}
