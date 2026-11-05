using Flit.Tramites.Application.Documents;
using Flit.Tramites.Domain.Documents;
using FluentAssertions;
using Xunit;

namespace Flit.Tramites.Application.Tests.Documents;

public sealed class MandatoFirmaModoResolverTests
{
    [Fact]
    public void ConEstampa_PintaEnModoSigner()
    {
        var firmante = new MandatarioFirmante("Ana", "1", FirmaImagen: [1, 2, 3]);
        MandatoFirmaModoResolver.TieneEstampa(firmante).Should().BeTrue();
        MandatoFirmaModoResolver.Resolve("signer", tieneEstampa: true)
            .Should().Be(MandatarioFirmaModo.Estampada);
    }

    [Fact]
    public void SinEstampa_Manual()
    {
        MandatoFirmaModoResolver.Resolve("signer", tieneEstampa: false)
            .Should().Be(MandatarioFirmaModo.Manual);
    }

    [Fact]
    public void Institucional_SinBloque()
    {
        MandatoFirmaModoResolver.Resolve("institutional", tieneEstampa: true)
            .Should().Be(MandatarioFirmaModo.SinBloque);
    }

    [Fact]
    public void Institucional_SinBloque_TambienSinEstampa()
    {
        MandatoFirmaModoResolver.Resolve("institutional", tieneEstampa: false)
            .Should().Be(MandatarioFirmaModo.SinBloque);
    }

    [Theory]
    [InlineData(null, true, MandatarioFirmaModo.Estampada)]
    [InlineData(null, false, MandatarioFirmaModo.Manual)]
    [InlineData("tipo-que-no-existe", true, MandatarioFirmaModo.Estampada)]
    [InlineData("tipo-que-no-existe", false, MandatarioFirmaModo.Manual)]
    public void TipoAusenteODesconocido_SeComportaComoSigner(
        string? tipo, bool tieneEstampa, MandatarioFirmaModo esperado)
    {
        MandatoFirmaModoResolver.Resolve(tipo, tieneEstampa).Should().Be(esperado);
    }

    [Fact]
    public void Abierto_ManualAunqueHayaEstampa()
    {
        MandatoFirmaModoResolver.Resolve("open", tieneEstampa: true)
            .Should().Be(MandatarioFirmaModo.Manual);
    }

    [Fact]
    public void SelloDeIdentidad_CuentaComoEstampa()
    {
        var firmante = new MandatarioFirmante("Ana", "1", SelloIdentidad: "Validación de identidad\nFirma x");
        MandatoFirmaModoResolver.TieneEstampa(firmante).Should().BeTrue();
    }
}
