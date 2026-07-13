using FluentAssertions;
using PacCollector.Infrastructure.Plugins.Print;

namespace PacCollector.Infrastructure.Tests.Plugins;

// paridad con el comportamiento del Rust render_cr_overwrite (mismas fixtures
// de comportamiento en pac_family_print_plugin.rs:1260-1274).
public class CrOverwriteRendererTests
{
    [Fact]
    public void Render_LastNonSpaceWins()
    {
        // dos segmentos: el primero escribe "Hello", el segundo sobrescribe col 0..2 con 'X','Y','Z'
        // 'l' y 'o' (col 3, 4) quedan del primer segmento
        var result = CrOverwriteRenderer.Render("Hello\rXYZ");
        result.Should().Be("XYZlo");
    }

    [Fact]
    public void Render_SpacesDoNotOverwriteNonSpace()
    {
        // segundo segmento "  X": dos spaces NO sobrescriben 'H','e'; 'X' sobrescribe 'l'
        var result = CrOverwriteRenderer.Render("Hello\r  X");
        result.Should().Be("HeXlo");
    }

    [Fact]
    public void Render_EmptyInput_ReturnsEmpty()
    {
        CrOverwriteRenderer.Render("").Should().Be("");
    }

    [Fact]
    public void Render_NoCarriageReturn_ReturnsAsIs()
    {
        // sin \r no hay overwrite — trim de trailing spaces se aplica igual
        CrOverwriteRenderer.Render("simple line").Should().Be("simple line");
    }

    [Fact]
    public void Render_MultipleLinesAreProcessedIndependently()
    {
        // dos lineas logicas, cada una con su propio CR-overwrite
        var input = "ABC\rXY\nDEF\rZ";
        var result = CrOverwriteRenderer.Render(input);
        // Linea 1: ABC + XY → XYC
        // Linea 2: DEF + Z → ZEF
        result.Should().Be("XYC\nZEF");
    }

    [Fact]
    public void Render_TrailingSpacesTrimmed()
    {
        // "abc   " → "abc" (trim trailing spaces de la linea rendered)
        CrOverwriteRenderer.Render("abc   ").Should().Be("abc");
    }

    [Fact]
    public void Render_DoesNotPanicOnLeadingCr()
    {
        // edge case: input que empieza con \r (segundo segmento vacio implicito)
        var result = CrOverwriteRenderer.Render("\rABC");
        result.Should().Be("ABC");
    }
}
