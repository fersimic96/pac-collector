using PacCollector.Domain.ValueObjects;

namespace PacCollector.Domain.Entities;

public sealed class Instrument
{
    public AnalyzerSerial Serial { get; }
    public string AnalyzerType { get; set; }
    public string? Alias { get; private set; }
    public string? LastIp { get; set; }
    public string? Firmware { get; set; }
    public DateTimeOffset FirstSeenAt { get; }
    public DateTimeOffset LastSeenAt { get; set; }
    private ulong _totalSamples;
    public ulong TotalSamples
    {
        get => _totalSamples;
        set => _totalSamples = value;
    }
    public bool Enabled { get; set; }

    // increment atomico para el caso concurrente de muestras del mismo serial
    public ulong IncrementTotalSamples() => Interlocked.Increment(ref _totalSamples);

    public Instrument(
        AnalyzerSerial serial,
        string analyzerType,
        string? lastIp,
        string? firmware,
        DateTimeOffset firstSeenAt,
        DateTimeOffset lastSeenAt,
        string? alias,
        ulong totalSamples,
        bool enabled)
    {
        Serial = serial;
        AnalyzerType = analyzerType;
        LastIp = lastIp;
        Firmware = firmware;
        FirstSeenAt = firstSeenAt;
        LastSeenAt = lastSeenAt;
        Alias = alias;
        _totalSamples = totalSamples;
        Enabled = enabled;
    }

    public static Instrument NewDiscovered(
        AnalyzerSerial serial,
        string analyzerType,
        string? ip,
        DateTimeOffset now)
        => new(serial, analyzerType, ip, firmware: null, firstSeenAt: now, lastSeenAt: now,
               alias: null, totalSamples: 0, enabled: true);

    public void Touch(string? ip, DateTimeOffset now)
    {
        LastSeenAt = now;
        if (ip is not null)
            LastIp = ip;
    }

    public bool IsOnline(DateTimeOffset now, TimeSpan threshold)
        => (now - LastSeenAt) < threshold;

    public bool CanBeDeleted() => false;

    public string DisplayName()
        => !string.IsNullOrEmpty(Alias) ? Alias : $"{Serial} ({AnalyzerType})";

    public void SetAlias(string? alias)
    {
        if (alias is null)
        {
            Alias = null;
            return;
        }
        var trimmed = alias.Trim();
        Alias = trimmed.Length == 0 ? null : trimmed;
    }
}
