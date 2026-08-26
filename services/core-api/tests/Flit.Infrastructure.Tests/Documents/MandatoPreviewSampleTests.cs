using Flit.Infrastructure.Documents;
using Flit.Tramites.Application.Documents;
using Flit.Tramites.Domain.Documents;
using FluentAssertions;
using Xunit;

namespace Flit.Infrastructure.Tests.Documents;

public sealed class MandatoPreviewSampleTests
{
    private static readonly MandatoPdfGenerator Generator = new();

    [Theory]
    [InlineData("generico")]
    [InlineData("sabaneta")]
    [InlineData("bello")]
    [InlineData("municipio")]
    public void Build_ProducesValidPdf(string templateCode)
    {
        var data = MandatoPreviewSample.Build(templateCode);
        data.TemplateCode.Should().Be(templateCode);

        var doc = Generator.GenerateMandato(data);
        doc.Tipo.Should().Be("mandato");
        doc.Content.Should().NotBeEmpty();
        System.Text.Encoding.ASCII.GetString(doc.Content, 0, 4).Should().Be("%PDF");
    }

    [Fact]
    public void Build_PersonaNatural_SinBloqueDeRepresentanteLegal()
    {
        // HU #11706 AC2 — el tipo de persona del mandante cambia la redacción. En natural NO puede
        // aparecer el bloque de representante legal, razón social ni NIT.
        var data = MandatoPreviewSample.Build(MandatoTemplateResolver.Municipio, esJuridica: false);

        var mandante = data.Tramite.Mandante;
        mandante!.EsJuridica.Should().BeFalse();
        mandante.RepresentanteLegalNombre.Should().BeNull();
        mandante.Nombre.Should().Be(MandatoPreviewSample.PhPnNombre);
        mandante.Documento.Should().Be(MandatoPreviewSample.PhPnDocumento);
    }

    [Fact]
    public void Build_PersonaJuridica_ConservaElBloqueCompleto()
    {
        var data = MandatoPreviewSample.Build(MandatoTemplateResolver.Municipio, esJuridica: true);

        var mandante = data.Tramite.Mandante;
        mandante!.EsJuridica.Should().BeTrue();
        mandante.RepresentanteLegalNombre.Should().Be(MandatoPreviewSample.PhRlNombre);
        mandante.Nombre.Should().Be(MandatoPreviewSample.PhRazonSocial);
        mandante.Documento.Should().Be(MandatoPreviewSample.PhNit);
    }

    [Fact]
    public void Build_ConOrganismoYMandatarioReales_LosNombraEnLugarDeLosMarcadores()
    {
        // HU #11706 AC3 — el mandatario elegido sale en el documento; el organismo simulado es el real.
        var organismo = new OrganismoTransito(
            "25286000", "STRIA TTOyTTE MCPAL FUNZA", MandatoPreviewSample.PhCiudadOrganismo);
        var firmante = new MandatarioFirmante("Ana Gestora", "1020304050");

        var data = MandatoPreviewSample.Build(
            MandatoTemplateResolver.Municipio, esJuridica: false, organismo, firmante);

        data.Tramite.Organismo.Codigo.Should().Be("25286000");
        data.Mandatario!.Nombre.Should().Be("Ana Gestora");
        data.Mandatario.Documento.Should().Be("1020304050");

        // AC8 — lo que no se simula sigue saliendo como marcador, nunca como dato inventado.
        data.Tramite.Vehiculo.Placa.Should().Be(MandatoPreviewSample.PhPlaca);
    }

    [Fact]
    public void Build_PersonaNatural_GeneraPdfValido()
    {
        var data = MandatoPreviewSample.Build(MandatoTemplateResolver.Municipio, esJuridica: false);

        var doc = Generator.GenerateMandato(data);

        doc.Content.Should().NotBeEmpty();
        System.Text.Encoding.ASCII.GetString(doc.Content, 0, 4).Should().Be("%PDF");
    }

    [Fact]
    public void Build_Sabaneta_UsesInstitutionalFamily()
    {
        var data = MandatoPreviewSample.Build(MandatoTemplateResolver.Sabaneta);
        data.Familia.Should().Be(MandatoFamilia.OrganismoTransito);
        data.InstitutionalMandataryNit.Should().Be("900273813-7");
        data.ModoFirmaMandatario.Should().Be(MandatarioFirmaModo.SinBloque);
    }
}
