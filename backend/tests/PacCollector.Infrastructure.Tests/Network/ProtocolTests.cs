using FluentAssertions;
using PacCollector.Infrastructure.Network;

namespace PacCollector.Infrastructure.Tests.Network;

public class ProtocolTests
{
    [Fact]
    public void BeaconBytesMatch()
        => Protocol.Beacon.Should().BeEquivalentTo(new byte[] { 0x01, 0x02, 0x03 });

    [Fact]
    public void NakIsThreeAsciiBytes()
        => Protocol.Nak.Should().BeEquivalentTo(new byte[] { 0x4E, 0x41, 0x4B });

    [Fact]
    public void BuildAckIsAsciiWithSpaces()
    {
        var ack = Protocol.BuildAck("192.168.50.5", 9980);
        ack.Should().Be("ACK 192.168.50.5 9980");
        ack.All(c => c <= 127).Should().BeTrue();
    }

    [Fact]
    public void OkResponseSerializesCompact()
    {
        var r = TcpLimsResponse.Ok("00A3");
        r.ToCompactJson().Should().Be("{\"Error\":\"\",\"SaveCheckSum\":\"00A3\"}");
    }

    [Fact]
    public void NackResponseSerializesCompact()
    {
        var r = TcpLimsResponse.Nack("00FF");
        r.ToCompactJson().Should().Be("{\"Error\":\"NACK\",\"SaveCheckSum\":\"00FF\"}");
    }
}
