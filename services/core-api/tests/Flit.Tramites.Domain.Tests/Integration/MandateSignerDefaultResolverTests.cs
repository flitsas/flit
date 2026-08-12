using Flit.Tramites.Domain.Integration;
using FluentAssertions;
using Xunit;

namespace Flit.Tramites.Domain.Tests.Integration;

/// <summary>
/// Bug reportado en DEV — pantalla y documento resolvían el mandatario con criterios distintos: el
/// listado de pantalla aplicaba la cascada completa (elegido → default OT → único candidato), la
/// generación del documento usaba <c>instance.MandateSignerId</c> crudo sin cascada, y el gate de
/// aprobación resolvía por cotejo de usuario. <see cref="MandateSignerDefaultResolver"/> es el resolvedor
/// ÚNICO que ahora usan los tres.
/// </summary>
public sealed class MandateSignerDefaultResolverTests
{
    private static readonly Guid Ana = Guid.Parse("cccccccc-1111-4000-8000-000000000001");
    private static readonly Guid Carlos = Guid.Parse("cccccccc-1111-4000-8000-000000000002");

    [Fact]
    public void EleccionExplicita_Gana_AunConDefaultDistinto()
    {
        var resultado = MandateSignerDefaultResolver.Resolve(
            candidateIds: [Ana, Carlos], explicitOrSavedSignerId: Ana, defaultSignerId: Carlos);

        resultado.Should().Be(Ana);
    }

    [Fact]
    public void SinEleccion_DefaultDelOt_SePreseleccionaSiEstaEntreCandidatos()
    {
        var resultado = MandateSignerDefaultResolver.Resolve(
            candidateIds: [Ana, Carlos], explicitOrSavedSignerId: null, defaultSignerId: Carlos);

        resultado.Should().Be(Carlos);
    }

    [Fact]
    public void SinEleccion_DefaultQueYaNoEstaEntreCandidatos_NoSeImpone()
    {
        var fueraDeCandidatos = Guid.NewGuid();

        var resultado = MandateSignerDefaultResolver.Resolve(
            candidateIds: [Ana, Carlos], explicitOrSavedSignerId: null, defaultSignerId: fueraDeCandidatos);

        // El único candidato entra por la regla siguiente; con dos candidatos y default inválido, null.
        resultado.Should().BeNull();
    }

    [Fact]
    public void SinEleccionNiDefault_UnicoCandidato_SeResuelveSolo()
    {
        var resultado = MandateSignerDefaultResolver.Resolve(
            candidateIds: [Ana], explicitOrSavedSignerId: null, defaultSignerId: null);

        resultado.Should().Be(Ana);
    }

    [Fact]
    public void SinEleccionNiDefault_VariosCandidatos_SinSugerencia()
    {
        // Documentado: con varios candidatos y sin default parametrizado, este resolvedor no arriesga una
        // sugerencia (null). El llamador decide: la pantalla no preselecciona nada, y el gate de
        // aprobación cae al cotejo por usuario de MandateSignerSelector antes de exigir selección.
        var resultado = MandateSignerDefaultResolver.Resolve(
            candidateIds: [Ana, Carlos], explicitOrSavedSignerId: null, defaultSignerId: null);

        resultado.Should().BeNull();
    }

    [Fact]
    public void SinCandidatos_SinEleccion_SinDefault_Null()
    {
        var resultado = MandateSignerDefaultResolver.Resolve(
            candidateIds: [], explicitOrSavedSignerId: null, defaultSignerId: null);

        resultado.Should().BeNull();
    }

    [Fact]
    public void EleccionExplicitaVacia_SeTrataComoAusente()
    {
        var resultado = MandateSignerDefaultResolver.Resolve(
            candidateIds: [Ana], explicitOrSavedSignerId: Guid.Empty, defaultSignerId: null);

        resultado.Should().Be(Ana);
    }
}
