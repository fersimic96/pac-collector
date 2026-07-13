using PacCollector.Application.UseCases;
using PacCollector.Domain.Ports;
using PacCollector.Infrastructure.Config;
using PacCollector.Infrastructure.Network;

namespace PacCollector.Api.Endpoints;

// shape consumido por el frontend ServerStatusDTO:
//   { serverIp, tcpPort, udpPort, instrumentsCount, samplesToday, running, printRunning, printPort }
// el TopBar.tsx del React lee status.running y los contadores; sin esto el boton
// Iniciar/Detener queda pegado y las metricas vacias.
public sealed record ServerStatusResponse(
    string ServerIp,
    ushort TcpPort,
    ushort UdpPort,
    int InstrumentsCount,
    ulong SamplesToday,
    bool Running,
    bool PrintRunning,
    ushort PrintPort);

public static class ListenerEndpoints
{
    public static void MapListenerEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/server/status", async (
            ListenerManager mgr,
            ConfigStore configStore,
            ListInstrumentsUseCase listInstruments,
            ISampleRepository samples,
            CancellationToken ct) =>
        {
            var cfg = configStore.Snapshot();
            var instruments = await listInstruments.ExecuteAsync(ct);
            var samplesToday = await samples.CountReceivedSinceAsync(DateTimeOffset.UtcNow.Date, ct);

            return Results.Ok(new ServerStatusResponse(
                ServerIp: cfg.General.SelectedIp ?? "auto",
                TcpPort: ListenerManager.TcpPort,
                UdpPort: ListenerManager.UdpPort,
                InstrumentsCount: instruments.Count,
                SamplesToday: samplesToday,
                Running: mgr.LimsRunning,
                PrintRunning: mgr.PrintRunning,
                PrintPort: cfg.General.PrintPort));
        });

        app.MapPost("/api/listeners/start", (ListenerManager mgr) =>
        {
            mgr.StartLims();
            return Results.NoContent();
        });

        app.MapPost("/api/listeners/stop", (ListenerManager mgr) =>
        {
            mgr.StopLims();
            return Results.NoContent();
        });

        app.MapPost("/api/print-listener/start", (ListenerManager mgr) =>
        {
            mgr.StartPrint();
            return Results.NoContent();
        });

        app.MapPost("/api/print-listener/stop", (ListenerManager mgr) =>
        {
            mgr.StopPrint();
            return Results.NoContent();
        });
    }
}
