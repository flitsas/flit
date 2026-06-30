using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Enums;
using Flit.Tramites.Domain.Integration;
using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.Tramites.Catalog;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Tramites.Application.Tests.UseCases.ProcedureInstances;

/// <summary>HU #10349 — AC1 (finalizar borrador sin identidad/FUR), AC2 (submit sin cambio) y AC3 (biométrica en draft finalizado).</summary>
public sealed class FinalizeDraftProcedureInstanceTests
{
    private readonly IProcedureInstanceRepository _repo = Substitute.For<IProcedureInstanceRepository>();
    private readonly FinalizeDraftProcedureInstanceHandler _sut;

    public FinalizeDraftProcedureInstanceTests() =>
        _sut = new FinalizeDraftProcedureInstanceHandler(_repo);

    private static ProcedureInstance Instance(Guid id, Guid tenant, string status = ProcedureInstanceStatus.Draft) =>
        new()
        {
            Id = id,
            TenantId = tenant,
            ProcedureTypeId = Guid.NewGuid(),
            ReferenceNumber = "TRM-2026-000001",
            Status = status,
            ModalidadEntrada = "matricula_inicial",
            TipologiaCodigo = TramiteTipologiaCatalog.CodigoMatriculaInicial,
            CreatedAt = DateTimeOffset.UtcNow,
        };

    private static ProcedureInstanceActor Actor(Guid tenant, Guid id, string parte) =>
        new()
        {
            Id = Guid.NewGuid(),
            TenantId = tenant,
            ProcedureInstanceId = id,
            ProcedureEntityId = Guid.NewGuid(),
            ActorType = parte,
            DocumentType = "CC",
            DocumentNumber = "1020304050",
            FullName = "Ana Compradora",
            Email = "ana@x.com",
            Metadata = "{}",
            CreatedAt = DateTimeOffset.UtcNow,
        };

    /// <summary>Matrícula con datos completos para FINALIZAR: actor comprador + docs obligatorios + organismo.
    /// Deliberadamente SIN biométrica ni FUR (AC1: finalize no los exige).</summary>
    private static ProcedureInstance FinalizeReady(Guid id, Guid tenant)
    {
        var i = Instance(id, tenant);
        i.Actors.Add(Actor(tenant, id, "comprador"));
        foreach (var t in new[] { "factura", "aduana", "impronta" })
        {
            i.Attachments.Add(new ProcedureInstanceAttachment
            {
                Id = Guid.NewGuid(),
                TenantId = tenant,
                ProcedureInstanceId = id,
                Tipo = t,
                StoragePath = $"p/{t}",
                UploadedAt = DateTimeOffset.UtcNow,
            });
        }
        i.FieldValues.Add(new ProcedureInstanceFieldValue
        {
            Id = Guid.NewGuid(),
            TenantId = tenant,
            ProcedureInstanceId = id,
            FieldKey = "transit_office_code",
            ValueText = "11001000",
            Source = "user",
        });
        return i;
    }

    // ── AC1 ──────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Finalize_CompleteDraft_WithoutIdentityOrFur_SealsDraftFinalizedAt_StaysDraft()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenant = Guid.NewGuid();
        var instance = FinalizeReady(id, tenant);
        // Precondición del AC1: no hay biométrica aprobada ni adjunto FUR y aun así debe finalizar.
        instance.BiometricValidations.Should().BeEmpty();
        instance.Attachments.Should().NotContain(a => a.Tipo == "fur");
        _repo.GetByIdWithWizardGraphAsync(id, tenant, ct).Returns(instance);

        var (result, error) = await _sut.HandleAsync(id, tenant, ct);

