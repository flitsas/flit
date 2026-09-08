using Flit.Tramites.Domain.Tramites.Services;
using FluentAssertions;
using Xunit;

namespace Flit.Tramites.Domain.Tests.Tramites;

/// <summary>
/// HU #12182 — las dos marcas del listado. Lo que se prueba aquí es la regla de los <b>dos
/// disparadores</b>: la marca vale tanto si el trámite lleva la capa encima como si la ES.
/// </summary>
public sealed class TramiteMarcasTests
{
    private static Dictionary<string, string?> Fv(params (string Clave, string? Valor)[] pares) =>
        pares.ToDictionary(p => p.Clave, p => p.Valor, StringComparer.OrdinalIgnoreCase);

    // ── Transformación ────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("cambio_color")]
    [InlineData("cambio_carroceria")]
    [InlineData("cambio_combustible")]
    [InlineData("blindaje")]
    public void TieneTransformacion_DeclaradaEnElFormulario_EsVerdadero(string clave)
    {
        TramiteMarcas.TieneTransformacion(Fv((clave, "true")), "TRASPASO_STANDARD")
            .Should().BeTrue();
    }

    [Theory]
    [InlineData("CAMBIO_COLOR")]
    [InlineData("CAMBIO_CARROCERIA")]
    [InlineData("CONVERSION_COMBUSTIBLE")]
    [InlineData("BLINDAJE")]
    public void TieneTransformacion_ElTipoEsLaTransformacion_EsVerdaderoSinDeclararNada(string codigo)
    {
        // ADR-0050: en la familia OTROS el cambio ES el trámite. Un CAMBIO_COLOR no declara
        // `cambio_color = true` en el formulario — no tendría a quién declarárselo.
        TramiteMarcas.TieneTransformacion(Fv(), codigo).Should().BeTrue();
    }

    [Theory]
    [InlineData("true")]
    [InlineData("TRUE")]
    [InlineData(" true ")]
    [InlineData("1")]
    [InlineData("si")]
    [InlineData("sí")]
    public void TieneTransformacion_ValoresAfirmativosDeV1_Cuentan(string valor)
    {
        // Los trámites migrados de V1 no guardan siempre "true": el criterio es el mismo que usa la
        // consulta de la empresa, para que las dos superficies no clasifiquen distinto el mismo trámite.
        TramiteMarcas.TieneTransformacion(Fv(("cambio_color", valor)), "TRASPASO_STANDARD")
            .Should().BeTrue();
    }

    [Theory]
    [InlineData("false")]
    [InlineData("no")]
    [InlineData("")]
    [InlineData(null)]
    public void TieneTransformacion_ValorNoAfirmativo_EsFalso(string? valor)
    {
        TramiteMarcas.TieneTransformacion(Fv(("cambio_color", valor)), "TRASPASO_STANDARD")
            .Should().BeFalse();
    }

    [Fact]
    public void TieneTransformacion_SinDeclararNadaYTipoCorriente_EsFalso()
    {
        TramiteMarcas.TieneTransformacion(Fv(("vin", "LRW…"), ("plate", "KYU631")), "MATRICULA_NUEVA")
            .Should().BeFalse();
    }

    [Fact]
    public void TieneTransformacion_TipoNulo_NoLanza()
    {
        // Un expediente servido sin tipo parametrizado no debe tumbar la fila del listado.
        TramiteMarcas.TieneTransformacion(Fv(), null).Should().BeFalse();
    }

    // ── Prenda ────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void TienePrenda_DecisionVigenteSobreUnTraspaso_EsVerdadero()
    {
        TramiteMarcas.TienePrenda(hayDecisionVigente: true, "TRASPASO_STANDARD").Should().BeTrue();
    }

    [Theory]
    [InlineData("PRENDA_INSCRIPCION")]
    [InlineData("LEVANTAMIENTO_PRENDA")]
    [InlineData("LEVANTAR_INSCRIBIR_PRENDA")]
    [InlineData("CAMBIO_ACREEDOR")]
    public void TienePrenda_ElTipoEsDePrenda_EsVerdaderoAntesDeCapturarLaDecision(string codigo)
    {
        // Un levantamiento de prenda es un trámite de prenda desde que se abre. Esperar a la
        // decisión dejaría sin marca justo a los trámites que se llaman así.
        TramiteMarcas.TienePrenda(hayDecisionVigente: false, codigo).Should().BeTrue();
    }

    [Fact]
    public void TienePrenda_SinDecisionYTipoCorriente_EsFalso()
    {
        TramiteMarcas.TienePrenda(hayDecisionVigente: false, "MATRICULA_NUEVA").Should().BeFalse();
    }

    [Fact]
    public void ClavesTransformacion_SonLasCuatroCapasDelMandato()
    {
        // El blindaje es una transformación como las otras tres. El catálogo de filtros de la
        // consulta de la empresa solo lista tres; esta marca no hereda esa omisión.
        TramiteMarcas.ClavesTransformacion.Should().BeEquivalentTo(
            ["cambio_color", "cambio_carroceria", "cambio_combustible", "blindaje"]);
    }
}
