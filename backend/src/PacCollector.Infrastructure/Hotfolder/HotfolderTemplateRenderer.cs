using System.Globalization;
using System.Text;
using PacCollector.Domain.Entities;
using PacCollector.Domain.ValueObjects;

namespace PacCollector.Infrastructure.Hotfolder;

// renderer del template hotfolder. Mini-engine de substitucion:
//   {Path[:format][|fallback1|...|literal]}
//   ?{Path}? (prefijo de linea, skip si Path es null/empty)
//   {Curve.ForEach: <row>}  -> N lineas
//   {Extra.ForEach: <row>}  -> N lineas
//
// No es un templating completo (no condicionales generales, no expresiones,
// no aritmetica). Es deliberadamente acotado para que el integrador escriba
// templates predecibles sin que el motor se vuelva un DSL turing-completo.
public static class HotfolderTemplateRenderer
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public sealed record RenderResult(string Filename, string Body);

    public static RenderResult Render(HotfolderTemplate template, Sample sample, string? alias)
    {
        var ctx = new RenderContext(sample, alias);
        var filename = SubstituteTokens(template.FilenameTemplate, ctx);
        var eol = string.Equals(template.LineEnding, "CRLF", StringComparison.OrdinalIgnoreCase) ? "\r\n" : "\n";

        var sb = new StringBuilder();
        foreach (var line in template.Lines)
        {
            var rendered = RenderLine(line, ctx, eol);
            if (rendered is null) continue;  // condicional fallida
            if (template.TrimEmptyLines && rendered.Length == 0) continue;
            sb.Append(rendered);
            // si rendered ya termina con eol (ForEach), no agregar otro
            if (!rendered.EndsWith(eol, StringComparison.Ordinal))
                sb.Append(eol);
        }
        return new RenderResult(filename, sb.ToString());
    }

    // devuelve null cuando el prefijo condicional falla (skip de linea)
    private static string? RenderLine(string line, RenderContext ctx, string eol)
    {
        // prefijo condicional ?{Path}? — substituye tokens internos y skip si vacio
        if (line.StartsWith('?'))
        {
            var endQuestion = line.IndexOf('?', 1);
            if (endQuestion > 1)
            {
                var condExpr = line[1..endQuestion];   // ej. "{Operator}" con braces
                var rest = line[(endQuestion + 1)..];
                var condValue = SubstituteTokens(condExpr, ctx);
                if (string.IsNullOrEmpty(condValue)) return null;
                line = rest;
            }
        }

        // ForEach (cubre toda la linea — el row-template puede ser cualquier cosa)
        var foreachResult = TryExpandForEach(line, ctx, eol);
        if (foreachResult is not null) return foreachResult;

        return SubstituteTokens(line, ctx);
    }

    // detecta lineas {Curve.ForEach: <row>} o {Extra.ForEach: <row>}
    // (la linea entera tiene que ser el ForEach — no se mezclan con otros tokens)
    private static string? TryExpandForEach(string line, RenderContext ctx, string eol)
    {
        const string curvePrefix = "{Curve.ForEach:";
        const string extraPrefix = "{Extra.ForEach:";
        if (line.StartsWith(curvePrefix, StringComparison.Ordinal) && line.EndsWith('}'))
        {
            var rowTemplate = line[curvePrefix.Length..^1];
            var sb = new StringBuilder();
            foreach (var p in ctx.Sample.Curve.Points)
            {
                var pointCtx = ctx.WithCurvePoint(p);
                sb.Append(SubstituteTokens(rowTemplate, pointCtx));
                sb.Append(eol);
            }
            return sb.ToString();
        }
        if (line.StartsWith(extraPrefix, StringComparison.Ordinal) && line.EndsWith('}'))
        {
            var rowTemplate = line[extraPrefix.Length..^1];
            var sb = new StringBuilder();
            foreach (var kv in ctx.Sample.Extra)
            {
                if (kv.Key == "hpgl_curve") continue;  // bloque HP-GL crudo se excluye de outputs
                var extraCtx = ctx.WithExtraEntry(kv.Key, kv.Value);
                sb.Append(SubstituteTokens(rowTemplate, extraCtx));
                sb.Append(eol);
            }
            return sb.ToString();
        }
        return null;
    }

    private static string SubstituteTokens(string template, RenderContext ctx)
    {
        var sb = new StringBuilder();
        var i = 0;
        while (i < template.Length)
        {
            var open = template.IndexOf('{', i);
            if (open < 0) { sb.Append(template, i, template.Length - i); break; }
            sb.Append(template, i, open - i);
            var close = FindMatchingClose(template, open);
            if (close < 0) { sb.Append(template, open, template.Length - open); break; }
            var expr = template[(open + 1)..close];
            sb.Append(ResolveExpression(expr, ctx) ?? "");
            i = close + 1;
        }
        return sb.ToString();
    }

    // matching de { } simple — no se anida porque ForEach se procesa en otro path
    private static int FindMatchingClose(string s, int open)
    {
        for (var i = open + 1; i < s.Length; i++)
        {
            if (s[i] == '}') return i;
        }
        return -1;
    }

    // expr: Path[:format][|fallback|fallback|...|literal]
    // Cuando hay multiples segmentos (separados por |), el ULTIMO que no resuelve se
    // trata como literal — eso permite {StartAt:yyyy-MM-dd|NaN} -> "NaN" si StartAt null.
    // Cuando hay UN solo segmento y no resuelve, devuelve null — para no devolver
    // accidentalmente el nombre del path como "literal" (ej. {Operator} -> null, no "Operator").
    private static string? ResolveExpression(string expr, RenderContext ctx)
    {
        var parts = expr.Split('|');
        for (var i = 0; i < parts.Length; i++)
        {
            var part = parts[i];
            var colon = part.IndexOf(':');
            var path = colon >= 0 ? part[..colon] : part;
            var format = colon >= 0 ? part[(colon + 1)..] : null;

            var resolved = ResolvePath(path.Trim(), format, ctx);
            if (resolved is not null) return resolved;
            // ultima parte de un fallback chain explicito (>=2 segmentos): tratar como literal
            if (i == parts.Length - 1 && parts.Length > 1) return part;
        }
        return null;
    }

    private static string? ResolvePath(string path, string? format, RenderContext ctx)
    {
        if (path.StartsWith("Extra.", StringComparison.Ordinal))
        {
            var key = path["Extra.".Length..];
            return ctx.Sample.Extra.TryGetValue(key, out var v) && !string.IsNullOrEmpty(v) ? v : null;
        }

        // contexto especial de Curve.ForEach
        if (ctx.CurvePoint is { } cp)
        {
            switch (path)
            {
                case "PctRecovered": return FormatDouble(cp.PctRecovered, format);
                case "PctLabel":
                    var rounded = Math.Round(cp.PctRecovered);
                    return Math.Abs(cp.PctRecovered - rounded) < 1e-9
                        ? $"{(int)cp.PctRecovered}%"
                        : $"{FormatDouble(cp.PctRecovered, format)}%";
                case "PctPadded4":
                    return ((uint)cp.PctRecovered).ToString("D4", Inv);
                case "TemperatureC": return FormatDouble(cp.TemperatureC, format);
            }
        }

        // contexto especial de Extra.ForEach
        if (ctx.ExtraEntry is var (ekey, eval))
        {
            switch (path)
            {
                case "Key": return ekey;
                case "Value": return eval;
            }
        }

        // contexto normal: campos del Sample
        var s = ctx.Sample;
        return path switch
        {
            "Serial" => s.Serial.AsString,
            "AnalyzerType" => s.AnalyzerType,
            "SampleIdentifier" => string.IsNullOrEmpty(s.SampleIdentifier) ? null : s.SampleIdentifier,
            "Operator" => string.IsNullOrEmpty(s.Operator) ? null : s.Operator,
            "Program" => string.IsNullOrEmpty(s.Program) ? null : s.Program,
            "Alias" => string.IsNullOrEmpty(ctx.Alias) ? null : ctx.Alias,
            "Ibp" => FormatNullableDouble(s.Ibp, format),
            "Fbp" => FormatNullableDouble(s.Fbp, format),
            "Residue" => FormatNullableDouble(s.Residue, format),
            "Recovery" => FormatNullableDouble(s.Recovery, format),
            "FbpVolume" => FormatNullableDouble(s.FbpVolume, format),
            "EndOfTest" => s.EndOfTest?.ToString().ToLowerInvariant(),
            "StartAt" => FormatNullableDate(s.StartAt, format),
            "EndAt" => FormatNullableDate(s.EndAt, format),
            "ReceivedAt" => FormatDate(s.ReceivedAt, format),
            "SourceIp" => string.IsNullOrEmpty(s.SourceIp) ? null : s.SourceIp,
            _ => null,
        };
    }

    private static string? FormatNullableDouble(double? v, string? format)
        => v.HasValue ? FormatDouble(v.Value, format) : null;

    private static string FormatDouble(double v, string? format)
        => format is null ? v.ToString("R", Inv) : v.ToString(format, Inv);

    private static string? FormatNullableDate(DateTimeOffset? v, string? format)
        => v.HasValue ? FormatDate(v.Value, format) : null;

    private static string FormatDate(DateTimeOffset v, string? format)
        => format is null ? v.ToString("o", Inv) : v.ToString(format, Inv);

    // contexto de render: sample + alias + opcional punto de curva / entrada de extra
    private sealed class RenderContext
    {
        public Sample Sample { get; }
        public string? Alias { get; }
        public CurvePoint? CurvePoint { get; }
        public (string Key, string Value)? ExtraEntry { get; }

        public RenderContext(Sample sample, string? alias, CurvePoint? cp = null, (string, string)? extra = null)
        {
            Sample = sample;
            Alias = alias;
            CurvePoint = cp;
            ExtraEntry = extra;
        }

        public RenderContext WithCurvePoint(CurvePoint cp) => new(Sample, Alias, cp);
        public RenderContext WithExtraEntry(string key, string value) => new(Sample, Alias, null, (key, value));
    }
}
