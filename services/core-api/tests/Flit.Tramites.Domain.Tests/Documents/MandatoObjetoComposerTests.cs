using Flit.Tramites.Domain.Documents;
using Flit.Tramites.Domain.Tramites.ValueObjects;
using FluentAssertions;
using Xunit;

namespace Flit.Tramites.Domain.Tests.Documents;

/// <summary>
/// Objeto <c>{{tramite}}</c> según <c>docs/ot/mandato/REGLAS-OBJETO-TRES-CAPAS.md</c>.
/// </summary>
public sealed class MandatoObjetoComposerTests
{
    [Fact]
    public void TraspasoMasColorYCarroceria_UsaConY()
    {
        var objeto = MandatoObjetoComposer.Componer(
            "TRASPASO",
            [MandatoObjetoComposer.CambioColor, MandatoObjetoComposer.CambioCarroceria]);

        objeto.Should().Be("TRASPASO CON CAMBIO DE COLOR Y CAMBIO DE CARROCERÍA");
    }

    [Fact]
    public void MatriculaMasCombustible_UsaCon()
    {
        var objeto = MandatoObjetoComposer.Componer(
            "MATRÍCULA INICIAL", [MandatoObjetoComposer.CambioCombustible]);

        objeto.Should().Be("MATRÍCULA INICIAL CON CONVERSIONES DE COMBUSTIBLE");
    }

    [Fact]
    public void SinComplementos_DevuelveElTramiteBase()
    {
        MandatoObjetoComposer.Componer("TRASPASO", null).Should().Be("TRASPASO");
        MandatoObjetoComposer.Componer("TRASPASO", []).Should().Be("TRASPASO");
    }

    [Fact]
    public void TresTransformaciones_OrdenFijoColorCarroceriaCombustible()
    {
        var objeto = MandatoObjetoComposer.Componer(
            "TRASPASO",
            [
                MandatoObjetoComposer.CambioCombustible,
                MandatoObjetoComposer.CambioColor,
                MandatoObjetoComposer.CambioCarroceria,
            ]);

        objeto.Should().Be(
            "TRASPASO CON CAMBIO DE COLOR Y CAMBIO DE CARROCERÍA Y CONVERSIONES DE COMBUSTIBLE");
    }

    [Fact]
    public void ElOrdenNoDependeDeComoLasMarcoElGestor()
    {
        var enUnOrden = MandatoObjetoComposer.Componer(
            "TRASPASO",
            [MandatoObjetoComposer.CambioCombustible, MandatoObjetoComposer.CambioColor]);
        var enElOtro = MandatoObjetoComposer.Componer(
            "TRASPASO",
            [MandatoObjetoComposer.CambioColor, MandatoObjetoComposer.CambioCombustible]);

        enUnOrden.Should().Be(enElOtro);
    }

    [Fact]
    public void LasClavesDesconocidasORepetidasNoEnsucianElTexto()
    {
        var objeto = MandatoObjetoComposer.Componer(
            "TRASPASO",
            [MandatoObjetoComposer.CambioColor, "cambio_inventado", "CAMBIO_COLOR", "  "]);

        objeto.Should().Be("TRASPASO CON CAMBIO DE COLOR");
    }

    [Fact]
    public void AC4_ElObjetoNoDependeDeLaFamiliaDePlantilla()
    {
        typeof(MandatoObjetoComposer).GetMethod(nameof(MandatoObjetoComposer.Componer))!
            .GetParameters().Should().HaveCount(4);
    }

    [Fact]
    public void MatriculaConInscripcionYCombustible()
    {
        var objeto = MandatoObjetoComposer.Componer(
            "MATRÍCULA INICIAL",
            [MandatoObjetoComposer.CambioCombustible],
            FurPrendaMarking.Constitucion);

        objeto.Should().Be("MATRÍCULA INICIAL CON INSCRIPCIÓN DE PRENDA Y CONVERSIONES DE COMBUSTIBLE");
    }

    [Fact]
    public void TraspasoConInscripcionDePrenda()
    {
        MandatoObjetoComposer.Componer("TRASPASO", [], FurPrendaMarking.Constitucion)
            .Should().Be("TRASPASO CON INSCRIPCIÓN DE PRENDA");
    }

    [Fact]
    public void TraspasoConLevantamientoDePrenda()
    {
        MandatoObjetoComposer.Componer("TRASPASO", [], FurPrendaMarking.Levantamiento)
            .Should().Be("TRASPASO CON LEVANTAMIENTO DE PRENDA");
    }

    [Fact]
    public void TraspasoConLevantamientoYColor()
    {
        MandatoObjetoComposer.Componer(
                "TRASPASO",
                [MandatoObjetoComposer.CambioColor],
                FurPrendaMarking.Levantamiento)
            .Should().Be("TRASPASO CON LEVANTAMIENTO DE PRENDA Y CAMBIO DE COLOR");
    }

    [Fact]
    public void AmbasPrendasYDosTransformaciones()
    {
        MandatoObjetoComposer.Componer(
                "TRASPASO",
                [MandatoObjetoComposer.CambioColor, MandatoObjetoComposer.CambioCarroceria],
                FurPrendaMarking.Ambos)
            .Should().Be(
                "TRASPASO CON LEVANTAMIENTO DE PRENDA Y INSCRIPCIÓN DE PRENDA Y CAMBIO DE COLOR Y CAMBIO DE CARROCERÍA");
    }

    [Fact]
    public void BlindajeComplementario()
    {
        MandatoObjetoComposer.Componer("TRASPASO", [MandatoObjetoComposer.Blindaje])
            .Should().Be("TRASPASO CON BLINDAJE");
    }

    [Fact]
    public void TipoBaseCambioColor_NoDuplicaLaCapaTres()
    {
        MandatoObjetoComposer.Componer(
                "CAMBIO DE COLOR",
                [MandatoObjetoComposer.CambioColor],
                procedureTypeCode: "CAMBIO_COLOR")
            .Should().Be("CAMBIO DE COLOR");
    }

    [Fact]
    public void TipoBaseInscribirPrenda_NoSumaTablaDos()
    {
        MandatoObjetoComposer.Componer(
                "INSCRIBIR PRENDA",
                null,
                FurPrendaMarking.Constitucion,
                "PRENDA_INSCRIPCION")
            .Should().Be("INSCRIBIR PRENDA");
    }
}
