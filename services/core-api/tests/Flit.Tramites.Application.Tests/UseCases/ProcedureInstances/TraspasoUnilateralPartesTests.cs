using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Tramites.Services;
using FluentAssertions;
using Xunit;

namespace Flit.Tramites.Application.Tests.UseCases.ProcedureInstances;

/// <summary>
/// ADR-0051 — el traductor ÚNICO de capacidades declaradas a partes internas
/// (<see cref="PartesDeclaradas"/>). Antes de extraerlo había dos copias con criterios distintos
/// —la de <c>FurCommand</c> y la de <c>BiometricaCommand</c>—, que es la misma clase de divergencia
/// que el ADR vino a cerrar: el FUR y la biométrica podían discrepar sobre quién interviene en el
/// mismo trámite.
/// </summary>
public sealed class TraspasoUnilateralPartesTests
{
    private static ProcedureTypeGateProfile Perfil(string json) => ProcedureTypeGateProfile.FromJson(json);

    private static ProcedureTypeGateProfile Unilateral =>
        Perfil(ProcedureTypeFixture.TraspasoUnilateral.GateProfile);

    private static ProcedureTypeGateProfile Standard =>
        Perfil(ProcedureTypeFixture.Traspaso.GateProfile);

    private static ProcedureTypeGateProfile Matricula =>
        Perfil(ProcedureTypeFixture.Matricula.GateProfile);

    [Fact]
    public void Unilateral_SoloElPropietarioFirmaYValidaIdentidad()
    {
        PartesDeclaradas.Firma(Unilateral).Should().Equal("vendedor");
        PartesDeclaradas.Identidad(Unilateral).Should().Equal("vendedor");
    }

    [Fact]
    public void TraspasoStandard_ConservaLasDosPartes()
    {
        // Control de no-regresión: el tipo que no declara las llaves nuevas no cambia de conducta.
        PartesDeclaradas.Firma(Standard).Should().BeEquivalentTo("vendedor", "comprador");
        PartesDeclaradas.Identidad(Standard).Should().BeEquivalentTo("vendedor", "comprador");
    }

    [Fact]
    public void Matricula_SoloComprador()
    {
        PartesDeclaradas.Firma(Matricula).Should().Equal("comprador");
        PartesDeclaradas.Identidad(Matricula).Should().Equal("comprador");
    }

    [Fact]
    public void DeCatalogo_TraduceElVocabularioDelCatalogoAlActorTypeInterno()
    {
        PartesDeclaradas.DeCatalogo(["OWNER", "BUYER", "LESSEE"], requiresSeller: true)
            .Should().Equal("vendedor", "comprador", "locatario");
    }

    [Fact]
    public void DeCatalogo_ConjuntoVacio_CaeAlCriterioPrevioAlAdr()
    {
        PartesDeclaradas.DeCatalogo([], requiresSeller: true)
            .Should().BeEquivalentTo("vendedor", "comprador");
        PartesDeclaradas.DeCatalogo([], requiresSeller: false)
            .Should().Equal("comprador");
    }

    [Fact]
    public void DeCatalogo_RolDesconocido_SeDescartaSinRomper()
    {
        // Un perfil con un código que el catálogo no sabe traducir no puede tumbar la generación del
        // FUR ni el listado: se ignora esa parte y el resto sigue.
        PartesDeclaradas.DeCatalogo(["OWNER", "MARCIANO"], requiresSeller: true)
            .Should().Equal("vendedor");
    }

    [Fact]
    public void EnOrden_NormalizaAlOrdenDePresentacion_SinPerderPartes()
    {
        // El perfil declara el orden que quiera; el listado de biométricas presenta siempre igual.
        PartesDeclaradas.EnOrden(["locatario", "vendedor", "comprador"])
            .Should().Equal("comprador", "vendedor", "locatario");
        // Una parte fuera del orden conocido se conserva al final en vez de desaparecer.
        PartesDeclaradas.EnOrden(["heredero", "comprador"]).Should().Equal("comprador", "heredero");
    }

    [Fact]
    public void Incluye_IgnoraMayusculas()
    {
        PartesDeclaradas.Incluye(["Vendedor"], "vendedor").Should().BeTrue();
        PartesDeclaradas.Incluye(["vendedor"], "comprador").Should().BeFalse();
    }
}
