using FluentAssertions;
using PacCollector.Domain.Errors;
using PacCollector.Domain.ValueObjects;

namespace PacCollector.Domain.Tests.ValueObjects;

public class DistillationCurveTests
{
    [Fact]
    public void AcceptsValidPointsAndSorts()
    {
        var curve = DistillationCurve.Create(new[]
        {
            new CurvePoint(50.0, 200.0),
            new CurvePoint(10.0, 150.0),
            new CurvePoint(90.0, 350.0),
        });
        curve.Points[0].PctRecovered.Should().Be(10.0);
        curve.Points[2].PctRecovered.Should().Be(90.0);
    }

    [Fact]
    public void RejectsOutOfRange()
    {
        Action act = () => DistillationCurve.Create(new[] { new CurvePoint(150.0, 200.0) });
        act.Should().Throw<InvalidCurvePointException>();
    }

    [Fact]
    public void EmptyIsValid()
    {
        DistillationCurve.Empty().IsEmpty.Should().BeTrue();
    }
}
