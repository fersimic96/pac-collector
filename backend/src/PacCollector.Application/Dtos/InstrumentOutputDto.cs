using PacCollector.Domain.Entities;

namespace PacCollector.Application.Dtos;

public sealed record InstrumentOutputDto(
    string Serial,
    string AnalyzerType,
    string? Alias,
    string? LastIp,
    string? Firmware,
    DateTimeOffset FirstSeenAt,
    DateTimeOffset LastSeenAt,
    ulong TotalSamples,
    bool Enabled)
{
    public static InstrumentOutputDto FromEntity(Instrument i) => new(
        Serial: i.Serial.AsString,
        AnalyzerType: i.AnalyzerType,
        Alias: i.Alias,
        LastIp: i.LastIp,
        Firmware: i.Firmware,
        FirstSeenAt: i.FirstSeenAt,
        LastSeenAt: i.LastSeenAt,
        TotalSamples: i.TotalSamples,
        Enabled: i.Enabled);
}
