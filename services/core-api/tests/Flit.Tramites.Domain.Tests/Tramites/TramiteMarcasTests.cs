using Flit.Tramites.Domain.Documents;
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

    // ── HU #12199 — las listas que viajan a SQL ──────────────────────────────────────────────
    //
    // El filtro del listado no puede llamar a estos predicados: un `switch` de C# no se traduce a
    // WHERE. Lo que viaja son las listas de códigos y de valores afirmativos, así que el riesgo real
    // es que una lista y su predicado se separen y el filtro deje de coincidir con el ícono.

    [Fact]
    public void LosCodigosDeTransformacionSonExactamenteLosQueElTipoTransforma()
    {
        // `CodigosTransformacion` no puede derivarse del switch —ese devuelve QUÉ atributo cambia—,
        // así que la coherencia la sostiene esta prueba y no el compilador.
        foreach (var codigo in ProcedureTypeLayers.CodigosTransformacion)
        {
            ProcedureTypeLayers.TransformacionDelTipo(codigo)
                .Should().NotBe(TransformacionBase.Ninguna, $"«{codigo}» está en la lista");
            ProcedureTypeLayers.EsTipoTransformacion(codigo).Should().BeTrue();
        }

        // Y al revés: ningún tipo del catálogo transforma sin estar en la lista.
        foreach (var codigo in CodigosDelCatalogo)
        {
            var transforma = ProcedureTypeLayers.TransformacionDelTipo(codigo) != TransformacionBase.Ninguna;
            transforma.Should().Be(ProcedureTypeLayers.CodigosTransformacion.Contains(codigo),
                $"«{codigo}» tiene que estar en la lista si y solo si transforma");
        }
    }

    [Fact]
    public void LosCodigosDePrendaBaseSonExactamenteLosDelPredicado()
    {
        foreach (var codigo in CodigosDelCatalogo)
            ProcedureTypeLayers.EsTipoPrendaBase(codigo)
                .Should().Be(ProcedureTypeLayers.CodigosPrendaBase.Contains(codigo), codigo);
    }

    [Theory]
    [InlineData("true")]
    [InlineData("1")]
    [InlineData("si")]
    [InlineData("sí")]
    public void LosValoresAfirmativosPublicadosSonLosQueElDominioAcepta(string valor)
    {
        // La lista es lo que el WHERE compara; si se quedara corta respecto del método, el filtro
        // perdería trámites que el listado sí marca.
        TramiteMarcas.ValoresAfirmativos.Should().Contain(valor);
        TramiteMarcas.TieneTransformacion(
            new Dictionary<string, string?> { [MandatoObjetoComposer.CambioColor] = $"  {valor.ToUpperInvariant()}  " },
            tipoCodigo: null).Should().BeTrue();
    }

    /// <summary>
    /// Los veintiún códigos parametrizados (seed <c>81-parametrizacion-tipos-operativos.sql</c>).
    /// Se listan a mano a propósito: un tipo nuevo en el seed que no llegue aquí deja de estar
    /// cubierto, y eso se ve en la revisión del seed, que es donde hay que decidirlo.
    /// </summary>
    private static readonly string[] CodigosDelCatalogo =
    [
        "MATRICULA_NUEVA", "MATRICULA_LEASING", "REMATRICULA", "TRASPASO_STANDARD",
        "TRASPASO_TRANSFERENCIA_DE_DOMINIO", "CAMBIO_LOCATARIO", "CAMBIO_COLOR",
        "CAMBIO_CARROCERIA", "CONVERSION_COMBUSTIBLE", "BLINDAJE", "PRENDA_INSCRIPCION",
        "LEVANTAMIENTO_PRENDA", "LEVANTAR_INSCRIBIR_PRENDA", "CAMBIO_ACREEDOR",
        "DUPLICADO_PLACA", "DUPLICADO_TARJETA", "REGRABAR_MOTOR_CHASIS", "RADICADO_CUENTA",
        "TRASLADO_CUENTA", "CANCELACION_MATRICULA",
    ];
}
