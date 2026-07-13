using System.Text;
using FluentAssertions;
using PacCollector.Infrastructure.Plugins.Print;

namespace PacCollector.Infrastructure.Tests.Plugins;

// paridad con el comportamiento del Rust strip_pcl + verificacion explicita
// del manejo del byte 0xB0 (CP-1252 °) que viene en reportes PAC con unidades °C.
public class PclStripperTests
{
    [Fact]
    public void Strip_RemovesParameterizedEscapeSequences()
    {
        // PCL: ESC %-12345X (UEL) + ESC &a10L + ESC (s0p12h4s0b4099T + texto
        var pcl = "\u001B%-12345X\u001B&a10L\u001B(s0p12h4s0b4099THELLO";
        var cleaned = PclStripper.Strip(pcl);
        cleaned.Should().Contain("HELLO");
        cleaned.Should().NotContain("\u001B");
    }

    [Fact]
    public void Strip_DropsHpglBlockAfterMarker()
    {
        var pcl = "Freeze point: -92.1\n%1BIN;SP1;PW.4;PU1002,500;";
        var cleaned = PclStripper.Strip(pcl);
        cleaned.Should().Contain("Freeze point: -92.1");
        cleaned.Should().NotContain("%1BIN;");
    }

    [Fact]
    public void Strip_TwoCharEscape_RemovedCorrectly()
    {
        // ESC + un solo char final (E = reset printer)
        var pcl = "\u001BEHELLO";
        var cleaned = PclStripper.Strip(pcl);
        cleaned.Should().Be("HELLO");
    }

    [Fact]
    public void Strip_EmptyInput_ReturnsEmpty()
    {
        PclStripper.Strip("").Should().Be("");
    }

    [Fact]
    public void Strip_PreservesUnicodeReplacementCharFromDegreeByte()
    {
        // CP-1252 byte 0xB0 ('°') no es UTF-8 valido. El decoder default (Encoding.UTF8)
        // lo reemplaza con U+FFFD ('�'). Validamos que PclStripper preserve el
        // replacement char — CleanValue lo limpia despues, pero acá debe pasar derecho.
        var bytes = new byte[] { 0x46, 0x50, 0x3A, 0x20, 0x36, 0x34, 0x2E, 0x35, 0xB0, 0x43 };  // "FP: 64.5°C" en CP-1252
        var decoded = Encoding.UTF8.GetString(bytes);  // 0xB0 → U+FFFD; 0x43 → 'C'
        decoded.Should().Contain("�");

        var cleaned = PclStripper.Strip(decoded);
        cleaned.Should().Contain("FP: 64.5");
        cleaned.Should().Contain("C");
    }

    [Fact]
    public void Strip_TextWithoutEscapes_PassesThrough()
    {
        const string plain = "Operator: Fer\nSample ID: GAS-01\nResult: 232.5\n";
        PclStripper.Strip(plain).Should().Be(plain);
    }

    [Fact]
    public void Strip_DanglingEscAtEnd_DoesNotThrow()
    {
        // edge case: ESC sin char siguiente
        var act = () => PclStripper.Strip("HELLO\u001B");
        act.Should().NotThrow();
    }
}
