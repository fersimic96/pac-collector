using System.Text;
using FluentAssertions;
using PacCollector.Infrastructure.Network;

namespace PacCollector.Infrastructure.Tests.Network;

public class IppResponseBuilderTests
{
    [Fact]
    public void HasVersionStatusRequestIdAndAttributes()
    {
        var requestId = new byte[] { 0x00, 0x00, 0x00, 0x0B };
        var response = IppResponseBuilder.BuildOk(requestId);

        // HTTP status line
        Encoding.ASCII.GetString(response, 0, 15).Should().Be("HTTP/1.1 200 OK");

        // localizar inicio del body IPP (despues de \r\n\r\n)
        var terminator = new byte[] { 0x0D, 0x0A, 0x0D, 0x0A };
        var idx = IndexOf(response, terminator);
        idx.Should().BeGreaterThan(0);
        var body = response[(idx + terminator.Length)..];

        // version 1.1
        body[..2].Should().BeEquivalentTo(new byte[] { 0x01, 0x01 });
        // status-code successful-ok (0x0000)
        body[2..4].Should().BeEquivalentTo(new byte[] { 0x00, 0x00 });
        // request_id eco
        body[4..8].Should().BeEquivalentTo(requestId);
        // end-of-attributes-tag
        body[^1].Should().Be(0x03);
        // printer-make-and-model presente
        IndexOf(body, "HP LaserJet 4"u8.ToArray()).Should().BeGreaterThan(0);
    }

    [Fact]
    public void ThrowsForInvalidRequestIdLength()
    {
        var act = () => IppResponseBuilder.BuildOk(new byte[] { 0x01, 0x02, 0x03 });
        act.Should().Throw<ArgumentException>();
    }

    private static int IndexOf(byte[] haystack, byte[] needle)
    {
        if (needle.Length == 0 || haystack.Length < needle.Length) return -1;
        for (var i = 0; i <= haystack.Length - needle.Length; i++)
        {
            var match = true;
            for (var j = 0; j < needle.Length; j++)
            {
                if (haystack[i + j] != needle[j]) { match = false; break; }
            }
            if (match) return i;
        }
        return -1;
    }
}
