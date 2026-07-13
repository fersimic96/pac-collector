using PacCollector.Domain.Entities;

namespace PacCollector.Domain.Ports;

public interface IPluginRegistry
{
    IInstrumentPlugin? FindForType(string analyzerType);
    IInstrumentPlugin? FindForPrint(ReadOnlyMemory<byte> raw);
    IReadOnlyList<PluginInfo> List();
    void SetEnabled(string id, bool enabled);
}
