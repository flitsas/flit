using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Tramites.ValueObjects;
using FluentAssertions;
using Xunit;

namespace Flit.Tramites.Application.Tests.UseCases.ProcedureInstances;

/// <summary>
/// HU #10989 (Feature #10972), ampliado por HU #11257 (Feature #11254, CF11) — el acreedor de la
/// prenda se capturaba en el wizard, se persistía, llegaba hasta el generador y se descartaba: el FUR
/// marcaba la casilla de gravamen pero no decía a favor de quién. Aquí se cubre el bloque de
/// observaciones que lo declara, tanto en constitución como en levantamiento.
/// </summary>
public sealed class FurPrendaObservationTests
{
    // ── Constitución ───────────────────────────────────────────────────────

    [Fact]
    public void Compose_Constitucion_ConAcreedorYDocumento_DeclaraElBeneficiario()
    {
        var texto = FurPrendaObservation.Compose(FurPrendaMarking.Constitucion, "BANCO XYZ S.A.", "890900608");

        texto.Should().Be("Inscripción de prenda a favor de BANCO XYZ S.A.");
    }

    [Fact]
    public void Compose_Constitucion_IgnoraNit_SoloNombre()
    {
        var conNit = FurPrendaObservation.Compose(FurPrendaMarking.Constitucion, "FONDEICON", "890900608");
        var sinNit = FurPrendaObservation.Compose(FurPrendaMarking.Constitucion, "FONDEICON", null);

        conNit.Should().Be("Inscripción de prenda a favor de FONDEICON");
        sinNit.Should().Be(conNit);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Compose_Constitucion_SinNombreDeAcreedor_NoInventaContenido(string? nombre)
    {
        // La casilla del FUR se marca por su propia vía; lo que se omite aquí es solo el texto.
        FurPrendaObservation.Compose(FurPrendaMarking.Constitucion, nombre, "890900608").Should().BeNull();
    }

    // ── Levantamiento (CF11, HU #11257) ───────────────────────────────────

    [Fact]
    public void Compose_Levantamiento_ConAcreedorYDocumento_DeclaraElBeneficiario()
    {
        var texto = FurPrendaObservation.Compose(FurPrendaMarking.Levantamiento, "BANCO XYZ S.A.", "890900608");

        texto.Should().Be("Levantamiento de prenda a favor de BANCO XYZ S.A.");
    }

    [Fact]
    public void Compose_Levantamiento_SinDocumento_ImprimeSoloElNombre()
    {
        var texto = FurPrendaObservation.Compose(FurPrendaMarking.Levantamiento, "BANCO XYZ S.A.", null);

        texto.Should().Be("Levantamiento de prenda a favor de BANCO XYZ S.A.");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Compose_Levantamiento_SinNombreDeAcreedor_NoImprimeNada(string? nombre)
    {
        // Sin nombre de acreedor capturado no se imprime nada: no se inventa contenido, igual que en
        // la ruta de constitución.
        FurPrendaObservation.Compose(FurPrendaMarking.Levantamiento, nombre, "890900608").Should().BeNull();
    }

    [Fact]
    public void Compose_Levantamiento_NuncaUsaElLiteralDeConstitucion()
    {
        var texto = FurPrendaObservation.Compose(FurPrendaMarking.Levantamiento, "BANCO XYZ S.A.", null);

        texto.Should().NotContain(FurPrendaObservation.Etiqueta);
        texto.Should().StartWith(FurPrendaObservation.EtiquetaLevantamiento);
    }

    // ── Ninguna ────────────────────────────────────────────────────────────

    [Fact]
    public void Compose_Ninguna_NoEscribeNada()
    {
        // Decisiones sin_prenda / omitir: aunque queden datos de una decisión anterior.
        FurPrendaObservation.Compose(FurPrendaMarking.Ninguna, "BANCO XYZ S.A.", "890900608").Should().BeNull();
    }

    [Fact]
    public void Compose_RecortaLosEspaciosDelCaptura()
    {
        FurPrendaObservation.Compose(FurPrendaMarking.Constitucion, "  BANCO XYZ S.A.  ", "  890900608  ")
            .Should().Be("Inscripción de prenda a favor de BANCO XYZ S.A.");
    }

    // ── Join: el gravamen se antepone al resto del recuadro ──────────────────

    [Fact]
    public void Join_AnteponeElGravamenAlRestoDeObservaciones()
    {
        var resultado = FurPrendaObservation.Join(
            "Inscripción de prenda a favor de BANCO XYZ S.A.",
            "Vehículo con platón adaptado. Color nuevo(NUEVO COLOR: ROJO)");

        resultado.Should().Be(
            "Inscripción de prenda a favor de BANCO XYZ S.A. Vehículo con platón adaptado. Color nuevo(NUEVO COLOR: ROJO)");
    }

    [Fact]
    public void Join_SinGravamen_DevuelveElRestoIntacto()
    {
        // Regresión ADR-0029: un trámite sin prenda debe conservar exactamente el texto previo.
        FurPrendaObservation.Join(null, "Color nuevo(NUEVO COLOR: ROJO)").Should().Be("Color nuevo(NUEVO COLOR: ROJO)");
    }

    [Fact]
    public void Join_SoloGravamen_DevuelveElBloque()
    {
        FurPrendaObservation.Join("Inscripción de prenda a favor de BANCO XYZ S.A.", null)
            .Should().Be("Inscripción de prenda a favor de BANCO XYZ S.A.");
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("", "")]
    [InlineData("   ", "   ")]
    public void Join_SinNada_DejaElRecuadroComoEstaba(string? a, string? b)
    {
        FurPrendaObservation.Join(a, b).Should().BeNull();
    }

    [Fact]
    public void Compose_Ambos_UneLevantamientoYConstitucion()
    {
        var texto = FurPrendaObservation.Compose(FurPrendaMarking.Ambos, "BANCO XYZ S.A.", "890900608");
        texto.Should().StartWith(FurPrendaObservation.EtiquetaLevantamiento);
        texto.Should().Contain(FurPrendaObservation.Etiqueta);
        texto.Should().NotContain("NIT");
    }

    // ── Levantamiento con entidad: el trámite dedicado declara DÓNDE se hizo ─────────────────────

    [Fact]
    public void Compose_Levantamiento_ConEntidad_DeclaraDondeSeHizo()
    {
        // El acreedor ya lo nombra el numeral 20 «A FAVOR DE»; repetirlo en el recuadro gastaba
        // renglones sin añadir información. Lo que falta decir es ante quién se levantó.
        var texto = FurPrendaObservation.Compose(
            FurPrendaMarking.Levantamiento, "BANCO SANTANDER COLOMBIA S.A.", "890903938",
            levantamientoEntidad: "NOTARÍA 15 DE MEDELLÍN");

        texto.Should().Be("Levantamiento de prenda ante NOTARÍA 15 DE MEDELLÍN");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Compose_Levantamiento_SinEntidad_ConservaElLiteralDeSiempre(string? entidad)
    {
        // No regresión: traspaso y matrícula NO capturan la entidad, así que su texto no cambia.
        var texto = FurPrendaObservation.Compose(
            FurPrendaMarking.Levantamiento, "BANCO XYZ S.A.", "890900608", entidad);

        texto.Should().Be("Levantamiento de prenda a favor de BANCO XYZ S.A.");
    }

    [Fact]
    public void Compose_Levantamiento_ConEntidadYSinAcreedor_SigueDeclarando()
    {
        // La entidad basta para que el recuadro diga algo: la casilla 12 ya no queda muda aunque el
        // acreedor falte (el gate lo exige aparte, pero el documento no debe salir en blanco).
        FurPrendaObservation.Compose(
            FurPrendaMarking.Levantamiento, null, null, "NOTARÍA 15 DE MEDELLÍN")
            .Should().Be("Levantamiento de prenda ante NOTARÍA 15 DE MEDELLÍN");
    }

    [Fact]
    public void Compose_Ambos_ConEntidad_UsaLaEntidadSoloEnElLevantamiento()
    {
        var texto = FurPrendaObservation.Compose(
            FurPrendaMarking.Ambos, "BANCO XYZ S.A.", "890900608", "NOTARÍA 15 DE MEDELLÍN");

        texto.Should().StartWith("Levantamiento de prenda ante NOTARÍA 15 DE MEDELLÍN");
        texto.Should().Contain("Inscripción de prenda a favor de BANCO XYZ S.A.");
    }
}
