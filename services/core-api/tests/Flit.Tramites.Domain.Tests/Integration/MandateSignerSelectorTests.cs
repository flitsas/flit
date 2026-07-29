using Flit.Tramites.Domain.Integration;
using FluentAssertions;
using Xunit;

namespace Flit.Tramites.Domain.Tests.Integration;

/// <summary>
/// HU #10916 (ADR-0036 §D9) — regla pura de resolución del firmante del mandato al aprobar: uno solo →
/// auto; varios con cotejo único por usuario → auto; varios sin match → requiere selección; selección
/// explícita válida → auto; explícita fuera del conjunto → requiere selección; cero → no configurado.
/// </summary>
public sealed class MandateSignerSelectorTests
{
    private static MandateSignerCandidate Signer(Guid? userId = null, Guid? id = null) =>
        new(id ?? Guid.NewGuid(), "Firmante", "123", userId);

    [Fact]
    public void SingleCandidate_AutoResolves()
    {
        var only = Signer();
        var r = MandateSignerSelector.Resolve([only], approvingUserId: Guid.NewGuid(), explicitSignerId: null);

        r.Status.Should().Be(MandateSignerResolutionStatus.Resolved);
        r.Signer.Should().Be(only);
    }

    [Fact]
    public void NoCandidates_IsNoConfigurado()
    {
        var r = MandateSignerSelector.Resolve([], approvingUserId: Guid.NewGuid(), explicitSignerId: null);

        r.Status.Should().Be(MandateSignerResolutionStatus.NoConfigurado);
        r.Signer.Should().BeNull();
    }

    [Fact]
    public void MultipleCandidates_UniqueUserMatch_AutoResolves()
    {
        var user = Guid.NewGuid();
        var match = Signer(userId: user);
        var other = Signer(userId: Guid.NewGuid());

        var r = MandateSignerSelector.Resolve([other, match], approvingUserId: user, explicitSignerId: null);

        r.Status.Should().Be(MandateSignerResolutionStatus.Resolved);
        r.Signer.Should().Be(match);
    }

    [Fact]
    public void MultipleCandidates_NoUserMatch_RequiresSelection()
    {
        var r = MandateSignerSelector.Resolve(
            [Signer(userId: Guid.NewGuid()), Signer(userId: Guid.NewGuid())],
            approvingUserId: Guid.NewGuid(),
            explicitSignerId: null);

        r.Status.Should().Be(MandateSignerResolutionStatus.RequiereSeleccion);
        r.Signer.Should().BeNull();
    }

    [Fact]
    public void MultipleCandidates_AmbiguousUserMatch_RequiresSelection()
    {
        // Dos mandatarios con el mismo user_id (dato inconsistente): no auto-firma, exige elegir.
        var user = Guid.NewGuid();
        var r = MandateSignerSelector.Resolve(
            [Signer(userId: user), Signer(userId: user)],
            approvingUserId: user,
            explicitSignerId: null);

        r.Status.Should().Be(MandateSignerResolutionStatus.RequiereSeleccion);
    }

    [Fact]
    public void ExplicitSelection_WithinCandidates_Resolves()
    {
        var chosen = Signer();
        var r = MandateSignerSelector.Resolve(
            [Signer(), chosen, Signer()], approvingUserId: null, explicitSignerId: chosen.Id);

        r.Status.Should().Be(MandateSignerResolutionStatus.Resolved);
        r.Signer.Should().Be(chosen);
    }

    [Fact]
    public void ExplicitSelection_OutsideCandidates_RequiresSelection()
    {
        var r = MandateSignerSelector.Resolve(
            [Signer(), Signer()], approvingUserId: null, explicitSignerId: Guid.NewGuid());

        r.Status.Should().Be(MandateSignerResolutionStatus.RequiereSeleccion);
    }
}
