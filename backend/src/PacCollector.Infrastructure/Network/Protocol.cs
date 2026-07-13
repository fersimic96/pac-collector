using System.Text.Json;
using System.Text.Json.Serialization;

namespace PacCollector.Infrastructure.Network;

// constantes y formato del protocolo de los equipos PAC en LAN
public static class Protocol
{
    // baliza UDP del equipo: 3 bytes mágicos
    public static readonly byte[] Beacon = { 0x01, 0x02, 0x03 };

    // los equipos terminan el JSON con NUL byte
    public const byte NullTerminator = 0x00;

    // respuesta UDP a payloads que no son la baliza
    public static readonly byte[] Nak = "NAK"u8.ToArray();

    public const int TcpReadChunk = 1024;
    public const int TcpReadTimeoutMs = 5000;

    public static string BuildAck(string serverIp, ushort tcpPort)
        => $"ACK {serverIp} {tcpPort}";
}

// respuesta JSON al equipo despues de recibir una muestra. Formato propietario PAC.
public sealed class TcpLimsResponse
{
    [JsonPropertyName("Error")]
    public string Error { get; set; } = "";

    [JsonPropertyName("SaveCheckSum")]
    public string SaveCheckSum { get; set; } = "";

    private static readonly JsonSerializerOptions CompactOptions = new()
    {
        WriteIndented = false,
    };

    public static TcpLimsResponse Ok(string checksum) => new() { Error = "", SaveCheckSum = checksum };
    public static TcpLimsResponse Nack(string checksum) => new() { Error = "NACK", SaveCheckSum = checksum };

    public string ToCompactJson() => JsonSerializer.Serialize(this, CompactOptions);
}
