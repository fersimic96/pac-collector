using PacCollector.Domain.Entities;

namespace PacCollector.Application.Dtos;

public sealed record CurvePointDto(double PctRecovered, double TemperatureC);

public sealed record SampleOutputDto(
    string Uuid,
    string Serial,
    string AnalyzerType,
    string SampleIdentifier,
    string? Operator,
    string? Program,
    DateTimeOffset? StartAt,
    DateTimeOffset? EndAt,
    double? Ibp,
    double? Fbp,
    double? Residue,
    double? Recovery,
    double? FbpVolume,
    bool? EndOfTest,
    ulong? AlarmBitmask,
    IReadOnlyList<CurvePointDto> Curve,
    IReadOnlyDictionary<string, string> Extra,
    string? SourceIp,
    DateTimeOffset ReceivedAt)
{
    public static SampleOutputDto FromEntity(Sample s) => new(
        Uuid: s.Uuid,
        Serial: s.Serial.AsString,
        AnalyzerType: s.AnalyzerType,
        SampleIdentifier: s.SampleIdentifier,
        Operator: s.Operator,
        Program: s.Program,
        StartAt: s.StartAt,
        EndAt: s.EndAt,
        Ibp: s.Ibp,
        Fbp: s.Fbp,
        Residue: s.Residue,
        Recovery: s.Recovery,
        FbpVolume: s.FbpVolume,
        EndOfTest: s.EndOfTest,
        AlarmBitmask: s.AlarmBitmask,
        Curve: s.Curve.Points.Select(p => new CurvePointDto(p.PctRecovered, p.TemperatureC)).ToList(),
        Extra: new Dictionary<string, string>(s.Extra),
        SourceIp: s.SourceIp,
        ReceivedAt: s.ReceivedAt);
}
