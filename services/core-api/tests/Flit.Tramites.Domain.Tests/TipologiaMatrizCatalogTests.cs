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
        TipologiaMatrizCatalog.DriftIssues().Should().BeEmpty();
    }
}
