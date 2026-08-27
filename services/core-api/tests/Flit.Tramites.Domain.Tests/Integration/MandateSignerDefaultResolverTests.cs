using Flit.Tramites.Domain.Integration;
using FluentAssertions;
using Xunit;

namespace Flit.Tramites.Domain.Tests.Integration;

public sealed class MandateSignerDefaultResolverTests
{
    private static readonly Guid Ana = Guid.Parse("cccccccc-1111-4000-8000-000000000001");
    private static readonly Guid Carlos = Guid.Parse("cccccccc-1111-4000-8000-000000000002");
    private static readonly Guid OtGlobal = Guid.Parse("cccccccc-1111-4000-8000-000000000099");

    [Fact]
    public void EleccionExplicita_Gana_AunConDefaultDistinto()
    {
        var resultado = MandateSignerDefaultResolver.Resolve(
            candidateIds: [Ana, Carlos], explicitOrSavedSignerId: Ana, defaultSignerId: Carlos);

        resultado.Should().Be(Ana);
    }

    [Fact]
    public void DefaultOt_GanaAlDeCompania_AunqueNoEsteEnCandidatos()
    {
        var resultado = MandateSignerDefaultResolver.Resolve(
            [Ana, Carlos],
            explicitOrSavedSignerId: null,
            otDefaultSignerId: OtGlobal,
            companyDefaultSignerId: Carlos);

        resultado.Should().Be(OtGlobal);
    }

    [Fact]
    public void SinDefaultOt_UsaDefaultDeCompaniaSiEstaEntreCandidatos()
    {
        var resultado = MandateSignerDefaultResolver.Resolve(
            [Ana, Carlos],
            explicitOrSavedSignerId: null,
            otDefaultSignerId: null,
            companyDefaultSignerId: Carlos);

        resultado.Should().Be(Carlos);
    }

    [Fact]
    public void SinDefaultOt_DefaultDeCompaniaFueraDeCandidatos_Vacio()
    {
        var resultado = MandateSignerDefaultResolver.Resolve(
            [Ana],
            explicitOrSavedSignerId: null,
            otDefaultSignerId: null,
            companyDefaultSignerId: Carlos);

        resultado.Should().BeNull();
    }

    [Fact]
    public void SinEleccionNiDefaults_UnicoCandidato_YaNoSeAutoelege()
    {
        var resultado = MandateSignerDefaultResolver.Resolve(
            candidateIds: [Ana], explicitOrSavedSignerId: null, defaultSignerId: null);

        resultado.Should().BeNull();
    }

    [Fact]
    public void SinEleccionNiDefault_VariosCandidatos_SinSugerencia()
    {
        var resultado = MandateSignerDefaultResolver.Resolve(
            candidateIds: [Ana, Carlos], explicitOrSavedSignerId: null, defaultSignerId: null);

        resultado.Should().BeNull();
    }

    [Fact]
    public void EleccionExplicitaVacia_SeTrataComoAusente()
    {
        var resultado = MandateSignerDefaultResolver.Resolve(
            candidateIds: [Ana], explicitOrSavedSignerId: Guid.Empty, defaultSignerId: null);

        resultado.Should().BeNull();
    }
}
