namespace PacCollector.Application.Dtos;

public sealed record SamplePage(
    IReadOnlyList<SampleOutputDto> Items,
    ulong Total,
    uint Offset,
    uint Limit);
