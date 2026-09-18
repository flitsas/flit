using Flit.Infrastructure.Email;
using FluentAssertions;
using Xunit;

namespace Flit.Infrastructure.Tests.Email;

/// <summary>
/// HU #12430 AC6 — saneamiento del nombre visible del remitente antes de aplicarlo a un envío.
/// Cubre la inyección de cabeceras (CR/LF, caracteres de control) y los caracteres que permitirían
/// simular una segunda dirección dentro del mismo <c>From</c>. La clase ELIMINA (no sustituye) los
/// caracteres prohibidos — cada caso de este archivo calcula el resultado carácter a carácter, sin
/// dar por hecho un reemplazo por espacio.
/// </summary>
/// <remarks>
/// Uso de ejemplo:
/// <code>
/// var applied = SenderDisplayNameSanitizer.Sanitize("Movilidad Andina") ?? "FLIT Trámites";
/// </code>
/// </remarks>
public sealed class SenderDisplayNameSanitizerTests
{
    // ── Happy path — nombre limpio pasa intacto ──────────────────────────────────────────────

    [Fact]
    public void NombreLimpio_PasaSinCambios()
    {
        SenderDisplayNameSanitizer.Sanitize("Movilidad Andina").Should().Be("Movilidad Andina");
    }

    [Fact]
    public void NombreConTildesYEnie_SePreservaTalCual()
    {
        SenderDisplayNameSanitizer.Sanitize("Compañía de Tránsito Añú").Should().Be("Compañía de Tránsito Añú");
    }

    // ── Inyección de cabeceras (CR/LF y otros caracteres de control) ─────────────────────────

    [Fact]
    public void ConCrLf_EliminaLosSaltosDeLineaYNoDejaUnaSegundaLinea()
    {
        var result = SenderDisplayNameSanitizer.Sanitize("Movilidad\r\nBcc: atacante@evil.test");

        // ':' y '@' también son prohibidos (podrían simular un segundo campo/dirección) — se
        // eliminan junto con el CRLF, sin dejar rastro de una cabecera SMTP inyectada.
        result.Should().Be("MovilidadBcc atacanteevil.test");
        result.Should().NotContain("\r").And.NotContain("\n").And.NotContain(":").And.NotContain("@");
    }

    [Fact]
    public void ConCaracterNulo_LoElimina()
    {
        SenderDisplayNameSanitizer.Sanitize("Movilidad\0Andina").Should().Be("MovilidadAndina");
    }

    [Fact]
    public void ConTabulador_LoElimina()
    {
        // El tabulador es un carácter de control: se ELIMINA (no se sustituye por espacio) —
        // igual que CR/LF.
        SenderDisplayNameSanitizer.Sanitize("Movilidad\tAndina").Should().Be("MovilidadAndina");
    }

    // ── Caracteres que permitirían simular una segunda dirección o campo ─────────────────────

    [Theory]
    [InlineData("<evil>", "evil")]
    [InlineData("a@b", "ab")]
    [InlineData("a\"b", "ab")]
    [InlineData("a,b", "ab")]
    [InlineData("a;b", "ab")]
    [InlineData("a:b", "ab")]
    public void ConCaracteresDeInyeccionDeDireccion_LosElimina(string input, string expected)
    {
        SenderDisplayNameSanitizer.Sanitize(input).Should().Be(expected);
    }

    [Fact]
    public void ConDisplayNameYDireccionEntreAngulos_QuedaSoloElTextoSinLosSimbolos()
    {
        var result = SenderDisplayNameSanitizer.Sanitize("\"Movilidad\" <a@b>");

        result.Should().Be("Movilidad ab");
        result.Should().NotContainAny("\"", "<", ">", "@");
    }

    // ── Colapso de espacios ───────────────────────────────────────────────────────────────────

    [Fact]
    public void ConEspaciosMultiples_LosColapsaAUno()
    {
        SenderDisplayNameSanitizer.Sanitize("Movilidad     Andina").Should().Be("Movilidad Andina");
    }

    // ── Recorte a 64 caracteres (NEW-11) ─────────────────────────────────────────────────────

    [Fact]
    public void ConMasDe64Caracteres_RecortaA64()
    {
        var largo = new string('A', 100);

        var result = SenderDisplayNameSanitizer.Sanitize(largo);

        result.Should().NotBeNull();
        result!.Length.Should().Be(64);
        result.Should().Be(new string('A', 64));
    }

    [Fact]
    public void ConExactamente64Caracteres_NoRecorta()
    {
        var exacto = new string('B', 64);

        SenderDisplayNameSanitizer.Sanitize(exacto).Should().Be(exacto);
    }

    // ── Vacío/nulo/solo-espacios ⇒ null (el llamador aplica su remitente por defecto) ────────

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ConEntradaVaciaONula_DevuelveNull(string? input)
    {
        SenderDisplayNameSanitizer.Sanitize(input).Should().BeNull();
    }

    [Fact]
    public void ConSoloCaracteresProhibidos_QuedaVacioYDevuelveNull()
    {
        SenderDisplayNameSanitizer.Sanitize("<>@\";:,").Should().BeNull();
    }

    [Fact]
    public void ConSoloCaracteresDeControl_QuedaVacioYDevuelveNull()
    {
        SenderDisplayNameSanitizer.Sanitize("\r\n\t\0").Should().BeNull();
    }
}