        error.Should().BeNull();
        result.Should().NotBeNull();
        result!.Status.Should().Be(ProcedureInstanceStatus.Draft);
        result.DraftFinalizedAt.Should().NotBeNull();
        instance.Status.Should().Be(ProcedureInstanceStatus.Draft);
        instance.DraftFinalizedAt.Should().NotBeNull();
        // Bitácora draft_finalizado + persistencia.
        _repo.Received(1).Add(Arg.Is<ProcedureInstanceEvent>(e => e.Tipo == "draft_finalizado"));
        await _repo.Received(1).SaveChangesAsync(ct);
    }

    [Fact]
    public async Task Finalize_NotFound_ReturnsNotFound()
    {
        var ct = TestContext.Current.CancellationToken;
        _repo.GetByIdWithWizardGraphAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), ct).Returns((ProcedureInstance?)null);

        var (_, error) = await _sut.HandleAsync(Guid.NewGuid(), Guid.NewGuid(), ct);

        error.Should().Be("not_found");
    }

    [Fact]
    public async Task Finalize_NotDraft_ReturnsNotDraft()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenant = Guid.NewGuid();
        _repo.GetByIdWithWizardGraphAsync(id, tenant, ct)
            .Returns(Instance(id, tenant, ProcedureInstanceStatus.Submitted));

        var (_, error) = await _sut.HandleAsync(id, tenant, ct);

        error.Should().Be("not_draft");
    }

    [Fact]
    public async Task Finalize_MissingActors_ReturnsActoresIncompletos()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenant = Guid.NewGuid();
        var instance = FinalizeReady(id, tenant);
        instance.Actors.Clear();
        _repo.GetByIdWithWizardGraphAsync(id, tenant, ct).Returns(instance);

        var (_, error) = await _sut.HandleAsync(id, tenant, ct);

        error.Should().Be("actores_incompletos");
        instance.DraftFinalizedAt.Should().BeNull();
    }

    [Fact]
    public async Task Finalize_MissingDocs_ReturnsDocumentosIncompletos()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenant = Guid.NewGuid();
        var instance = FinalizeReady(id, tenant);
        instance.Attachments.Clear();
        _repo.GetByIdWithWizardGraphAsync(id, tenant, ct).Returns(instance);

        var (_, error) = await _sut.HandleAsync(id, tenant, ct);

        error.Should().Be("documentos_incompletos");
    }

    [Fact]
    public async Task Finalize_MissingOrganismo_ReturnsOrganismoRequerido()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenant = Guid.NewGuid();
        var instance = FinalizeReady(id, tenant);
        instance.FieldValues.Clear();
        _repo.GetByIdWithWizardGraphAsync(id, tenant, ct).Returns(instance);

        var (_, error) = await _sut.HandleAsync(id, tenant, ct);

        error.Should().Be("organismo_requerido");
    }

    [Fact]
    public async Task Finalize_AlreadyFinalized_IsIdempotent()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenant = Guid.NewGuid();
        var instance = FinalizeReady(id, tenant);
        var sealedAt = DateTimeOffset.UtcNow.AddMinutes(-30);
        instance.DraftFinalizedAt = sealedAt;
        _repo.GetByIdWithWizardGraphAsync(id, tenant, ct).Returns(instance);

        var (result, error) = await _sut.HandleAsync(id, tenant, ct);

        error.Should().BeNull();
        result!.DraftFinalizedAt.Should().Be(sealedAt); // no se re-sella
        _repo.DidNotReceive().Add(Arg.Any<ProcedureInstanceEvent>());
        await _repo.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    // ── AC2 — submit conserva la regla estricta sobre un borrador finalizado ──────

    [Fact]
    public async Task Submit_AfterFinalize_BiometricPending_StillBlocksIdentidad()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenant = Guid.NewGuid();
        // Borrador finalizado con docs+fur+organismo pero SIN biométrica aprobada.
        var instance = FinalizeReady(id, tenant);
        instance.DraftFinalizedAt = DateTimeOffset.UtcNow;
        instance.Attachments.Add(new ProcedureInstanceAttachment
        {
            Id = Guid.NewGuid(), TenantId = tenant, ProcedureInstanceId = id, Tipo = "fur",
            StoragePath = "p/fur", UploadedAt = DateTimeOffset.UtcNow,
        });

        var typeRepo = Substitute.For<IProcedureTypeRepository>();
        typeRepo.GetByIdAsync(instance.ProcedureTypeId, ct).Returns(new ProcedureType
        {
            Id = instance.ProcedureTypeId, Code = "X", Name = "X", Family = "matriculas",
            PublicationStatus = PublicationStatus.Published, CreatedAt = DateTimeOffset.UtcNow,
        });
        _repo.GetByIdWithWizardGraphAsync(id, tenant, ct).Returns(instance);

        var submit = new SubmitProcedureInstanceHandler(
            _repo, typeRepo, NullProcedureStateChangeNotifier.Instance, NullOtRuleGate.Instance,
            NullTransitOfficeGrantGate.Instance);

        var (_, error) = await submit.HandleAsync(id, tenant, ct);

        error.Should().Be("identidad_requerida");
        instance.Status.Should().Be(ProcedureInstanceStatus.Draft);
    }

    [Fact]
    public async Task Submit_AfterFinalize_AllApproved_Radica()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenant = Guid.NewGuid();
        var instance = FinalizeReady(id, tenant);
        instance.DraftFinalizedAt = DateTimeOffset.UtcNow;
        instance.Attachments.Add(new ProcedureInstanceAttachment
        {
            Id = Guid.NewGuid(), TenantId = tenant, ProcedureInstanceId = id, Tipo = "fur",
            StoragePath = "p/fur", UploadedAt = DateTimeOffset.UtcNow,
        });
        instance.BiometricValidations.Add(new ProcedureInstanceBiometricValidation
        {
            Id = Guid.NewGuid(), TenantId = tenant, ProcedureInstanceId = id, PartyRole = "comprador",
            Status = BiometricEstados.Aprobado, Name = "Ana", DocumentType = "CC", DocumentNumber = "1020304050",
            Email = "ana@x.com", TokenHash = Guid.NewGuid().ToString("N"),
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(1), CreatedAt = DateTimeOffset.UtcNow,
        });

        var typeRepo = Substitute.For<IProcedureTypeRepository>();
        typeRepo.GetByIdAsync(instance.ProcedureTypeId, ct).Returns(new ProcedureType
        {
            Id = instance.ProcedureTypeId, Code = "X", Name = "X", Family = "matriculas",
            PublicationStatus = PublicationStatus.Published, CreatedAt = DateTimeOffset.UtcNow,
        });
        _repo.GetByIdWithWizardGraphAsync(id, tenant, ct).Returns(instance);

        var submit = new SubmitProcedureInstanceHandler(
            _repo, typeRepo, NullProcedureStateChangeNotifier.Instance, NullOtRuleGate.Instance,
            NullTransitOfficeGrantGate.Instance);

        var (result, error) = await submit.HandleAsync(id, tenant, ct);

        error.Should().BeNull();
        result!.Status.Should().Be(ProcedureInstanceStatus.Submitted);
    }

    // ── AC3 — biométrica funciona sobre un borrador finalizado ────────────────────

    [Fact]
    public async Task Biometric_OnFinalizedDraft_SimulatesApproved()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenant = Guid.NewGuid();
        var instance = FinalizeReady(id, tenant);
        instance.DraftFinalizedAt = DateTimeOffset.UtcNow; // borrador finalizado
        _repo.GetByIdWithBiometricsAndActorsAsync(id, tenant, ct).Returns(instance);

        var simular = new SimularBiometriaHandler(_repo);
        var (result, error) = await simular.HandleAsync(id, tenant, "comprador", ct);

        error.Should().BeNull();
        result.Should().NotBeNull();
        instance.BiometricValidations.Should().Contain(v =>
            v.PartyRole == "comprador" && v.Status == BiometricEstados.Aprobado);
    }
}
