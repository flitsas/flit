using Flit.Tramites.Application.UseCases.ProcedureInstances.Estados;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Integration;
using Flit.Tramites.Domain.Repositories;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Tramites.Application.Tests.UseCases.ProcedureInstances;

/// <summary>
/// HU #10916 (ADR-0036 §D9) — resolución del mandatario al aprobar (ruta OT, que no pasa por el
/// lifecycle): el mandato aplica sii ya existe su adjunto; uno solo → auto; varios sin cotejo →
/// requiere selección (409); sin candidatos (institucional) → aprobar sin firmante.
/// </summary>
public sealed class MandatoApprovalHandlerTests
{
    private readonly IProcedureInstanceRepository _repo = Substitute.For<IProcedureInstanceRepository>();
    private readonly IMandateSignerDirectory _directory = Substitute.For<IMandateSignerDirectory>();

    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly Guid Office = Guid.NewGuid();

    private MandatoApprovalHandler Handler() => new(_repo, _directory);

    private ProcedureInstance SeedInstance(bool hasMandato = true, Guid? transitOffice = null)
    {
        var instance = new ProcedureInstance
        {
            Id = Guid.NewGuid(),
            TenantId = Tenant,
            TransitOfficeId = transitOffice ?? Office,
            Attachments = hasMandato ? [new ProcedureInstanceAttachment { Tipo = "mandato" }] : [],
        };
        _repo.GetByIdWithFurGraphAsync(instance.Id, Tenant, Arg.Any<CancellationToken>()).Returns(instance);
        return instance;
    }

    private void Candidates(params MandateSignerCandidate[] candidates) =>
        _directory.GetCandidatesAsync(Office, Tenant, Arg.Any<CancellationToken>())
            .Returns(candidates);

    private static MandateSignerCandidate Signer(Guid? userId = null, Guid? id = null) =>
        new(id ?? Guid.NewGuid(), "Firmante", "123", userId);

    [Fact]
    public async Task NoMandatoAttachment_IsNotApplicable_AndSkipsDirectory()
    {
        var instance = SeedInstance(hasMandato: false);

        var decision = await Handler().CheckAsync(instance.Id, Tenant, Guid.NewGuid(), null, TestContext.Current.CancellationToken);

        decision.Outcome.Should().Be(MandatoApprovalOutcome.NotApplicable);
        await _directory.DidNotReceive().GetCandidatesAsync(
            Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SingleCandidate_AutoResolves()
    {
        var instance = SeedInstance();
        var only = Signer();
        Candidates(only);

        var decision = await Handler().CheckAsync(instance.Id, Tenant, Guid.NewGuid(), null, TestContext.Current.CancellationToken);

        decision.Outcome.Should().Be(MandatoApprovalOutcome.Resolved);
        decision.MandateSignerId.Should().Be(only.Id);
    }

    [Fact]
    public async Task MultipleCandidates_NoUserMatch_RequiresSelection()
    {
        var instance = SeedInstance();
        Candidates(Signer(userId: Guid.NewGuid()), Signer(userId: Guid.NewGuid()));

        var decision = await Handler().CheckAsync(instance.Id, Tenant, Guid.NewGuid(), null, TestContext.Current.CancellationToken);

        decision.Outcome.Should().Be(MandatoApprovalOutcome.RequiereSeleccion);
        decision.MandateSignerId.Should().BeNull();
    }

    [Fact]
    public async Task MultipleCandidates_UniqueUserMatch_AutoResolves()
    {
        var instance = SeedInstance();
        var user = Guid.NewGuid();
        var match = Signer(userId: user);
        Candidates(Signer(userId: Guid.NewGuid()), match);

        var decision = await Handler().CheckAsync(instance.Id, Tenant, user, null, TestContext.Current.CancellationToken);

        decision.Outcome.Should().Be(MandatoApprovalOutcome.Resolved);
        decision.MandateSignerId.Should().Be(match.Id);
    }

    [Fact]
    public async Task MultipleCandidates_ExplicitSelection_Resolves()
    {
        var instance = SeedInstance();
        var chosen = Signer();
        Candidates(Signer(), chosen);

        var decision = await Handler().CheckAsync(instance.Id, Tenant, null, chosen.Id, TestContext.Current.CancellationToken);

        decision.Outcome.Should().Be(MandatoApprovalOutcome.Resolved);
        decision.MandateSignerId.Should().Be(chosen.Id);
    }

    [Fact]
    public async Task NoCandidates_Institutional_IsNotApplicable()
    {
        var instance = SeedInstance();
        Candidates();

        var decision = await Handler().CheckAsync(instance.Id, Tenant, Guid.NewGuid(), null, TestContext.Current.CancellationToken);

        // Sabaneta (mandatario institucional, sin firmante persona): aprobar sin firmante, sin 409.
        decision.Outcome.Should().Be(MandatoApprovalOutcome.NotApplicable);
    }
}
