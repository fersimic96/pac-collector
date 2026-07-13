using PacCollector.Domain.Ports;

namespace PacCollector.Application.UseCases;

public sealed class UpdateInstrumentAliasUseCase
{
    private readonly IInstrumentRepository _instruments;

    public UpdateInstrumentAliasUseCase(IInstrumentRepository instruments) => _instruments = instruments;

    public Task ExecuteAsync(string serial, string? alias, CancellationToken ct = default)
    {
        string? cleaned = null;
        if (alias is not null)
        {
            var trimmed = alias.Trim();
            if (trimmed.Length > 0) cleaned = trimmed;
        }
        return _instruments.UpdateAliasAsync(serial, cleaned, ct);
    }
}
