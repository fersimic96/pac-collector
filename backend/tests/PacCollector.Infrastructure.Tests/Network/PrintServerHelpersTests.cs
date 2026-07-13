using FluentAssertions;
using PacCollector.Infrastructure.Network;

namespace PacCollector.Infrastructure.Tests.Network;

public class PrintServerHelpersTests
{
    [Fact]
    public void IndexOfSubseq_FindsPatternInMiddle()
    {
        var hay = new List<byte> { (byte)'a', (byte)'b', (byte)'c', (byte)'d', (byte)'e', (byte)'f' };
        PrintServer.IndexOfSubseq(hay, "cd"u8.ToArray()).Should().Be(2);
    }

    [Fact]
    public void IndexOfSubseq_ReturnsMinusOneIfNotFound()
    {
        var hay = new List<byte> { (byte)'a', (byte)'b', (byte)'c' };
        PrintServer.IndexOfSubseq(hay, "xy"u8.ToArray()).Should().Be(-1);
    }

    [Fact]
    public void CountSubseq_CountsNonOverlapping()
    {
        var hay = new List<byte>("abcabcabc"u8.ToArray());
        PrintServer.CountSubseq(hay, "abc"u8.ToArray()).Should().Be(3);
    }

    [Fact]
    public void CountSubseq_CountsTwoUelMarkers()
    {
        var uel = new byte[] { 0x1B, 0x25, 0x2D, 0x31, 0x32, 0x33, 0x34, 0x35, 0x58 };
        var hay = new List<byte>(uel.Concat(uel));
        PrintServer.CountSubseq(hay, uel).Should().Be(2);
    }

    [Fact]
    public void CountSubseq_ReturnsZeroForEmptyHaystack()
        => PrintServer.CountSubseq(new List<byte>(), "abc"u8.ToArray()).Should().Be(0);

    [Fact]
    public void CountSubseq_ReturnsZeroWhenNeedleNotPresent()
        => PrintServer.CountSubseq(new List<byte>("abc"u8.ToArray()), "xyz"u8.ToArray()).Should().Be(0);
}
