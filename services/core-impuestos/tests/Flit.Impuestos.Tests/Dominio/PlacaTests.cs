using Flit.Impuestos.Domain.Liquidaciones;
using FluentAssertions;
using Xunit;

namespace Flit.Impuestos.Tests.Dominio;

/// <summary>
/// HU #14694 (M05, S16) — la placa se normaliza a mayúsculas, sin espacios ni guiones, antes de guardarla
/// (<c>liquidacion_items.placa varchar(10)</c>). Lo que después de normalizar queda vacío, trae otro carácter o pasa de 10 no es
/// una placa (la API lo responde como 422 <c>PLACA_INVALIDA</c>).
/// </summary>
/// <example>
/// <code>
/// Placa.TryNormalizar(" abc-123 ", out var placa); // true, "ABC123"
/// </code>
/// </example>
public sealed class PlacaTests
{
    [Theory]
    [InlineData("ABC123", "ABC123")]
    [InlineData("abc123", "ABC123")]
    [InlineData(" abc 123 ", "ABC123")]
    [InlineData("ABC-123", "ABC123")]
    [InlineData("a-b c-1\t2 3", "ABC123")]
    [InlineData("abc12d", "ABC12D")]
    [InlineData("R12345", "R12345")]
    public void Normaliza_a_mayusculas_sin_espacios_ni_guiones(string entrada, string esperada)
    {
        Placa.TryNormalizar(entrada, out var placa).Should().BeTrue();
        placa.Should().Be(esperada);
        Placa.Normalizar(entrada).Should().Be(esperada);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("--")]
    [InlineData("ABC.123")]
    [InlineData("ABC_123")]
    [InlineData("ÁBC123")]
    [InlineData("ABCDEFG12345")]
    public void Rechaza_lo_que_no_es_una_placa(string? entrada)
    {
        Placa.TryNormalizar(entrada, out var placa).Should().BeFalse();
        placa.Should().BeEmpty();
        var act = () => Placa.Normalizar(entrada);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void El_largo_maximo_es_el_de_la_columna()
    {
        Placa.LargoMaximo.Should().Be(10);
        Placa.TryNormalizar("ABCDE-12345", out var diez).Should().BeTrue("el guion no cuenta");
        diez.Should().HaveLength(10);
    }
}
