using System.Globalization;
using System.Text;
using System.Text.Json;
using PacCollector.Domain.Entities;
using PacCollector.Domain.Errors;
using PacCollector.Domain.Ports;
using PacCollector.Domain.ValueObjects;
using PacCollector.Infrastructure.Plugins.Builtin;

namespace PacCollector.Infrastructure.Plugins;

// parsea el json LIMS Ethernet del equipo y extrae cada campo por nombre
public sealed class PacFamilyPlugin : IInstrumentPlugin
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    private static readonly UTF8Encoding StrictUtf8 = new(false, throwOnInvalidBytes: true);

    private static readonly HashSet<string> ReservedKeys = new(StringComparer.Ordinal)
    {
        "AnalyzerType", "AnalyzerSerialNumber", "SampleIdentifier",
        "OperatorId", "ProgramName",
        "StartRunDate", "StartRunTime", "EndRunDate", "EndRunTime",
        "IBP", "FBP", "Residue", "Recovery", "FBPvolume",
        "EndOfTest", "DuringRunAlarm",
    };

    private readonly PacInstrumentSpec _spec;
    private readonly IReadOnlyDictionary<string, FieldMeta> _fields;

    public PacFamilyPlugin(PacInstrumentSpec spec)
    {
        _spec = spec;
        var fields = new Dictionary<string, FieldMeta>(StringComparer.Ordinal);
        foreach (var f in BuiltinSpecs.CommonFields)
            fields[f.Key] = new FieldMeta(f.Label, f.Unit, f.Group);
        foreach (var f in spec.FieldSpecs)
            fields[f.Key] = new FieldMeta(f.Label, f.Unit, f.Group);
        _fields = fields;
    }

    public string Id => _spec.PluginId;
    public string DisplayName => _spec.DisplayName;
    public string Version => _spec.Version;
    public string Vendor => _spec.Vendor;
    public IReadOnlyList<string> SupportedTypes => new[] { _spec.AnalyzerType };
    public IReadOnlyDictionary<string, FieldMeta> FieldDescriptions => _fields;

    public Sample ParseMessage(ReadOnlyMemory<byte> raw, string? sourceIp, DateTimeOffset receivedAt)
    {
        string rawText;
        try { rawText = StrictUtf8.GetString(raw.Span); }
        catch (DecoderFallbackException e)
        {
            throw new MalformedMessageException($"invalid UTF-8: {e.Message}");
        }

        JsonDocument doc;
        try { doc = JsonDocument.Parse(rawText); }
        catch (JsonException e)
        {
            throw new MalformedMessageException($"invalid JSON: {e.Message}");
        }
        using (doc)
        {
            var root = doc.RootElement;
            var analyzerType = TryGetString(root, "AnalyzerType") ?? _spec.AnalyzerType;

            // DataDictionary o campos en root (sin AnalyzerType)
            var dd = ExtractDataDictionary(root);
            if (dd is null)
                throw new MalformedMessageException("no DataDictionary or root fields");

            var serialRaw = DdString(dd, "AnalyzerSerialNumber")
                ?? throw new MalformedMessageException("missing AnalyzerSerialNumber");
            var serial = AnalyzerSerial.Create(serialRaw);

            var sampleIdentifier = DdString(dd, "SampleIdentifier") ?? "";
            var operator_ = DdString(dd, "OperatorId");
            var program = DdString(dd, "ProgramName");

            var startAt = ParsePacDateTime(DdString(dd, "StartRunDate"), DdString(dd, "StartRunTime"));
            var endAt = ParsePacDateTime(DdString(dd, "EndRunDate"), DdString(dd, "EndRunTime"));

            var ibp = DdDouble(dd, "IBP");
            var fbp = DdDouble(dd, "FBP");
            var residue = DdDouble(dd, "Residue");
            var recovery = DdDouble(dd, "Recovery");
            var fbpVolume = DdDouble(dd, "FBPvolume");

            var endOfTestStr = DdString(dd, "EndOfTest");
            bool? endOfTest = endOfTestStr is null
                ? null
                : endOfTestStr.Trim() == "1" || endOfTestStr.Trim().Equals("true", StringComparison.OrdinalIgnoreCase);

            ulong? alarmBitmask = null;
            if (DdString(dd, "DuringRunAlarm") is { } alarm
                && ulong.TryParse(alarm.Trim(), NumberStyles.Integer, Inv, out var a))
                alarmBitmask = a;

            var points = new List<CurvePoint>();
            foreach (var (key, val) in dd)
            {
                if (!key.StartsWith("Recovered_", StringComparison.Ordinal)) continue;
                var rest = key["Recovered_".Length..];
                if (!double.TryParse(rest, NumberStyles.Float, Inv, out var pct)) continue;
                if (val is not string vs) continue;
                if (!ParsePacNumber(vs, out var temp)) continue;
                points.Add(new CurvePoint(pct, temp));
            }
            points.Sort((x, y) => x.PctRecovered.CompareTo(y.PctRecovered));
            DistillationCurve curve;
            try { curve = DistillationCurve.Create(points); }
            catch (InvalidCurvePointException) { curve = DistillationCurve.Empty(); }

            var extra = new SortedDictionary<string, string>(StringComparer.Ordinal);
            foreach (var (key, val) in dd)
            {
                if (ReservedKeys.Contains(key)) continue;
                if (key.StartsWith("Recovered_", StringComparison.Ordinal)) continue;
                extra[key] = val switch
                {
                    string s => s.Trim(),
                    _ => val?.ToString() ?? "",
                };
            }

            return new Sample
            {
                Uuid = Guid.NewGuid().ToString(),
                Serial = serial,
                AnalyzerType = analyzerType,
                SampleIdentifier = sampleIdentifier,
                Operator = operator_,
                Program = program,
                StartAt = startAt,
                EndAt = endAt,
                Ibp = ibp,
                Fbp = fbp,
                Residue = residue,
                Recovery = recovery,
                FbpVolume = fbpVolume,
                EndOfTest = endOfTest,
                AlarmBitmask = alarmBitmask,
                Curve = curve,
                Extra = extra,
                SourceIp = sourceIp,
                ReceivedAt = receivedAt,
                RawJson = rawText,
            };
        }
    }

    // ── helpers ──

    // los valores del DataDictionary son strings o numeros; los normalizo a object para iterar
    private static Dictionary<string, object?>? ExtractDataDictionary(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object) return null;
        if (root.TryGetProperty("DataDictionary", out var ddEl) && ddEl.ValueKind == JsonValueKind.Object)
            return JsonObjectToDict(ddEl);

        // sin DataDictionary: usa el root menos AnalyzerType
        var d = JsonObjectToDict(root);
        d.Remove("AnalyzerType");
        return d.Count > 0 ? d : null;
    }

    private static Dictionary<string, object?> JsonObjectToDict(JsonElement obj)
    {
        var d = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var prop in obj.EnumerateObject())
        {
            d[prop.Name] = prop.Value.ValueKind switch
            {
                JsonValueKind.String => prop.Value.GetString(),
                JsonValueKind.Number => prop.Value.GetRawText(),
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                JsonValueKind.Null => null,
                _ => prop.Value.GetRawText(),
            };
        }
        return d;
    }

    private static string? TryGetString(JsonElement obj, string key)
    {
        if (obj.ValueKind != JsonValueKind.Object) return null;
        if (!obj.TryGetProperty(key, out var v)) return null;
        return v.ValueKind == JsonValueKind.String ? v.GetString() : null;
    }

    private static string? DdString(Dictionary<string, object?> dd, string key)
    {
        if (!dd.TryGetValue(key, out var v) || v is not string s) return null;
        var t = s.Trim();
        return t.Length == 0 ? null : t;
    }

    private static double? DdDouble(Dictionary<string, object?> dd, string key)
    {
        if (!dd.TryGetValue(key, out var v) || v is not string s) return null;
        return ParsePacNumber(s, out var d) ? d : null;
    }

    private static bool ParsePacNumber(string raw, out double value)
        => double.TryParse(raw.Trim(), NumberStyles.Float, Inv, out value);

    // formato PAC: "25 Apr 2026" + "14:45" → "25 Apr 2026 14:45" parseado en cultura invariante
    private static DateTimeOffset? ParsePacDateTime(string? date, string? time)
    {
        if (string.IsNullOrWhiteSpace(date) || string.IsNullOrWhiteSpace(time)) return null;
        var combined = $"{date.Trim()} {time.Trim()}";
        if (DateTime.TryParseExact(
                combined,
                ["d MMM yyyy H:mm", "d MMM yyyy HH:mm"],
                Inv,
                DateTimeStyles.AllowInnerWhite | DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var dt))
            return new DateTimeOffset(dt, TimeSpan.Zero);
        return null;
    }
}
