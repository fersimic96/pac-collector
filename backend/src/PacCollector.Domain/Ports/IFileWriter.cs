using PacCollector.Domain.Entities;

namespace PacCollector.Domain.Ports;

public sealed record UnknownPayloadSaved(string Path);

public interface IFileWriter
{
    Task WriteSampleArtifactsAsync(Sample sample, CancellationToken ct = default);

    Task<UnknownPayloadSaved> WriteUnknownPayloadAsync(
        ReadOnlyMemory<byte> raw,
        string? analyzerType,
        string? sourceIp,
        string reason,
        DateTimeOffset receivedAt,
        CancellationToken ct = default);
}
