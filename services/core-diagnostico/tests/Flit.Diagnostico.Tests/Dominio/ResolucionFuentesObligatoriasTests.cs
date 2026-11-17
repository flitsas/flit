using Flit.Diagnostico.Domain.Catalogos;
using Flit.Diagnostico.Domain.Consultas;
using FluentAssertions;
using Xunit;

namespace Flit.Diagnostico.Tests.Dominio;

/// <summary>
/// ADR-0078 — resolución y piso de las fuentes obligatorias. La cabecera del tipo gana aunque esté vacía; sin ella se
/// hereda la familia; sin ninguna, <c>[]</c>. <see cref="ResolucionFuentesObligatorias.ValidarGuardado"/> rechaza
/// <c>RUNT</c>, un <c>SIMIT_*</c> en el unilateral y quitar <c>SIMIT_PROPIETARIO</c> del piso. No persiste.
/// </summary>
/// <example>
/// <code>
/// dotnet test services/core-diagnostico/tests/Flit.Diagnostico.Tests --filter FullyQualifiedName~ResolucionFuentesObligatoriasTests
/// </code>
/// </example>
public sealed class ResolucionFuentesObligatoriasTests
{
    // La cabecera del unilateral nace vacía y no hereda el SIMIT de la familia TRASPASO.
    [Fact]
    public void Cabecera_de_tipo_vacia_gana_a_la_familia()
    {
        ResolucionFuentesObligatorias.Resolver(
                ResolucionFuentesObligatorias.TraspasoUnilateral,
                ResolucionFuentesObligatorias.FamiliaTraspaso,
                ResolucionFuentesObligatorias.Semilla)
            .Should().BeEmpty();
    }

    // Un tipo de la familia sin cabecera propia publica la línea de la familia.
    [Fact]
    public void Sin_cabecera_de_tipo_hereda_la_familia()
    {
        var soloFamilia = ResolucionFuentesObligatorias.Semilla.Where(d => d.TipoTramiteCodigo is null);

        ResolucionFuentesObligatorias.Resolver("TRASPASO_NUEVO", ResolucionFuentesObligatorias.FamiliaTraspaso, soloFamilia)
            .Should().Equal(Fuente.SimitPropietario);
    }

    // Sin cabecera de tipo ni de familia no hay nada que publicar.
    [Fact]
    public void Sin_cabecera_de_tipo_ni_de_familia_devuelve_vacio()
    {
        ResolucionFuentesObligatorias.Resolver("MATRICULA_INICIAL", "MATRICULAS", ResolucionFuentesObligatorias.Semilla)
            .Should().BeEmpty();
        ResolucionFuentesObligatorias.Resolver("OTRO", null, []).Should().BeEmpty();
    }

    // RUNT no entra en este catálogo.
    [Fact]
    public void Validar_guardado_rechaza_runt()
    {
        var rechazo = ResolucionFuentesObligatorias.ValidarGuardado(
            null, ResolucionFuentesObligatorias.TraspasoStandard, [Fuente.Runt, Fuente.SimitPropietario]);

        rechazo.Should().NotBeNull();
        rechazo!.Codigo.Should().Be(RechazoGuardadoFuentes.FuenteRuntNoConfigurable);
        rechazo.Campo.Should().Be(RechazoGuardadoFuentes.CampoFuentes);
    }

    // Cualquier SIMIT_* en el unilateral se rechaza. Ampliar el ordinario con otro SIMIT sí pasa.
    [Theory]
    [InlineData(Fuente.SimitVehiculo)]
    [InlineData(Fuente.SimitPropietario)]
    [InlineData(Fuente.SimitLocatario)]
    public void Validar_guardado_rechaza_un_simit_en_el_unilateral(Fuente simit)
    {
        var rechazo = ResolucionFuentesObligatorias.ValidarGuardado(null, ResolucionFuentesObligatorias.TraspasoUnilateral, [simit]);

        rechazo.Should().NotBeNull();
        rechazo!.Codigo.Should().Be(RechazoGuardadoFuentes.SimitProhibidoEnUnilateral);
        rechazo.Campo.Should().Be(RechazoGuardadoFuentes.CampoFuentes);

        ResolucionFuentesObligatorias.ValidarGuardado(
                null, ResolucionFuentesObligatorias.TraspasoStandard, [Fuente.SimitPropietario, Fuente.SimitVehiculo])
            .Should().BeNull();
    }

    // El piso no se puede quitar del ordinario, de la transferencia ni de la familia.
    [Theory]
    [InlineData(null, ResolucionFuentesObligatorias.TraspasoStandard)]
    [InlineData(null, ResolucionFuentesObligatorias.TraspasoTransferenciaDeDominio)]
    [InlineData(ResolucionFuentesObligatorias.FamiliaTraspaso, null)]
    public void Validar_guardado_rechaza_quitar_simit_propietario_del_piso(string? familia, string? tipo)
    {
        var rechazo = ResolucionFuentesObligatorias.ValidarGuardado(familia, tipo, [Fuente.SimitVehiculo]);

        rechazo.Should().NotBeNull();
        rechazo!.Codigo.Should().Be(RechazoGuardadoFuentes.PisoNoDispensable);
        rechazo.Campo.Should().Be(RechazoGuardadoFuentes.CampoFuentes);
    }
}
