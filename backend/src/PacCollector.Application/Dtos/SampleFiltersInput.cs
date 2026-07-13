namespace PacCollector.Application.Dtos;

public sealed record SampleFiltersInput(
    string? Serial = null,
    string? Program = null,
    string? Operator = null,
    DateTimeOffset? From = null,
    DateTimeOffset? To = null);
