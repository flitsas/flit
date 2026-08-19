using Flit.Tramites.Domain.Documents;
using Flit.Tramites.Domain.Tramites.ValueObjects;
using FluentAssertions;
using Xunit;

namespace Flit.Tramites.Domain.Tests.Documents;

/// <summary>
/// HU #11206 — el objeto del contrato de mandato nombra el trámite y las transformaciones del vehículo.
/// Es una función pura: por eso se prueba directamente, sin generar un PDF.
/// </summary>
public sealed class MandatoObjetoComposerTests
{
    [Fact]
    public void AC1_ConDosTransformaciones_ElObjetoNombraElTramiteYAmbas()
    {
        var objeto = MandatoObjetoComposer.Componer(
            "TRASPASO DE PROPIEDAD",
            [MandatoObjetoComposer.CambioColor, MandatoObjetoComposer.CambioCarroceria]);

        objeto.Should().Be("TRASPASO DE PROPIEDAD, CAMBIO DE COLOR Y CAMBIO DE CARROCERÍA");
    }

    [Fact]
    public void AC2_ConUnaSolaTransformacion_SeUneConY_SinComas()
    {
        var objeto = MandatoObjetoComposer.Componer(
            "MATRÍCULA INICIAL", [MandatoObjetoComposer.CambioCombustible]);

        objeto.Should().Be("MATRÍCULA INICIAL Y CAMBIO DE COMBUSTIBLE");
    }

    [Fact]
    public void AC3_SinTransformaciones_ElObjetoQuedaExactamenteComoHastaAhora()
    {
        MandatoObjetoComposer.Componer("TRASPASO DE PROPIEDAD", null)
            .Should().Be("TRASPASO DE PROPIEDAD");
        MandatoObjetoComposer.Componer("TRASPASO DE PROPIEDAD", [])
            .Should().Be("TRASPASO DE PROPIEDAD");
    }

    [Fact]
    public void ConLasTres_LasDosPrimerasVanConComaYLaUltimaConY()
    {
        var objeto = MandatoObjetoComposer.Componer(
            "TRASPASO DE PROPIEDAD",
            [
                MandatoObjetoComposer.CambioColor,
                MandatoObjetoComposer.CambioCarroceria,
                MandatoObjetoComposer.CambioCombustible,
            ]);

        objeto.Should().Be(
            "TRASPASO DE PROPIEDAD, CAMBIO DE COLOR, CAMBIO DE CARROCERÍA Y CAMBIO DE COMBUSTIBLE");
    }

    [Fact]
    public void ElOrdenNoDependeDeComoLasMarcoElGestor()
    {
        // Dos trámites con las mismas transformaciones deben leerse igual; si el orden dependiera del
        // orden de captura, dos contratos equivalentes saldrían distintos.
        var enUnOrden = MandatoObjetoComposer.Componer(
            "TRASPASO DE PROPIEDAD",
            [MandatoObjetoComposer.CambioCombustible, MandatoObjetoComposer.CambioColor]);
        var enElOtro = MandatoObjetoComposer.Componer(
            "TRASPASO DE PROPIEDAD",
            [MandatoObjetoComposer.CambioColor, MandatoObjetoComposer.CambioCombustible]);

        enUnOrden.Should().Be(enElOtro);
    }

    [Fact]
    public void LasClavesDesconocidasORepetidasNoEnsucianElTexto()
    {
        var objeto = MandatoObjetoComposer.Componer(
            "TRASPASO DE PROPIEDAD",
            [MandatoObjetoComposer.CambioColor, "cambio_inventado", "CAMBIO_COLOR", "  "]);

        objeto.Should().Be("TRASPASO DE PROPIEDAD Y CAMBIO DE COLOR");
    }

    [Fact]
    public void AC4_ElObjetoNoDependeDeLaFamiliaDePlantilla()
    {
        // La composición es una función del dominio, sin parámetro de familia ni de plantilla: por
        // construcción, todos los organismos redactan el objeto igual. HU #11627 agrega `prendaMarking`
        // (no es familia ni plantilla), así que el conteo pasa de 2 a 3 parámetros.
        typeof(MandatoObjetoComposer).GetMethod(nameof(MandatoObjetoComposer.Componer))!
            .GetParameters().Should().HaveCount(3);
    }

