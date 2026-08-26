using Flit.Tramites.Domain.Documents;
using FluentAssertions;
using Xunit;

namespace Flit.Tramites.Domain.Tests.Documents;

/// <summary>
/// HU #10915 (ADR-0036) — el resolver de plantilla de mandato es una función pura y cerrada: mapea el
/// <c>template_code</c> del OT a su variante y cae a <see cref="MandatoVariante.Generico"/> ante cualquier
/// valor desconocido, nulo o vacío (default seguro cuando el OT no tiene configuración).
/// </summary>
public sealed class MandatoTemplateResolverTests
{
    [Theory]
    [InlineData("sabaneta", MandatoVariante.Sabaneta)]
    [InlineData("SABANETA", MandatoVariante.Sabaneta)]
    [InlineData("  bello  ", MandatoVariante.Bello)]
    [InlineData("municipio", MandatoVariante.Municipio)]
    [InlineData("generico", MandatoVariante.Generico)]
    public void Resolve_MapsKnownCodes(string code, MandatoVariante expected) =>
        MandatoTemplateResolver.Resolve(code).Should().Be(expected);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("desconocido")]
    public void Resolve_FallsBackToGenerico(string? code) =>
        MandatoTemplateResolver.Resolve(code).Should().Be(MandatoVariante.Generico);

    [Fact]
    public void ResolveEmissionCode_Open_IsGenerico() =>
        MandatoTemplateResolver.ResolveEmissionCode("open", mandanteEsJuridica: false, "11001000")
            .Should().Be(MandatoTemplateResolver.Generico);

    [Fact]
    public void ResolveEmissionCode_PersonaJuridica_IsSabaneta_EvenForBogota() =>
        MandatoTemplateResolver.ResolveEmissionCode("institutional", mandanteEsJuridica: true, "11001000")
            .Should().Be(MandatoTemplateResolver.Sabaneta);

    [Fact]
    public void ResolveEmissionCode_EnvigadoJuridica_SameAsSabaneta() =>
        MandatoTemplateResolver.ResolveEmissionCode("institutional", mandanteEsJuridica: true, "5266000")
            .Should().Be(MandatoTemplateResolver.Sabaneta);

    [Fact]
    public void ResolveEmissionCode_BogotaPersonaNatural_IsGenerico_NotBello() =>
        MandatoTemplateResolver.ResolveEmissionCode("signer", mandanteEsJuridica: false, "11001000")
            .Should().Be(MandatoTemplateResolver.Generico);

    [Fact]
    public void ResolveEmissionCode_BelloPersonaNatural_IsBello() =>
        MandatoTemplateResolver.ResolveEmissionCode("signer", mandanteEsJuridica: false, "5088000")
            .Should().Be(MandatoTemplateResolver.Bello);

    [Fact]
    public void ResolveEmissionCode_CompradorJuridicoEnSigner_IsSabaneta() =>
        MandatoTemplateResolver.ResolveEmissionCode("signer", mandanteEsJuridica: true, "11001000")
            .Should().Be(MandatoTemplateResolver.Sabaneta);

    [Fact]
    public void ResolveEmissionCode_EnvigadoPersonaNatural_IsGenerico() =>
        MandatoTemplateResolver.ResolveEmissionCode("signer", mandanteEsJuridica: false, "5266000")
            .Should().Be(MandatoTemplateResolver.Generico);

    [Fact]
    public void ResolveEmissionCode_FunzaPersonaNatural_IsMunicipio() =>
        MandatoTemplateResolver.ResolveEmissionCode("signer", mandanteEsJuridica: false, "25286000")
            .Should().Be(MandatoTemplateResolver.Municipio);
}
