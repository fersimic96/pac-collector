using System.Globalization;
using PacCollector.Domain.ValueObjects;

namespace PacCollector.Infrastructure.Plugins.Print;

internal sealed record DistillationTable(
    double? Ibp,
    double? Fbp,
    double? RecoveryPct,
    double? ResiduePct,
    IReadOnlyList<CurvePoint> CurvePoints);

// parser de tabla de destilacion: busca banner "Recovered" y parsea las filas
//   IBP <temp>
//   N% <temp>
//   FBP <temp>
//   %R <recovery> % %r <residue> %
// universal: cualquier equipo que mande este formato funciona.
internal static class DistillationTableParser
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public static DistillationTable Parse(string cleaned)
    {
        var bannerIdx = cleaned.IndexOf("Recovered", StringComparison.Ordinal);
        if (bannerIdx < 0)
            return new DistillationTable(null, null, null, null, Array.Empty<CurvePoint>());

        var afterBanner = cleaned[bannerIdx..];
        var lines = afterBanner.Split('\n');

        double? ibp = null;
        double? fbp = null;
        double? recoveryPct = null;
        double? residuePct = null;
        var points = new List<CurvePoint>();

        for (var li = 1; li < lines.Length; li++)
        {
            var line = lines[li];
            if (string.IsNullOrWhiteSpace(line)) continue;
            if (line.Contains("%1BIN", StringComparison.Ordinal)) break;
            if (line.Contains("Correlation:", StringComparison.Ordinal)) break;

            var recoveryMatch = PrintRegex.RecoveryResidue().Match(line);
            if (recoveryMatch.Success)
            {
                if (double.TryParse(recoveryMatch.Groups[1].Value, NumberStyles.Float, Inv, out var r))
                    recoveryPct = r;
                if (double.TryParse(recoveryMatch.Groups[2].Value, NumberStyles.Float, Inv, out var rr))
                    residuePct = rr;
                continue;
            }

            var rowMatch = PrintRegex.DistillationRow().Match(line);
            if (!rowMatch.Success) continue;

            var label = rowMatch.Groups[1].Value.Trim();
            if (!double.TryParse(rowMatch.Groups[2].Value, NumberStyles.Float, Inv, out var temp))
                continue;

            if (label == "IBP") ibp = temp;
            else if (label == "FBP") fbp = temp;
            else if (label.EndsWith('%'))
            {
                var pctDigits = new string(label.TrimEnd('%').Where(char.IsAsciiDigit).ToArray());
                if (double.TryParse(pctDigits, NumberStyles.Float, Inv, out var pct))
                    points.Add(new CurvePoint(pct, temp));
            }
        }

        return new DistillationTable(ibp, fbp, recoveryPct, residuePct, points);
    }
}
