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

    private ProcedureInstance SeedInstance(bool hasMandato = true, Guid? transitOffice = null, string? mandatoSource = null)
    {
        var instance = new ProcedureInstance
        {
            ProcedureType = ProcedureTypeFixture.Matricula,
            Id = Guid.NewGuid(),
            TenantId = Tenant,
            TransitOfficeId = transitOffice ?? Office,
            Attachments = hasMandato
                ? [new ProcedureInstanceAttachment { Tipo = "mandato", Source = mandatoSource ?? "user" }]
                : [],
        };
        _repo.GetByIdWithFurGraphAsync(instance.Id, Tenant, Arg.Any<CancellationToken>()).Returns(instance);
        return instance;
    }

    private void Candidates(params MandateSignerCandidate[] candidates) =>
        _directory.GetCandidatesAsync(Office, Tenant, Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(candidates);

    private static MandateSignerCandidate Signer(Guid? userId = null, Guid? id = null, bool vigente = true) =>
        new(id ?? Guid.NewGuid(), "Firmante", "123", userId, vigente);

    [Fact]
    public async Task NoMandatoAttachment_IsNotApplicable_AndSkipsDirectory()
    {
        var instance = SeedInstance(hasMandato: false);

        var decision = await Handler().CheckAsync(instance.Id, Tenant, Guid.NewGuid(), null, TestContext.Current.CancellationToken);

        decision.Outcome.Should().Be(MandatoApprovalOutcome.NotApplicable);
        await _directory.DidNotReceive().GetCandidatesAsync(
            Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
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
    public async Task SingleCandidate_WithoutVigentIdentity_RequiresValidation()
    {
        var instance = SeedInstance();
        Candidates(Signer(vigente: false));

        var decision = await Handler().CheckAsync(
            instance.Id, Tenant, Guid.NewGuid(), null, TestContext.Current.CancellationToken);

        // HU #10911/#10916 — el mandatario debe validar identidad (vigente) antes de firmar.
        decision.Outcome.Should().Be(MandatoApprovalOutcome.IdentidadRequerida);
        decision.MandateSignerId.Should().BeNull();
    }

    [Fact]
    public async Task ExplicitSelection_WithoutVigentIdentity_RequiresValidation()
    {
        var instance = SeedInstance();
        var chosen = Signer(vigente: false);
        Candidates(Signer(), chosen);

        var decision = await Handler().CheckAsync(
            instance.Id, Tenant, null, chosen.Id, TestContext.Current.CancellationToken);

        decision.Outcome.Should().Be(MandatoApprovalOutcome.IdentidadRequerida);
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

    // ---- HU #11317 (Feature #11309, ADR-0042 §supersede parcial) — el gate no exige mandatario cuando
    // el único adjunto de mandato es un documento personalizado (Source="company") ---------------------

    [Fact]
    public async Task MandatoOrigenCompany_IsNotApplicable_AndSkipsDirectory()
    {
        // El PDF de la compañía es estático (sin bloques de firma del mandatario): su sola presencia no
        // debe exigir un firmante que el documento no usa.
        var instance = SeedInstance(mandatoSource: "company");

        var decision = await Handler().CheckAsync(
            instance.Id, Tenant, Guid.NewGuid(), null, TestContext.Current.CancellationToken);

        decision.Outcome.Should().Be(MandatoApprovalOutcome.NotApplicable);
        decision.MandateSignerId.Should().BeNull();
        await _directory.DidNotReceive().GetCandidatesAsync(
            Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task MandatoOrigenSystem_SigueExigiendoMandatarioComoAntes()
    {
        // Contraparte explícita: cuando el adjunto de mandato es del SISTEMA, el gate se comporta
        // exactamente igual que antes de la HU #11317 (varios candidatos sin cotejo ⇒ 409).
        var instance = SeedInstance(mandatoSource: "system");
        Candidates(Signer(userId: Guid.NewGuid()), Signer(userId: Guid.NewGuid()));

        var decision = await Handler().CheckAsync(
            instance.Id, Tenant, Guid.NewGuid(), null, TestContext.Current.CancellationToken);

        decision.Outcome.Should().Be(MandatoApprovalOutcome.RequiereSeleccion);
    }

    [Fact]
    public async Task MandatoOrigenCompanyYSystemAmbos_ExigeMandatarioPorElDelSistema()
    {
        // Caso límite (no debería darse por la idempotencia de FurCommand, pero el gate debe ser robusto
        // igual): si CUALQUIER adjunto de mandato no es "company", sigue exigiendo mandatario.
        var instance = new ProcedureInstance
        {
            ProcedureType = ProcedureTypeFixture.Matricula,
            Id = Guid.NewGuid(),
            TenantId = Tenant,
            TransitOfficeId = Office,
            Attachments =
            [
                new ProcedureInstanceAttachment { Tipo = "mandato", Source = "company" },
                new ProcedureInstanceAttachment { Tipo = "mandato", Source = "system" },
            ],
        };
        _repo.GetByIdWithFurGraphAsync(instance.Id, Tenant, Arg.Any<CancellationToken>()).Returns(instance);
        Candidates(Signer());

        var decision = await Handler().CheckAsync(
            instance.Id, Tenant, Guid.NewGuid(), null, TestContext.Current.CancellationToken);

        decision.Outcome.Should().Be(MandatoApprovalOutcome.Resolved);
    }

    // ---- develop (PR #241) — modo de asignación abierta del mandato --------------------------------

    [Fact]
    public async Task OpenAssignmentMode_SkipsSignerEvenWithCandidates()
    {
        var instance = SeedInstance();
        instance.FieldValues =
        [
            new ProcedureInstanceFieldValue { FieldKey = "transit_office_code", ValueText = "05001000" },
        ];
        Candidates(Signer(), Signer(userId: Guid.NewGuid()));

        var policy = Substitute.For<IMandateRequirementPolicy>();
        policy.ResolveAsync("05001000", Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .Returns(new MandateOtConfig(
                Office,
                "generico",
                RequiresForNaturalPerson: true,
                null,
                null,
                AssignmentMode: "open"));

        var handler = new MandatoApprovalHandler(_repo, _directory, mandatePolicy: policy);
        var decision = await handler.CheckAsync(
            instance.Id, Tenant, Guid.NewGuid(), null, TestContext.Current.CancellationToken);

        decision.Outcome.Should().Be(MandatoApprovalOutcome.NotApplicable);
        await _directory.DidNotReceive().GetCandidatesAsync(
            Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }
}
