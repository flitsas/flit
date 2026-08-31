using Flit.Tramites.Application.Documents;
using Flit.Tramites.Domain.Documents;
using FluentAssertions;
using Xunit;

namespace Flit.Tramites.Application.Tests.Documents;

public sealed class MandatoFirmaModoResolverTests
{
    [Fact]
    public void ConEstampa_PintaAunqueHayaConvenioFalsoYModoSigner()
    {
        var firmante = new MandatarioFirmante("Ana", "1", FirmaImagen: [1, 2, 3]);
        MandatoFirmaModoResolver.TieneEstampa(firmante).Should().BeTrue();
        MandatoFirmaModoResolver.Resolve("signer", tieneConvenio: false, tieneEstampa: true)
            .Should().Be(MandatarioFirmaModo.Estampada);
    }

    [Fact]
    public void SinEstampa_Manual()
    {
        MandatoFirmaModoResolver.Resolve("signer", tieneConvenio: false, tieneEstampa: false)
            .Should().Be(MandatarioFirmaModo.Manual);
    }

    [Fact]
    public void Convenio_SinBloqueAunqueHayaEstampa()
    {
        MandatoFirmaModoResolver.Resolve("signer", tieneConvenio: true, tieneEstampa: true)
            .Should().Be(MandatarioFirmaModo.SinBloque);
    }

    [Fact]
    public void Institucional_SinBloque()
    {
        MandatoFirmaModoResolver.Resolve("institutional", tieneConvenio: false, tieneEstampa: true)
            .Should().Be(MandatarioFirmaModo.SinBloque);
    }

    [Fact]
    public void Abierto_ManualAunqueHayaEstampa()
    {
        MandatoFirmaModoResolver.Resolve("open", tieneConvenio: false, tieneEstampa: true)
            .Should().Be(MandatarioFirmaModo.Manual);
    }

    [Fact]
    public void SelloDeIdentidad_CuentaComoEstampa()
    {
        var firmante = new MandatarioFirmante("Ana", "1", SelloIdentidad: "Validación de identidad\nFirma x");
        MandatoFirmaModoResolver.TieneEstampa(firmante).Should().BeTrue();
    }
}
