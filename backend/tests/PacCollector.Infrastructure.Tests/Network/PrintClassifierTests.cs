using System.Text;
using FluentAssertions;
using PacCollector.Infrastructure.Network;

namespace PacCollector.Infrastructure.Tests.Network;

public class PrintClassifierTests
{
    private static PrintClassification Classify(string head)
        => PrintClassifier.Classify(Encoding.ASCII.GetBytes(head));

    [Theory]
    [InlineData("POST / HTTP/1.1")]
    [InlineData("GET / HTTP/1.1")]
    [InlineData("PATCH /x HTTP/1.1")]
    [InlineData("OPTIONS / HTTP/1.1")]
    [InlineData("DELETE / HTTP/1.1")]
    [InlineData("HEAD / HTTP/1.1")]
    [InlineData("PUT / HTTP/1.1")]
    public void RecognisesFullHttpMethods(string input)
        => Classify(input).Should().Be(PrintClassification.Http);

    [Theory]
    [InlineData("PO")]
    [InlineData("P")]
    [InlineData("")]
    public void HoldsDecisionForShortAsciiUpperPrefix(string input)
        => Classify(input).Should().Be(PrintClassification.Indeterminate);

    [Fact]
    public void TreatsBinaryStartAsRaw()
        => PrintClassifier.Classify(new byte[] { 0x01, 0x01, 0x00, 0x02 }).Should().Be(PrintClassification.Raw);

    [Fact]
    public void TreatsUelMarkerAsRaw()
        => PrintClassifier.Classify(new byte[] { 0x1B, 0x25, 0x2D, 0x31, 0x32, 0x33, 0x34, 0x35, 0x58 })
            .Should().Be(PrintClassification.Raw);

    [Fact]
    public void TreatsOptiPmdMarkerAsRaw()
        => Classify("OptiPMD").Should().Be(PrintClassification.Raw);
}
