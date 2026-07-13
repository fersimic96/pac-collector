using PacCollector.Application.Dtos;
using PacCollector.Application.UseCases;
using PacCollector.Domain.Ports;

namespace PacCollector.Api.Endpoints;

public sealed record SampleSearchRequest(
    string? Serial,
    string? Program,
    string? Operator,
    DateTimeOffset? From,
    DateTimeOffset? To,
    uint Offset = 0,
    uint Limit = 50);

public static class SampleEndpoints
{
    public static void MapSampleEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/samples/{uuid}", async (string uuid, ISampleRepository repo, CancellationToken ct) =>
        {
            var sample = await repo.FindByUuidAsync(uuid, ct);
            return sample is null
                ? Results.NotFound(new { error = "sample not found", uuid })
                : Results.Ok(SampleOutputDto.FromEntity(sample));
        });

        app.MapPost("/api/samples/search", async (SampleSearchRequest req, ListSamplesUseCase uc, CancellationToken ct) =>
        {
            var filters = new SampleFiltersInput(
                Serial: req.Serial,
                Program: req.Program,
                Operator: req.Operator,
                From: req.From,
                To: req.To);
            var page = await uc.ExecuteAsync(filters, req.Offset, req.Limit, ct);
            return Results.Ok(page);
        });
    }
}
