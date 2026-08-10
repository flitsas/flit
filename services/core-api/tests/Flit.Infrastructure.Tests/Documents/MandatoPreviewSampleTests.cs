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
    public void Build_Sabaneta_UsesInstitutionalFamily()
    {
        var data = MandatoPreviewSample.Build(MandatoTemplateResolver.Sabaneta);
        data.Familia.Should().Be(MandatoFamilia.OrganismoTransito);
        data.InstitutionalMandataryNit.Should().Be("900273813-7");
        data.ModoFirmaMandatario.Should().Be(MandatarioFirmaModo.SinBloque);
    }
}
