using Flit.Tramites.Domain.Documents;
using FluentAssertions;
using Xunit;

namespace Flit.Tramites.Domain.Tests.Documents;

public sealed class MandatoTramiteIdentityTests
{
    [Theory]
    [InlineData("TRASPASO_STANDARD", null, null, null)]
    [InlineData(null, "TRASPASO", null, null)]
    [InlineData(null, null, "traspaso_standard", null)]
    [InlineData(null, null, null, "traspaso")]
    [InlineData("traspaso_standard", null, null, null)]
    public void EsTraspaso_AceptaCatalogoFamiliaWizardYModalidad(
        string? code, string? family, string? tipologia, string? modalidad)
    {
        MandatoTramiteIdentity.EsTraspaso(code, family, tipologia, modalidad).Should().BeTrue();
    }

    [Theory]
    [InlineData("MATRICULA_NUEVA", "MATRICULAS", "matricula_inicial", "matricula_inicial")]
    [InlineData("CAMBIO_COLOR", "OTROS", null, null)]
    [InlineData("matricula_inicial", null, null, null)]
    public void EsTraspaso_NoConfundeMatriculaNiOtros(
        string? code, string? family, string? tipologia, string? modalidad)
    {
        MandatoTramiteIdentity.EsTraspaso(code, family, tipologia, modalidad).Should().BeFalse();
    }

    [Fact]
    public void NombreObjeto_UsaElNameDelCatalogoEnMayusculas()
    {
        MandatoTramiteIdentity.NombreObjeto("Traspaso", "TRASPASO_STANDARD", "TRASPASO", null, null)
            .Should().Be("TRASPASO");
        MandatoTramiteIdentity.NombreObjeto("Matrícula inicial", "MATRICULA_NUEVA", "MATRICULAS", null, null)
            .Should().Be("MATRÍCULA INICIAL");
    }

    [Fact]
    public void NombreObjeto_SinCatalogo_CaeALaRedaccionLegal()
    {
        MandatoTramiteIdentity.NombreObjeto(null, "TRASPASO_STANDARD", null, null, null)
            .Should().Be(MandatoTramiteIdentity.NombreTraspasoFallback);
        MandatoTramiteIdentity.NombreObjeto(null, "MATRICULA_NUEVA", null, null, null)
            .Should().Be(MandatoTramiteIdentity.NombreMatriculaFallback);
    }

    [Theory]
    [InlineData(null, null, "TRASPASO_STANDARD")]
    [InlineData("traspaso_standard", null, "TRASPASO_STANDARD")]
    [InlineData(null, "traspaso", "TRASPASO_STANDARD")]
    [InlineData("MATRICULA_NUEVA", null, "MATRICULA_NUEVA")]
    [InlineData(null, "matricula_inicial", "MATRICULA_NUEVA")]
    [InlineData("CAMBIO_COLOR", null, "CAMBIO_COLOR")]
    public void CanonicalCode_UnificaWizardYCatalogo(string? code, string? tipologia, string expected)
    {
        MandatoTramiteIdentity.CanonicalCode(code, tipologia).Should().Be(expected);
    }
}
