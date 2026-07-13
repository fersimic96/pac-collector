using System.Buffers.Binary;
using System.Text;

namespace PacCollector.Infrastructure.Network;

// construye una respuesta IPP "successful-ok" minima para que el spooler del equipo
// crea que estamos imprimiendo bien. Le respondemos como si fueramos una HP LaserJet 4.
internal static class IppResponseBuilder
{
    public static byte[] BuildOk(ReadOnlySpan<byte> requestId)
    {
        if (requestId.Length != 4)
            throw new ArgumentException("requestId must be 4 bytes", nameof(requestId));

        var ipp = new List<byte>(512);

        // IPP version 1.1 + status-code "successful-ok" (0x0000)
        ipp.AddRange(new byte[] { 0x01, 0x01, 0x00, 0x00 });
        ipp.AddRange(requestId.ToArray());

        // operation-attributes-tag
        ipp.Add(0x01);
        AppendAttr(ipp, 0x47, "attributes-charset"u8, "utf-8"u8);
        AppendAttr(ipp, 0x48, "attributes-natural-language"u8, "en-us"u8);

        // printer-attributes-tag
        ipp.Add(0x04);
        AppendAttr(ipp, 0x45, "printer-uri-supported"u8, "http://127.0.0.1:631/ipp"u8);
        AppendAttr(ipp, 0x44, "uri-authentication-supported"u8, "none"u8);
        AppendAttr(ipp, 0x44, "uri-security-supported"u8, "none"u8);
        AppendAttr(ipp, 0x42, "printer-name"u8, "PAC-IRIS-CAPTURE"u8);
        AppendAttr(ipp, 0x44, "printer-state-reasons"u8, "none"u8);
        AppendAttrUInt32(ipp, 0x23, "printer-state"u8, 3); // idle
        AppendAttr(ipp, 0x22, "printer-is-accepting-jobs"u8, new byte[] { 0x01 });
        AppendAttr(ipp, 0x42, "printer-make-and-model"u8, "HP LaserJet 4"u8);
        AppendAttr(ipp, 0x44, "document-format-supported"u8, "application/octet-stream"u8);
        AppendAttr(ipp, 0x44, "document-format-default"u8, "application/octet-stream"u8);
        AppendAttr(ipp, 0x44, "compression-supported"u8, "none"u8);
        AppendAttrUInt32(ipp, 0x21, "queued-job-count"u8, 0);
        AppendAttrUInt32(ipp, 0x21, "printer-up-time"u8, 3600);

        // end-of-attributes-tag
        ipp.Add(0x03);

        var ippBody = ipp.ToArray();
        var http = new StringBuilder(512);
        http.Append("HTTP/1.1 200 OK\r\n");
        http.Append("Server: PAC-IRIS-CAPTURE/1.0\r\n");
        http.Append("Content-Type: application/ipp\r\n");
        http.Append($"Content-Length: {ippBody.Length}\r\n");
        http.Append("Connection: close\r\n");
        http.Append("\r\n");

        var headerBytes = Encoding.ASCII.GetBytes(http.ToString());
        var result = new byte[headerBytes.Length + ippBody.Length];
        Buffer.BlockCopy(headerBytes, 0, result, 0, headerBytes.Length);
        Buffer.BlockCopy(ippBody, 0, result, headerBytes.Length, ippBody.Length);
        return result;
    }

    private static void AppendAttr(List<byte> dest, byte tag, ReadOnlySpan<byte> name, ReadOnlySpan<byte> value)
    {
        dest.Add(tag);
        Span<byte> len = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(len, (ushort)name.Length);
        dest.AddRange(len.ToArray());
        dest.AddRange(name.ToArray());
        BinaryPrimitives.WriteUInt16BigEndian(len, (ushort)value.Length);
        dest.AddRange(len.ToArray());
        dest.AddRange(value.ToArray());
    }

    private static void AppendAttrUInt32(List<byte> dest, byte tag, ReadOnlySpan<byte> name, uint value)
    {
        Span<byte> v = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(v, value);
        AppendAttr(dest, tag, name, v);
    }
}
