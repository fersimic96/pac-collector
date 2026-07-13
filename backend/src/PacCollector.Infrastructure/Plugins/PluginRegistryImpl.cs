using System.Collections.Concurrent;
using PacCollector.Domain.Entities;
using PacCollector.Domain.Ports;
using PacCollector.Infrastructure.Plugins.Builtin;
using PacCollector.Infrastructure.Plugins.Print;

namespace PacCollector.Infrastructure.Plugins;

// registro central de todos los plugins de equipos. Combina:
//   - plugins LIMS JSON: uno por cada PacInstrumentSpec builtin (7 equipos)
//   - plugins print: uno por cada PrintPluginSpec cargado de JSON
// FindForType: busca un plugin no-print por AnalyzerType
// FindForPrint: itera los plugins print preguntando AcceptsPrintFormat
public sealed class PluginRegistryImpl : IPluginRegistry
{
    // lock para que Reload swap sea atomico contra lecturas de FindForType/FindForPrint/List
    private readonly Lock _gate = new();
    private List<RegisteredPlugin> _plugins;

    // override dirs guardados para que Reload() pueda releer del mismo lugar
    private string? _limsOverrideDir;
    private string? _printOverrideDir;

    public PluginRegistryImpl(IEnumerable<IInstrumentPlugin> plugins)
    {
        _plugins = plugins.Select(p => new RegisteredPlugin(p, true)).ToList();
    }

    // factory recomendada: carga los specs LIMS y print desde JSON embebidos.
    // Si se pasa un override dir, los .json de disco pisan los embedded (por pluginId).
    // Esto permite agregar/cambiar equipos PAC sin tocar codigo.
    public static PluginRegistryImpl LoadBuiltin(
        string? limsPluginsOverrideDir = null,
        string? printPluginsOverrideDir = null)
    {
        var registry = new PluginRegistryImpl(LoadFromSpecs(limsPluginsOverrideDir, printPluginsOverrideDir))
        {
            _limsOverrideDir = limsPluginsOverrideDir,
            _printOverrideDir = printPluginsOverrideDir,
        };
        return registry;
    }

    private static IEnumerable<IInstrumentPlugin> LoadFromSpecs(string? limsDir, string? printDir)
    {
        foreach (var spec in PacInstrumentSpecLoader.LoadAll(limsDir))
            yield return new PacFamilyPlugin(spec);
        foreach (var spec in PrintPluginSpecLoader.LoadAll(printDir))
            yield return new ConfigurablePrintPlugin(spec);
    }

    // releere los specs desde los override dirs configurados y reemplaza el set
    // de plugins activos atomicamente. Preserva el estado enabled de plugins que
    // sobreviven al reload (matched por Id). Si el reload tira, NO toca el estado actual.
    public void Reload()
    {
        IReadOnlyDictionary<string, bool> previousEnabled;
        lock (_gate)
        {
            previousEnabled = _plugins.ToDictionary(p => p.Plugin.Id, p => p.Enabled, StringComparer.Ordinal);
        }

        // construir afuera del lock para que las lecturas no se bloqueen mientras parseamos JSON
        var fresh = LoadFromSpecs(_limsOverrideDir, _printOverrideDir)
            .Select(p => new RegisteredPlugin(p, previousEnabled.GetValueOrDefault(p.Id, defaultValue: true)))
            .ToList();

        lock (_gate)
        {
            _plugins = fresh;
        }
    }

    public IInstrumentPlugin? FindForType(string analyzerType)
    {
        List<RegisteredPlugin> snapshot;
        lock (_gate) snapshot = _plugins;
        foreach (var reg in snapshot)
        {
            if (!reg.Enabled) continue;
            if (reg.Plugin.IsPrintPlugin) continue;
            if (reg.Plugin.SupportedTypes.Contains(analyzerType, StringComparer.Ordinal))
                return reg.Plugin;
        }
        return null;
    }

    public IInstrumentPlugin? FindForPrint(ReadOnlyMemory<byte> raw)
    {
        List<RegisteredPlugin> snapshot;
        lock (_gate) snapshot = _plugins;
        foreach (var reg in snapshot)
        {
            if (!reg.Enabled) continue;
            if (!reg.Plugin.IsPrintPlugin) continue;
            if (reg.Plugin.AcceptsPrintFormat(raw)) return reg.Plugin;
        }
        return null;
    }

    // expone solo plugins LIMS JSON al listado del UI. Los print plugins son detalle
    // interno (se acceden via FindForPrint con sniff de bytes, no por AnalyzerType).
    // si se incluyeran, AnalyzerType dupplicaria entre LIMS y print (mismo equipo).
    public IReadOnlyList<PluginInfo> List()
    {
        List<RegisteredPlugin> snapshot;
        lock (_gate) snapshot = _plugins;
        return snapshot
            .Where(r => !r.Plugin.IsPrintPlugin)
            .Select(r => new PluginInfo(
                Id: r.Plugin.Id,
                DisplayName: r.Plugin.DisplayName,
                Version: r.Plugin.Version,
                Vendor: r.Plugin.Vendor,
                SupportedTypes: r.Plugin.SupportedTypes,
                Source: new PluginSource.Builtin(),
                Enabled: r.Enabled))
            .ToList();
    }

    public void SetEnabled(string id, bool enabled)
    {
        lock (_gate)
        {
            var reg = _plugins.FirstOrDefault(r => string.Equals(r.Plugin.Id, id, StringComparison.Ordinal));
            if (reg is not null) reg.Enabled = enabled;
        }
    }

    // ids de TODOS los plugins activos (LIMS y print). Lo usan los upload/reload
    // para verificar que un plugin recien subido aparecio en el registry.
    public IReadOnlyCollection<string> AllPluginIds()
    {
        lock (_gate)
            return _plugins.Select(r => r.Plugin.Id).ToList();
    }

    private sealed class RegisteredPlugin
    {
        public IInstrumentPlugin Plugin { get; }
        public bool Enabled { get; set; }
        public RegisteredPlugin(IInstrumentPlugin plugin, bool enabled)
        {
            Plugin = plugin;
            Enabled = enabled;
        }
    }
}