    // HU #11627 — la prenda no viaja por field_values (Feature #10585): se nombra con un parámetro
    // aparte, primero entre los elementos que siguen al nombre del trámite, antes de las transformaciones.
    // Decisión del PO 2026-08-19: el objeto distingue constitución (PRENDA) de levantamiento
    // (LEVANTAMIENTO DE PRENDA).

    [Fact]
    public void HU11627_AC1_MatriculaInicialConConstitucionYCambioDeCombustible()
    {
        var objeto = MandatoObjetoComposer.Componer(
            "MATRÍCULA INICIAL",
            [MandatoObjetoComposer.CambioCombustible],
            FurPrendaMarking.Constitucion);

        objeto.Should().Be("MATRÍCULA INICIAL, PRENDA Y CAMBIO DE COMBUSTIBLE");
    }

    [Fact]
    public void HU11627_AC2_MatriculaInicialSoloConConstitucion()
    {
        var objeto = MandatoObjetoComposer.Componer(
            "MATRÍCULA INICIAL", null, FurPrendaMarking.Constitucion);

        objeto.Should().Be("MATRÍCULA INICIAL Y PRENDA");
    }

    [Fact]
    public void HU11627_AC3_TraspasoConConstitucion()
    {
        var objeto = MandatoObjetoComposer.Componer(
            "TRASPASO DE PROPIEDAD", [], FurPrendaMarking.Constitucion);

        objeto.Should().Be("TRASPASO DE PROPIEDAD Y PRENDA");
    }

    [Fact]
    public void HU11627_AC4_MatriculaInicialConLevantamientoYCambioDeCombustible()
    {
        var objeto = MandatoObjetoComposer.Componer(
            "MATRÍCULA INICIAL",
            [MandatoObjetoComposer.CambioCombustible],
            FurPrendaMarking.Levantamiento);

        objeto.Should().Be("MATRÍCULA INICIAL, LEVANTAMIENTO DE PRENDA Y CAMBIO DE COMBUSTIBLE");
    }

    [Fact]
    public void HU11627_AC5_MatriculaInicialSoloConLevantamiento()
    {
        var objeto = MandatoObjetoComposer.Componer(
            "MATRÍCULA INICIAL", null, FurPrendaMarking.Levantamiento);

        objeto.Should().Be("MATRÍCULA INICIAL Y LEVANTAMIENTO DE PRENDA");
    }

    [Fact]
    public void HU11627_AC6_TraspasoConLevantamiento()
    {
        var objeto = MandatoObjetoComposer.Componer(
            "TRASPASO DE PROPIEDAD", [], FurPrendaMarking.Levantamiento);

        objeto.Should().Be("TRASPASO DE PROPIEDAD Y LEVANTAMIENTO DE PRENDA");
    }

    [Fact]
    public void HU11627_AC7_SinPrenda_ElObjetoEsIdenticoAlDeHoy()
    {
        // Regresión: ninguna de las 3 transformaciones cambia etiqueta ni orden sin marcación de prenda.
        MandatoObjetoComposer.Componer("TRASPASO DE PROPIEDAD", null, FurPrendaMarking.Ninguna)
            .Should().Be("TRASPASO DE PROPIEDAD");
        MandatoObjetoComposer.Componer(
                "TRASPASO DE PROPIEDAD",
                [
                    MandatoObjetoComposer.CambioColor,
                    MandatoObjetoComposer.CambioCarroceria,
                    MandatoObjetoComposer.CambioCombustible,
                ],
                FurPrendaMarking.Ninguna)
            .Should().Be(
                "TRASPASO DE PROPIEDAD, CAMBIO DE COLOR, CAMBIO DE CARROCERÍA Y CAMBIO DE COMBUSTIBLE");
    }
}
