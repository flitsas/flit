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

        texto.Should().Be("GRAVAMEN / PRENDA A FAVOR DE: BANCO XYZ S.A. - NIT 890900608");
    }

    [Fact]
    public void Compose_Constitucion_SinDocumento_NoDejaSeparadoresSueltos()
    {
        var texto = FurPrendaObservation.Compose(FurPrendaMarking.Constitucion, "BANCO XYZ S.A.", null);

        texto.Should().Be("GRAVAMEN / PRENDA A FAVOR DE: BANCO XYZ S.A.");
        texto.Should().NotEndWith("-");
        texto.Should().NotContain("NIT");
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

        texto.Should().Be("LEVANTAMIENTO DE GRAVAMEN A FAVOR DE: BANCO XYZ S.A. - NIT 890900608");
    }

    [Fact]
    public void Compose_Levantamiento_SinDocumento_ImprimeSoloElNombre()
    {
        var texto = FurPrendaObservation.Compose(FurPrendaMarking.Levantamiento, "BANCO XYZ S.A.", null);

        texto.Should().Be("LEVANTAMIENTO DE GRAVAMEN A FAVOR DE: BANCO XYZ S.A.");
        texto.Should().NotContain("NIT");
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
            .Should().Be("GRAVAMEN / PRENDA A FAVOR DE: BANCO XYZ S.A. - NIT 890900608");
    }

    // ── Join: el gravamen se antepone al resto del recuadro ──────────────────

    [Fact]
    public void Join_AnteponeElGravamenAlRestoDeObservaciones()
    {
        var resultado = FurPrendaObservation.Join(
            "GRAVAMEN / PRENDA A FAVOR DE: BANCO XYZ S.A.",
            "Vehículo con platón adaptado. Cambio de color: ROJO.");

        resultado.Should().Be(
            "GRAVAMEN / PRENDA A FAVOR DE: BANCO XYZ S.A. Vehículo con platón adaptado. Cambio de color: ROJO.");
    }

    [Fact]
    public void Join_SinGravamen_DevuelveElRestoIntacto()
    {
        // Regresión ADR-0029: un trámite sin prenda debe conservar exactamente el texto previo.
        FurPrendaObservation.Join(null, "Cambio de color: ROJO.").Should().Be("Cambio de color: ROJO.");
    }

    [Fact]
    public void Join_SoloGravamen_DevuelveElBloque()
    {
        FurPrendaObservation.Join("GRAVAMEN / PRENDA A FAVOR DE: BANCO XYZ S.A.", null)
            .Should().Be("GRAVAMEN / PRENDA A FAVOR DE: BANCO XYZ S.A.");
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("", "")]
    [InlineData("   ", "   ")]
    public void Join_SinNada_DejaElRecuadroComoEstaba(string? a, string? b)
    {
        FurPrendaObservation.Join(a, b).Should().BeNull();
    }
}
