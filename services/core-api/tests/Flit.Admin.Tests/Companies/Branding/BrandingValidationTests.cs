using Flit.Admin.Domain.Companies.Branding;
using FluentAssertions;
using Xunit;

namespace Flit.Admin.Tests.Companies.Branding;

/// <summary>
/// Uso de ejemplo:
/// var trimmed = BrandingValidation.NormalizeName("  Movilidad Andina  "); // "Movilidad Andina"
/// BrandingValidation.IsNameLengthValid(trimmed); // true
/// HU #12413 AC3/AC5 — una regla por prueba (longitud, markup, hex por campo, normalización, trim).
/// </summary>
public sealed class BrandingValidationTests
{
    [Fact]
    public void NormalizeName_RecortaEspaciosEnLosExtremos()
    {
        BrandingValidation.NormalizeName("  Movilidad Andina  ").Should().Be("Movilidad Andina");
    }

    [Theory]
    [InlineData("Ab", true)] // 2 chars — límite inferior
    [InlineData("A", false)] // 1 char — por debajo del límite
    [InlineData("Movilidad Andina S.A.S.", true)] // dentro de rango
    public void IsNameLengthValid_RespetaElMinimo(string name, bool expected)
    {
        BrandingValidation.IsNameLengthValid(name).Should().Be(expected);
    }

    [Fact]
    public void IsNameLengthValid_ConMasDe40Caracteres_EsInvalido()
    {
        var tooLong = new string('A', 41);
        BrandingValidation.IsNameLengthValid(tooLong).Should().BeFalse();
    }

    [Fact]
    public void IsNameLengthValid_ConExactamente40Caracteres_EsValido()
    {
        var exact = new string('A', 40);
        BrandingValidation.IsNameLengthValid(exact).Should().BeTrue();
    }

    [Theory]
    [InlineData("Movilidad <script>alert(1)</script>", true)]
    [InlineData("<b>Andina</b>", true)]
    [InlineData("Movilidad Andina", false)]
    public void HasMarkup_DetectaEtiquetas(string name, bool expected)
    {
        BrandingValidation.HasMarkup(name).Should().Be(expected);
    }

    [Theory]
    [InlineData("#0B3D91", true)]
    [InlineData("#0b3d91", true)]
    [InlineData("0B3D91", false)] // sin almohadilla
    [InlineData("#0B3D9", false)] // corto
    [InlineData("#0B3D91FF", false)] // largo (con alfa)
    [InlineData("#GGGGGG", false)] // no hex
    [InlineData(null, false)]
    public void IsValidHexColor_SoloAceptaHexDeSeisDigitosConAlmohadilla(string? value, bool expected)
    {
        BrandingValidation.IsValidHexColor(value).Should().Be(expected);
    }

    [Fact]
    public void NormalizeHexColor_ConvierteAMayusculas()
    {
        BrandingValidation.NormalizeHexColor("#0b3d91").Should().Be("#0B3D91");
    }
}
