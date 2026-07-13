using PacCollector.Application.Services;

namespace PacCollector.Application.UseCases;

public sealed class ReceivePrintUseCase
{
    private readonly SampleProcessingService _processing;

    public ReceivePrintUseCase(SampleProcessingService processing) => _processing = processing;

    public Task ExecuteAsync(
        ReadOnlyMemory<byte> raw,
        string? sourceIp,
        CancellationToken ct = default)
        => _processing.ProcessPrintMessageAsync(raw, sourceIp, ct);
}
