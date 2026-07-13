using System.Globalization;
using System.Text;
using System.Text.Json;
using PacCollector.Domain.Entities;
using PacCollector.Domain.Errors;
using PacCollector.Domain.Ports;
using PacCollector.Domain.ValueObjects;
using PacCollector.Infrastructure.Config;
using PacCollector.Infrastructure.Hotfolder;

namespace PacCollector.Infrastructure.Filesystem;

public sealed class FileWriterImpl : IFileWriter
{
    private const string MasterHeader = "timestamp,serial,analyzerType,sampleId,operator,program,startAt,endAt,ibp,fbp,residue,recovery,fbpVolume\n";
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    private readonly string _dbDir;
    private readonly string _recentDir;
    private readonly ConfigStore _config;
    private readonly IReadOnlyDictionary<string, HotfolderTemplate> _templates;
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    public FileWriterImpl(
        string dbDir,
        string recentDir,
        ConfigStore config,
        IReadOnlyDictionary<string, HotfolderTemplate>? hotfolderTemplates = null)
    {
        _dbDir = dbDir;
        _recentDir = recentDir;
        _config = config;
        _templates = hotfolderTemplates ?? new Dictionary<string, HotfolderTemplate>(StringComparer.Ordinal);
    }

    public async Task WriteSampleArtifactsAsync(Sample sample, CancellationToken ct = default)
    {
        var cfg = _config.Snapshot();
        var formats = cfg.OutputFormats;
        var general = cfg.General;

        cfg.Instruments.TryGetValue(sample.AnalyzerType, out var instSettings);
        var showKey = instSettings?.ShowKey ?? general.ShowKey;
        var showUnit = instSettings?.ShowUnit ?? general.ShowUnit;
        var delimiter = general.Delimiter == "TAB" ? "\t" : general.Delimiter;
        var eolStr = EolTranslator.Translate(general.Eol);

        var baseName = BaseFilename(sample, general);
        var dbRoot = ResolveDbRoot(sample, cfg);
        var recentRoot = ResolveRecentRoot(sample, cfg);

        await EnsureDirsAsync(dbRoot, formats, ct);
        if (formats.MirrorToRecent)
            await EnsureDirsAsync(recentRoot, formats, ct);

        var dbGlobal = Path.Combine(_dbDir, "_global");
        if (formats.WriteGlobalMasterCsv)
            Directory.CreateDirectory(dbGlobal);

        // el txt lims aplica solo a destilacion
        var isDistillation = sample.AnalyzerType == "OptiPMD";

        var jsonPath = Path.Combine(dbRoot, "json", $"{baseName}.json");
        if (formats.WriteJson)
            await AtomicWriter.WriteAllTextAsync(jsonPath, sample.RawJson, ct);

        var txtPath = Path.Combine(dbRoot, "samples", $"{baseName}.txt");
        if (formats.WriteLimsTxt && isDistillation)
        {
            var body = LimsClassicText(sample, delimiter, eolStr, showKey, showUnit);
            await AtomicWriter.WriteAllTextAsync(txtPath, body, ct);
        }

        var reportPath = Path.Combine(dbRoot, "reports", $"{baseName}.legible.txt");
        string? reportBody = null;
        if (formats.WriteLegibleTxt)
        {
            reportBody = LegibleReport(sample);
            await AtomicWriter.WriteAllTextAsync(reportPath, reportBody, ct);
        }

        var curvePath = Path.Combine(dbRoot, "curves", $"{baseName}.curva.csv");
        if (formats.WriteCurveCsv && !sample.Curve.IsEmpty)
            await AtomicWriter.WriteAllTextAsync(curvePath, CurveCsv(sample), ct);

        await WriteHotFolderAsync(sample, cfg, baseName, isDistillation, ct);

        if (formats.MirrorToRecent)
        {
            if (formats.WriteJson)
                TryCopy(jsonPath, Path.Combine(recentRoot, "json", $"{baseName}.json"));
            if (formats.WriteLimsTxt && isDistillation)
                TryCopy(txtPath, Path.Combine(recentRoot, "samples", $"{baseName}.txt"));
            if (formats.WriteLegibleTxt)
                TryCopy(reportPath, Path.Combine(recentRoot, "reports", $"{baseName}.legible.txt"));
            if (formats.WriteCurveCsv && !sample.Curve.IsEmpty)
                TryCopy(curvePath, Path.Combine(recentRoot, "curves", $"{baseName}.curva.csv"));
        }

        if (formats.WriteLegibleTxt && reportBody is not null)
            try { await AtomicWriter.WriteAllTextAsync(Path.Combine(dbRoot, "latest.txt"), reportBody, ct); }
            catch (Exception e) when (e is not OutOfMemoryException) { /* best-effort */ }

        await _writeLock.WaitAsync(ct);
        try
        {
            var row = MasterRow(sample);
            if (formats.WriteMasterCsv)
                await AppendMasterCsvAsync(Path.Combine(dbRoot, "master.csv"), row, ct);
            if (formats.WriteGlobalMasterCsv)
                await AppendMasterCsvAsync(Path.Combine(dbGlobal, "master.csv"), row, ct);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public async Task<UnknownPayloadSaved> WriteUnknownPayloadAsync(
        ReadOnlyMemory<byte> raw,
        string? analyzerType,
        string? sourceIp,
        string reason,
        DateTimeOffset receivedAt,
        CancellationToken ct = default)
    {
        // separa por tipo, o _invalid si los bytes no son UTF-8 ni JSON, o _untyped si JSON sin AnalyzerType
        string bucket;
        if (!string.IsNullOrWhiteSpace(analyzerType))
            bucket = SafeFilename.Sanitize(analyzerType.Trim()).AsString;
        else
            bucket = LooksLikeJson(raw.Span) ? "_untyped" : "_invalid";

        var unknownRoot = Path.Combine(_dbDir, "_unknown", bucket);
        Directory.CreateDirectory(unknownRoot);

        var ts = receivedAt.ToString("yyyyMMdd_HHmmss_fff", Inv);
        var ipPart = sourceIp?.Replace('.', '_').Replace(':', '-') ?? "noip";

        var isText = IsValidUtf8(raw.Span);
        var ext = isText ? "json" : "bin";
        var payloadPath = Path.Combine(unknownRoot, $"{ts}_{ipPart}.{ext}");

        await AtomicWriter.WriteAllBytesAsync(payloadPath, raw, ct);

        var meta = new
        {
            received_at = receivedAt.ToString("o", Inv),
            analyzer_type = analyzerType,
            source_ip = sourceIp,
            reason,
            bytes = raw.Length,
            is_text = isText,
        };
        var metaPath = Path.Combine(unknownRoot, $"{ts}_{ipPart}.meta.json");
        try
        {
            var metaJson = JsonSerializer.Serialize(meta, JsonOptions.Pretty);
            await AtomicWriter.WriteAllTextAsync(metaPath, metaJson, ct);
        }
        catch (Exception e) when (e is not OutOfMemoryException) { /* metadata is best-effort */ }

        return new UnknownPayloadSaved(payloadPath);
    }

    // ── helpers de ruta ──

    private string ResolveDbRoot(Sample sample, AppConfig cfg)
    {
        if (cfg.Instruments.TryGetValue(sample.AnalyzerType, out var inst)
            && !string.IsNullOrEmpty(inst.OutputDir))
            return inst.OutputDir;
        return Path.Combine(_dbDir, InstrumentFolderName(sample));
    }

    private string ResolveRecentRoot(Sample sample, AppConfig cfg)
    {
        if (cfg.Instruments.TryGetValue(sample.AnalyzerType, out var inst)
            && !string.IsNullOrEmpty(inst.RecentDir))
            return inst.RecentDir;
        return Path.Combine(_recentDir, InstrumentFolderName(sample));
    }

    private static string InstrumentFolderName(Sample sample)
        => $"{SafeFilename.Sanitize(sample.Serial.AsString)}_{SafeFilename.Sanitize(sample.AnalyzerType)}";

    private static string BaseFilename(Sample sample, GeneralSettings cfg)
    {
        var parts = new List<string>();
        if (cfg.ShowAnalyzerSn)
            parts.Add(SafeFilename.Sanitize(sample.Serial.AsString).AsString);
        if (cfg.ShowSampleId)
            parts.Add(SafeFilename.Sanitize(sample.SampleIdentifier).AsString);
        if (cfg.ShowStartTime)
        {
            var t = sample.StartAt ?? sample.ReceivedAt;
            parts.Add(t.ToString("yyyyMMdd_HHmm", Inv));
        }
        if (parts.Count == 0)
        {
            parts.Add(sample.ReceivedAt.ToString("yyyyMMdd_HHmmss", Inv));
            parts.Add(sample.Uuid.Length > 8 ? sample.Uuid[..8] : sample.Uuid);
        }
        return string.Join("_", parts);
    }

    private static async Task EnsureDirsAsync(string folderRoot, OutputFormats formats, CancellationToken ct)
    {
        var subs = new List<string>();
        if (formats.WriteJson) subs.Add("json");
        if (formats.WriteLimsTxt) subs.Add("samples");
        if (formats.WriteLegibleTxt) subs.Add("reports");
        if (formats.WriteCurveCsv) subs.Add("curves");
        foreach (var sub in subs)
        {
            var p = Path.Combine(folderRoot, sub);
            try { Directory.CreateDirectory(p); }
            catch (Exception e)
            {
                throw new ConfigInvalidException("db_dir", $"cannot create {p}: {e.Message}");
            }
        }
        await Task.CompletedTask;
    }

    private static void TryCopy(string src, string dst)
    {
        try { File.Copy(src, dst, overwrite: true); }
        catch { /* best-effort mirror */ }
    }

    // ── formatos ──

    // formato lims clasico para destilacion: KEY{delim}VALUE{delim}{eol}
    internal static string LimsClassicText(Sample sample, string delimiter, string eol, bool showKey, bool _showUnit)
    {
        var sb = new StringBuilder();
        void Put(string k, string v)
        {
            if (showKey) sb.Append(k).Append(delimiter).Append(v).Append(delimiter).Append(eol);
            else sb.Append(v).Append(delimiter).Append(eol);
        }
        Put("AnalyzerType", sample.AnalyzerType);
        Put("AnalyzerSerialNumber", sample.Serial.AsString);
        Put("SampleIdentifier", sample.SampleIdentifier);
        if (sample.Operator is not null) Put("OperatorId", sample.Operator);
        if (sample.Program is not null) Put("ProgramName", sample.Program);
        if (sample.Ibp is { } ibp) Put("IBP", FormatNumber(ibp));
        if (sample.Fbp is { } fbp) Put("FBP", FormatNumber(fbp));
        if (sample.Residue is { } r) Put("Residue", FormatNumber(r));
        if (sample.Recovery is { } rec) Put("Recovery", FormatNumber(rec));
        foreach (var p in sample.Curve.Points)
            Put($"Recovered_{(uint)p.PctRecovered:D4}", FormatNumber(p.TemperatureC));
        foreach (var (k, v) in sample.Extra)
            Put(k, v);
        return sb.ToString();
    }

    internal static string LegibleReport(Sample sample)
    {
        var sb = new StringBuilder();
        sb.Append("PAC Collector — Reporte legible\n");
        sb.Append($"Generado: {DateTimeOffset.UtcNow.ToString("yyyy-MM-dd HH:mm:ss 'UTC'", Inv)}\n");
        sb.Append(new string('=', 70));
        sb.Append('\n');
        sb.Append("\n═ IDENTIFICACIÓN ═\n");
        sb.Append($"  Modelo: {sample.AnalyzerType}\n");
        sb.Append($"  Serie: {sample.Serial.AsString}\n");
        sb.Append($"  Muestra: {sample.SampleIdentifier}\n");
        if (sample.Operator is not null) sb.Append($"  Operador: {sample.Operator}\n");
        if (sample.Program is not null) sb.Append($"  Programa: {sample.Program}\n");
        sb.Append("\n═ TIEMPOS ═\n");
        if (sample.StartAt is { } st)
            sb.Append($"  Inicio: {st.ToString("yyyy-MM-dd HH:mm:ss 'UTC'", Inv)}\n");
        if (sample.EndAt is { } et)
            sb.Append($"  Fin:    {et.ToString("yyyy-MM-dd HH:mm:ss 'UTC'", Inv)}\n");
        sb.Append("\n═ RESULTADOS ═\n");
        if (sample.Ibp is { } ibp) sb.Append($"  IBP:      {FormatNumber(ibp)} °C\n");
        if (sample.Fbp is { } fbp) sb.Append($"  FBP:      {FormatNumber(fbp)} °C\n");
        if (sample.Residue is { } rr) sb.Append($"  Residue:  {FormatNumber(rr)} %\n");
        if (sample.Recovery is { } rec) sb.Append($"  Recovery: {FormatNumber(rec)} %\n");
        if (!sample.Curve.IsEmpty)
        {
            sb.Append($"\n═ CURVA DE DESTILACIÓN ({sample.Curve.Count} puntos) ═\n");
            sb.Append("  %Recuperado    Temperatura (°C)\n");
            sb.Append("  ────────────  ─────────────────\n");
            foreach (var p in sample.Curve.Points)
            {
                var pct = FormatNumber(p.PctRecovered).PadLeft(10);
                var temp = p.TemperatureC.ToString("F1", Inv).PadLeft(10);
                sb.Append($"  {pct}%   {temp}\n");
            }
        }
        return sb.ToString();
    }

    internal static string CurveCsv(Sample sample)
    {
        var sb = new StringBuilder();
        sb.Append($"AnalyzerSN,{sample.Serial.AsString}\n");
        sb.Append($"SampleID,{sample.SampleIdentifier}\n");
        sb.Append("%Recuperado,Temperatura (°C)\n");
        if (sample.Ibp is { } ibp) sb.Append($"0 (IBP),{FormatNumber(ibp)}\n");
        foreach (var p in sample.Curve.Points)
            sb.Append($"{FormatNumber(p.PctRecovered)},{FormatNumber(p.TemperatureC)}\n");
        if (sample.Fbp is { } fbp && sample.FbpVolume is { } fv)
            sb.Append($"{FormatNumber(fv)} (FBP),{FormatNumber(fbp)}\n");
        return sb.ToString();
    }

    // CSV de 2 columnas Key;Value con todos los campos del sample
    internal static string SampleAllCsv(Sample sample, string? alias)
    {
        var sb = new StringBuilder();
        sb.Append("Key;Value\r\n");
        void Put(string k, string v)
        {
            sb.Append(CsvEscape(k));
            sb.Append(';');
            sb.Append(CsvEscape(v));
            sb.Append("\r\n");
        }
        Put("Instrument", alias ?? sample.AnalyzerType);
        Put("AnalyzerType", sample.AnalyzerType);
        Put("AnalyzerSN", sample.Serial.AsString);
        Put("SampleId", sample.SampleIdentifier);
        if (sample.Operator is not null) Put("OperatorId", sample.Operator);
        if (sample.Program is not null) Put("ProgramName", sample.Program);
        if (sample.Ibp is { } ibp) Put("IBP", FormatNumber(ibp));
        if (sample.Fbp is { } fbp) Put("FBP", FormatNumber(fbp));
        if (sample.Residue is { } rr) Put("Residue", FormatNumber(rr));
        if (sample.Recovery is { } rec) Put("Recovery", FormatNumber(rec));
        if (sample.FbpVolume is { } fv) Put("FBPVolume", FormatNumber(fv));
        foreach (var p in sample.Curve.Points)
            Put($"Recovered_{(uint)p.PctRecovered:D4}", FormatNumber(p.TemperatureC));
        foreach (var (k, v) in sample.Extra)
            Put(k, v);
        Put("ReceivedAt", sample.ReceivedAt.ToString("o", Inv));
        if (sample.StartAt is { } st) Put("StartAt", st.ToString("o", Inv));
        if (sample.EndAt is { } et) Put("EndAt", et.ToString("o", Inv));
        if (sample.SourceIp is not null) Put("SourceIP", sample.SourceIp);
        return sb.ToString();
    }

    // formato txt estilo LIMS Ethernet para destilacion
    internal static string LimsEthernetTxt(Sample sample, string? alias)
    {
        const string nan = "NaN";
        static string OptF(double? v) => v.HasValue ? FormatNumber(v.Value) : nan;
        static string FmtDt(DateTimeOffset? t)
            => t.HasValue ? t.Value.ToString("yyyy-MM-ddTHH:mm:ss.000", Inv) : nan;

        string Extra(string key) => sample.Extra.TryGetValue(key, out var v) ? v : nan;
        string ExtraOr(string key1, string key2, string fallback) =>
            sample.Extra.TryGetValue(key1, out var v1) ? v1
            : sample.Extra.TryGetValue(key2, out var v2) ? v2
            : fallback;

        var sb = new StringBuilder();
        void Ln(string s) { sb.Append(s); sb.Append("\r\n"); }
        void Blank() => sb.Append("\r\n");

        Ln("Status: C");
        Ln($"Instrument: {alias ?? sample.AnalyzerType}");
        Ln($"Probe serial number: {sample.Serial.AsString}");
        Blank();
        Ln($"Sample: {sample.SampleIdentifier}");
        Ln("Description: ");
        Ln($"Start of distillation: {FmtDt(sample.StartAt)}");
        Ln($"End of distillation: {FmtDt(sample.EndAt)}");
        Ln($"Standard: {sample.Program ?? nan}");
        Ln($"Group: {ExtraOr("Group", "group", "1")}");
        Ln("Temp. unit: C");
        Ln($"Product: {ExtraOr("Product", "product", "")}");
        Blank();
        Ln($"Percent recovery: {OptF(sample.Recovery)}");
        Ln($"Corr. recovery: {nan}");
        Ln($"Residue: {OptF(sample.Residue)}");
        Ln($"Barom. pressure: {ExtraOr("BaromPressure", "barom_pressure", nan)}");
        Ln($"Total recovery: {nan}");
        Ln($"Corr. total recovery: {nan}");
        Ln($"Observed loss: {nan}");
        Ln($"Corr. loss: {nan}");
        Ln($"Condenser temp.: {Extra("CondensorTemp")}");
        Ln($"Receiver temp.: {Extra("ReceiverTemp")}");
        Ln($"Ambient temp.: {nan}");
        Blank();
        Ln($"User(Instrument): {sample.Operator ?? ""}");
        Ln($"Heating mode: {nan}");
        Ln($"FBP detection mode: {nan}");
        Ln($"Final heating adjustment: {nan}");
        Ln($"Stop condition: {nan}");
        Ln($"Temp. stop: {nan}");
        Blank();
        Ln("Corrected");
        Ln("========= ");
        Ln($"IBP {OptF(sample.Ibp)}");
        foreach (var p in sample.Curve.Points)
        {
            var rounded = Math.Round(p.PctRecovered);
            var label = Math.Abs(p.PctRecovered - rounded) < 1e-9
                ? $"{(int)p.PctRecovered}%"
                : $"{FormatNumber(p.PctRecovered)}%";
            Ln($"{label} {FormatNumber(p.TemperatureC)}");
        }
        Ln($"FBP {OptF(sample.Fbp)}");
        Blank();
        return sb.ToString();
    }

    internal static string MasterRow(Sample sample)
    {
        static string Opt(double? v) => v.HasValue ? FormatNumber(v.Value) : "";
        return string.Join(',',
            sample.ReceivedAt.ToString("o", Inv),
            sample.Serial.AsString,
            sample.AnalyzerType,
            sample.SampleIdentifier,
            sample.Operator ?? "",
            sample.Program ?? "",
            sample.StartAt?.ToString("o", Inv) ?? "",
            sample.EndAt?.ToString("o", Inv) ?? "",
            Opt(sample.Ibp),
            Opt(sample.Fbp),
            Opt(sample.Residue),
            Opt(sample.Recovery),
            Opt(sample.FbpVolume)) + "\n";
    }

    private async Task WriteHotFolderAsync(
        Sample sample,
        AppConfig cfg,
        string baseName,
        bool isDistillation,
        CancellationToken ct)
    {
        HotFolderFormat? fmt = null;
        string? dir = null;
        string? routeAlias = null;
        string? templateName = null;
        if (cfg.InstrumentRoutes.TryGetValue(sample.Serial.AsString, out var route))
        {
            fmt = route.HotFolderFormat;
            dir = route.HotFolderDir;
            routeAlias = string.IsNullOrWhiteSpace(route.Alias) ? null : route.Alias;
            templateName = route.HotFolderTemplate;
        }
        else if (cfg.Instruments.TryGetValue(sample.AnalyzerType, out var inst))
        {
            fmt = inst.HotFolderFormat;
            dir = inst.HotFolderDir;
            routeAlias = string.IsNullOrWhiteSpace(inst.Alias) ? null : inst.Alias;
            templateName = inst.HotFolderTemplate;
        }
        if (string.IsNullOrEmpty(dir)) return;

        try { Directory.CreateDirectory(dir); }
        catch (Exception e) { throw new ConfigInvalidException("hot_folder_dir", $"cannot create {dir}: {e.Message}"); }

        // template-driven path (nuevo). Si la route/settings tiene HotFolderTemplate
        // seteado y el template existe, se usa. Si esta seteado pero no existe, log
        // y cae al enum fallback (no rompe el flow).
        (string filename, string body)? payload = null;
        if (!string.IsNullOrEmpty(templateName))
        {
            if (_templates.TryGetValue(templateName, out var template))
            {
                var rendered = HotfolderTemplateRenderer.Render(template, sample, routeAlias);
                payload = (rendered.Filename, rendered.Body);
            }
            else
            {
                Console.Error.WriteLine($"[hotfolder] template '{templateName}' not found, falling back to enum HotFolderFormat");
            }
        }

        // enum fallback (legacy path): si no hay template o no se encontro, usar el switch
        if (payload is null && fmt is not null)
        {
            payload = fmt switch
            {
                HotFolderFormat.LimsEthernet when isDistillation
                    => ($"{baseName}.txt", LimsEthernetTxt(sample, routeAlias)),
                HotFolderFormat.LimsEthernet => null,
                HotFolderFormat.CsvAll => ($"{baseName}.csv", SampleAllCsv(sample, routeAlias)),
                HotFolderFormat.Csv => ($"{baseName}.curva.csv", CurveCsv(sample)),
                _ => null,
            };
        }
        if (payload is null) return;

        try
        {
            await AtomicWriter.WriteAllTextAsync(Path.Combine(dir, payload.Value.filename), payload.Value.body, ct);
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            throw new ConfigInvalidException("hot_folder_dir", $"write hot folder: {e.Message}");
        }
    }

    // master.csv append-only con header en la primera escritura, write-through + fsync para durability
    private static async Task AppendMasterCsvAsync(string path, string row, CancellationToken ct)
    {
        var needsHeader = !File.Exists(path);
        await using var fs = new FileStream(
            path, FileMode.Append, FileAccess.Write, FileShare.Read,
            4096, FileOptions.WriteThrough | FileOptions.Asynchronous);
        if (needsHeader)
        {
            var header = Encoding.UTF8.GetBytes(MasterHeader);
            await fs.WriteAsync(header, ct);
        }
        var bytes = Encoding.UTF8.GetBytes(row);
        await fs.WriteAsync(bytes, ct);
        await fs.FlushAsync(ct);
        fs.Flush(flushToDisk: true);
    }

    private static string CsvEscape(string s)
    {
        if (s.IndexOfAny([';', '"', '\n', '\r']) < 0) return s;
        return $"\"{s.Replace("\"", "\"\"")}\"";
    }

    private static string FormatNumber(double v)
    {
        // mismo comportamiento que rust f64::to_string: shortest round-trippable representation
        return v.ToString("R", Inv);
    }

    private static bool LooksLikeJson(ReadOnlySpan<byte> raw)
    {
        if (!IsValidUtf8(raw)) return false;
        try
        {
            using var doc = JsonDocument.Parse(raw.ToArray());
            return true;
        }
        catch { return false; }
    }

    // UTF8 estricto (lanza si hay bytes invalidos), Encoding.UTF8 por default los reemplaza con ?
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    private static bool IsValidUtf8(ReadOnlySpan<byte> raw)
    {
        try
        {
            _ = StrictUtf8.GetString(raw);
            return true;
        }
        catch (DecoderFallbackException) { return false; }
    }
}
