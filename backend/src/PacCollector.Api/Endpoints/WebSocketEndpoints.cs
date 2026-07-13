using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using PacCollector.Domain.Ports;
using PacCollector.Infrastructure.EventBus;

namespace PacCollector.Api.Endpoints;

public static class WebSocketEndpoints
{
    public static void MapWebSocketEndpoints(this IEndpointRouteBuilder app)
    {
        app.Map("/api/events", async (HttpContext ctx, ChannelEventBus bus, CancellationToken serverShutdown) =>
        {
            if (!ctx.WebSockets.IsWebSocketRequest)
            {
                ctx.Response.StatusCode = StatusCodes.Status400BadRequest;
                return;
            }

            using var socket = await ctx.WebSockets.AcceptWebSocketAsync();
            using var subscription = bus.Subscribe(out var reader);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ctx.RequestAborted, serverShutdown);

            try
            {
                await foreach (var evt in reader.ReadAllAsync(linked.Token))
                {
                    if (socket.State != WebSocketState.Open) break;
                    var payload = SerializeEvent(evt);
                    await socket.SendAsync(
                        new ArraySegment<byte>(payload),
                        WebSocketMessageType.Text,
                        endOfMessage: true,
                        linked.Token);
                }
            }
            catch (OperationCanceledException) { /* client/server bajaron */ }
            catch (WebSocketException) { /* client cerro abruptamente */ }
            finally
            {
                if (socket.State == WebSocketState.Open)
                {
                    try { await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None); }
                    catch { /* best-effort */ }
                }
            }
        });
    }

    // serializa { type: "<EventName>", payload: {...} } para que el cliente JS pueda discriminar
    private static byte[] SerializeEvent(DomainEvent evt)
    {
        var typeName = evt.GetType().Name;
        var envelope = new { type = typeName, payload = (object)evt };
        var json = JsonSerializer.Serialize(envelope, JsonOptions);
        return Encoding.UTF8.GetBytes(json);
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };
}
