using System.Collections.Concurrent;
using System.Text.Json;
using PacCollector.Domain.Entities;
using PacCollector.Domain.Errors;
using PacCollector.Domain.Ports;
using PacCollector.Domain.ValueObjects;
using PacCollector.Infrastructure.Config;

namespace PacCollector.Infrastructure.Persistence;

// persiste la lista de instrumentos como JSON; ConcurrentDictionary + semaforo para escribir
public sealed class JsonInstrumentRepository : IInstrumentRepository
{
    private readonly string _path;
    private readonly ConcurrentDictionary<string, Instrument> _bySerial = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    private JsonInstrumentRepository(string path) => _path = path;

    public static JsonInstrumentRepository Load(string path)
    {
        var repo = new JsonInstrumentRepository(path);
        if (!File.Exists(path)) return repo;

        try
        {
            var raw = File.ReadAllText(path);
            var list = JsonSerializer.Deserialize<List<InstrumentRecord>>(raw, JsonOptions.Default);
            if (list is null) return repo;
            foreach (var rec in list)
                repo._bySerial[rec.Serial] = rec.ToEntity();
        }
        catch
        {
            TryBackupCorrupt(path);
        }
        return repo;
    }

    public async Task UpsertOnContactAsync(Instrument instrument, CancellationToken ct = default)
    {
        _bySerial[instrument.Serial.AsString] = instrument;
        await PersistAsync(ct);
    }

    public Task<Instrument?> FindBySerialAsync(string serial, CancellationToken ct = default)
    {
        _bySerial.TryGetValue(serial, out var inst);
        return Task.FromResult<Instrument?>(inst);
    }

    public async Task UpdateAliasAsync(string serial, string? alias, CancellationToken ct = default)
    {
        if (!_bySerial.TryGetValue(serial, out var inst))
            throw new InstrumentNotFoundException(serial);
        inst.SetAlias(alias);
        await PersistAsync(ct);
    }

    public Task<IReadOnlyList<Instrument>> ListAllAsync(CancellationToken ct = default)
    {
        var snapshot = _bySerial.Values
            .OrderBy(i => i.Serial.AsString, StringComparer.Ordinal)
            .ToList();
        return Task.FromResult<IReadOnlyList<Instrument>>(snapshot);
    }

    public async Task IncrementSampleCountAsync(string serial, CancellationToken ct = default)
    {
        if (_bySerial.TryGetValue(serial, out var inst))
            inst.IncrementTotalSamples();
        await PersistAsync(ct);
    }

    private async Task PersistAsync(CancellationToken ct)
    {
        await _writeLock.WaitAsync(ct);
        try
        {
            var snapshot = _bySerial.Values
                .OrderBy(i => i.Serial.AsString, StringComparer.Ordinal)
                .Select(InstrumentRecord.FromEntity)
                .ToList();
            var json = JsonSerializer.Serialize(snapshot, JsonOptions.Pretty);

            var parent = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(parent))
                Directory.CreateDirectory(parent);

            var tmp = $"{_path}.{Guid.NewGuid():N}.tmp";
            try
            {
                await using (var fs = new FileStream(
                    tmp, FileMode.Create, FileAccess.Write, FileShare.None,
                    bufferSize: 4096, FileOptions.WriteThrough | FileOptions.Asynchronous))
                {
                    var bytes = System.Text.Encoding.UTF8.GetBytes(json);
                    await fs.WriteAsync(bytes, ct);
                    await fs.FlushAsync(ct);
                    fs.Flush(flushToDisk: true);
                }
                File.Move(tmp, _path, overwrite: true);
            }
            catch
            {
                try { if (File.Exists(tmp)) File.Delete(tmp); } catch { /* best-effort cleanup */ }
                throw;
            }
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private static void TryBackupCorrupt(string path)
    {
        try
        {
            var ts = DateTimeOffset.UtcNow.ToString("yyyyMMdd_HHmmss");
            var name = Path.GetFileName(path);
            var backup = Path.Combine(Path.GetDirectoryName(path) ?? "", $"{name}.broken-{ts}");
            File.Move(path, backup, overwrite: false);
        }
        catch { /* nothing more to do */ }
    }

    // record JSON-friendly para persistir (entidad mutable no serializa limpio con records)
    private sealed record InstrumentRecord(
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
        public static InstrumentRecord FromEntity(Instrument i) => new(
            i.Serial.AsString, i.AnalyzerType, i.Alias, i.LastIp, i.Firmware,
            i.FirstSeenAt, i.LastSeenAt, i.TotalSamples, i.Enabled);

        public Instrument ToEntity() => new(
            AnalyzerSerial.Create(Serial), AnalyzerType, LastIp, Firmware,
            FirstSeenAt, LastSeenAt, Alias, TotalSamples, Enabled);
    }
}
