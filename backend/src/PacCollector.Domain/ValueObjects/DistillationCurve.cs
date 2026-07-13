using PacCollector.Domain.Errors;

namespace PacCollector.Domain.ValueObjects;

public sealed record CurvePoint(double PctRecovered, double TemperatureC);

public sealed class DistillationCurve
{
    private readonly List<CurvePoint> _points;

    private DistillationCurve(List<CurvePoint> points) => _points = points;

    public static DistillationCurve Create(IEnumerable<CurvePoint> points)
    {
        var list = points.ToList();
        foreach (var p in list)
        {
            if (p.PctRecovered < 0.0 || p.PctRecovered > 100.0)
                throw new InvalidCurvePointException($"pct_recovered out of range: {p.PctRecovered}");
        }
        list.Sort((a, b) => a.PctRecovered.CompareTo(b.PctRecovered));
        return new DistillationCurve(list);
    }

    public static DistillationCurve Empty() => new(new List<CurvePoint>());

    public IReadOnlyList<CurvePoint> Points => _points;
    public bool IsEmpty => _points.Count == 0;
    public int Count => _points.Count;
}
