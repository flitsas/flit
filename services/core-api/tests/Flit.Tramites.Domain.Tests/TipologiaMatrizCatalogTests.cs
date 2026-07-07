using FluentAssertions;
using Flit.Tramites.Domain.Tramites.Catalog;
using Flit.Tramites.Domain.Tramites.Enums;
using Xunit;

namespace Flit.Tramites.Domain.Tests;

public sealed class TipologiaMatrizCatalogTests
{
    [Fact]
    public void VendedorRequerido_SoloTraspaso()
    {
        TipologiaMatrizCatalog.VendedorRequerido(TramiteTipologiaCatalog.CodigoTraspasoStandard).Should().BeTrue();
        TipologiaMatrizCatalog.VendedorRequerido(TramiteTipologiaCatalog.CodigoMatriculaInicial).Should().BeFalse();
        TipologiaMatrizCatalog.VendedorRequerido(null).Should().BeFalse();
        TipologiaMatrizCatalog.VendedorRequerido("desconocido").Should().BeFalse();
    }

    [Fact]
    public void GetAdquirente_CaeAlGenericoComprador()
    {
        var def = TipologiaMatrizCatalog.GetAdquirente(null);
        def.Rol.Should().Be(ParteRol.Comprador);

        var traspaso = TipologiaMatrizCatalog.GetAdquirente(TramiteTipologiaCatalog.CodigoTraspasoStandard);
        traspaso.Rol.Should().Be(ParteRol.Comprador);
    }

    [Fact]
    public void GetPartesRequeridas_TraspasoIncluyeVendedorYComprador()
    {
        var partes = TipologiaMatrizCatalog.GetPartesRequeridas(TramiteTipologiaCatalog.CodigoTraspasoStandard);
        partes.Should().Contain(p => p.Rol == ParteRol.Vendedor && p.Obligatorio);
        partes.Should().Contain(p => p.Rol == ParteRol.Comprador && p.Obligatorio);
    }

    [Fact]
    public void GetPartesRequeridas_MatriculaSoloComprador()
    {
        var partes = TipologiaMatrizCatalog.GetPartesRequeridas(TramiteTipologiaCatalog.CodigoMatriculaInicial);
        partes.Should().ContainSingle().Which.Rol.Should().Be(ParteRol.Comprador);
    }

    [Fact]
    public void GetPasoTipologia_DevuelvePasoContextual()
    {
        var paso6 = TipologiaMatrizCatalog.GetPasoTipologia(TramiteTipologiaCatalog.CodigoTraspasoStandard, 6);
        paso6.Should().NotBeNull();
        paso6!.Titulo.Should().Contain("FUR");

        TipologiaMatrizCatalog.GetPasoTipologia(TramiteTipologiaCatalog.CodigoMatriculaInicial, 6)
            .Should().BeNull(); // matrícula solo tiene 5 pasos
    }

    [Fact]
    public void DriftIssues_CatalogoSano()
    {
        // Cubre TODOS los journeys, incluido el traspaso unilateral (HU #10590): sin desincronización
        // entre el catálogo de checklist, las partes/adquirente y el nº de pasos esperado por modalidad.
        TipologiaMatrizCatalog.DriftIssues().Should().BeEmpty();
    }

    // ── HU #10590 — Traspaso unilateral ───────────────────────────────────────

    [Fact]
    public void TraspasoUnilateral_JourneyExisteConModalidadPropia()
    {
        var journey = TipologiaMatrizCatalog.Get(TramiteTipologiaCatalog.CodigoTraspasoUnilateral);

        journey.Should().NotBeNull();
        journey!.Modalidad.Should().Be(TramiteModalidadEntrada.TraspasoUnilateral);
        journey.VendedorRequerido.Should().BeFalse(); // no es compraventa directa
        journey.Pasos.Should().HaveCount(5);
    }

    [Fact]
    public void TraspasoUnilateral_PartesArrendadoraYLocatario()
    {
        var partes = TipologiaMatrizCatalog.GetPartesRequeridas(TramiteTipologiaCatalog.CodigoTraspasoUnilateral);

        partes.Should().Contain(p => p.Rol == ParteRol.Arrendadora && p.Obligatorio);
        partes.Should().Contain(p => p.Rol == ParteRol.Locatario && p.Obligatorio);
        partes.Should().NotContain(p => p.Rol == ParteRol.Vendedor);
    }

    [Fact]
    public void TraspasoUnilateral_ChecklistExigeLosCuatroDocumentos()
    {
        var tip = TramiteTipologiaCatalog.Get(TramiteTipologiaCatalog.CodigoTraspasoUnilateral);
        tip.Should().NotBeNull();

        var obligatorios = tip!.Checklist.Where(i => i.Obligatorio).Select(i => i.DocTipo).ToList();
        obligatorios.Should().BeEquivalentTo(new[]
        {
            "paz_salvo_locatario", "doc_locatario", "contrato_leasing", "declaracion_arrendadora",
        });
    }

    [Fact]
    public void TraspasoUnilateral_DocLocatarioAyudaMencionaNit()
    {
        var tip = TramiteTipologiaCatalog.Get(TramiteTipologiaCatalog.CodigoTraspasoUnilateral);
        var docLocatario = tip!.Checklist.Single(i => i.Id == "doc_locatario");

        docLocatario.Ayuda.Should().Contain("NIT");
        docLocatario.Ayuda.Should().Contain("un solo archivo");
    }
}
