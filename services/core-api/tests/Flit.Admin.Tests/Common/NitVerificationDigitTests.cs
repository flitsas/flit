using Flit.Admin.Domain.Common;
using FluentAssertions;
using Xunit;

namespace Flit.Admin.Tests.Common;

/// <summary>
/// HU #12206 (CF-25) — dígito de verificación del NIT, módulo 11 DIAN.
///
/// <para>La tabla cubre lo que el AC exige: NIT de 9 dígitos, NIT de más de 9, y los <b>residuos 0 y
/// 1</b>, que es donde el algoritmo tiene su caso especial (el dígito ES el residuo; aplicar
/// <c>11 - r</c> ahí produciría 11 y 10, que no son dígitos).</para>
///
/// Uso de ejemplo:
/// <code>
/// NitVerificationDigit.Compute("890903938"); // 8
/// </code>
/// </summary>
public sealed class NitVerificationDigitTests
{
    /// <summary>
    /// NITs de 9 dígitos con DV público y verificable (entidades reales, dato público de registro —
    /// no es PII de una persona natural).
    /// </summary>
    [Theory]
    [InlineData("890903938", 8)]   // Bancolombia S.A.
    [InlineData("860002964", 4)]   // Banco de Bogotá S.A.
    [InlineData("899999061", 9)]   // Bogotá D.C.
    [InlineData("900123456", 8)]
    [InlineData("830084433", 7)]
    public void NitDeNueveDigitos_DevuelveElDigitoCorrecto(string nit, int esperado)
    {
        NitVerificationDigit.Compute(nit).Should().Be(esperado);
    }

    /// <summary>
    /// El caso especial del algoritmo. <c>900000009</c> deja residuo 0 y <c>900000002</c> residuo 1:
    /// en ambos el DV es el propio residuo.
    /// </summary>
    [Theory]
    [InlineData("900000009", 0)]
    [InlineData("900000002", 1)]
    [InlineData("1000000004", 0)]
    [InlineData("1000000008", 1)]
    public void ResiduoCeroOUno_ElDigitoEsElResiduo(string nit, int esperado)
    {
        NitVerificationDigit.Compute(nit).Should().Be(esperado);
    }

    /// <summary>NITs de más de 9 dígitos: la ponderación depende de la posición, no de la longitud.</summary>
    [Theory]
    [InlineData("9001234567", 0)]
    [InlineData("12345678901", 2)]
    public void NitDeMasDeNueveDigitos_DevuelveElDigitoCorrecto(string nit, int esperado)
    {
        NitVerificationDigit.Compute(nit).Should().Be(esperado);
    }

    [Fact]
    public void ElDigitoSiempreEstaEntreCeroYNueve()
    {
        for (var nit = 900000000; nit < 900001000; nit++)
        {
            NitVerificationDigit
                .Compute(nit.ToString(System.Globalization.CultureInfo.InvariantCulture))
                .Should().BeInRange(0, 9);
        }
    }

    [Theory]
    [InlineData("900.123.456", 8)]
    [InlineData("900-123-456", 8)]
    [InlineData(" 900 123 456 ", 8)]
    public void IgnoraPuntosGuionesYEspacios(string nit, int esperado)
    {
        NitVerificationDigit.Compute(nit).Should().Be(esperado);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("900ABC456")]
    [InlineData("1234567890123456")]  // 16 dígitos: fuera de la tabla de ponderaciones
    public void NitNoCalculable_TryComputeDevuelveFalse(string? nit)
    {
        NitVerificationDigit.TryCompute(nit, out var digit).Should().BeFalse();
        digit.Should().Be(0);
    }

    [Fact]
    public void NitNoCalculable_ComputeLanzaSinFiltrarElValor()
    {
        var acto = () => NitVerificationDigit.Compute("900ABC456");

        // El NIT es PII (Ley 1581): el mensaje de la excepción puede acabar en un log y no debe
        // repetir el valor recibido.
        acto.Should().Throw<ArgumentException>()
            .Which.Message.Should().NotContain("900ABC456");
    }

    [Fact]
    public void EsUnaFuncionPuraDelDominio()
    {
        var tipo = typeof(NitVerificationDigit);

        tipo.Namespace.Should().Be("Flit.Admin.Domain.Common");
        tipo.IsAbstract.Should().BeTrue("una clase estática no se instancia");
        tipo.IsSealed.Should().BeTrue();

        // Sin estado y sin dependencias: nada que inyectar, nada que configurar.
        tipo.GetConstructors().Should().BeEmpty();
        tipo.GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
            .Should().BeEmpty();

        // Determinista: mil llamadas, el mismo resultado.
        var primero = NitVerificationDigit.Compute("900123456");
        Enumerable.Range(0, 1000)
            .Select(_ => NitVerificationDigit.Compute("900123456"))
            .Should().AllSatisfy(d => d.Should().Be(primero));
    }
}
