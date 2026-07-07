using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Enums;
using Flit.Tramites.Domain.Integration;
using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.Tramites.Catalog;
using Flit.Tramites.Domain.Tramites.Enums;
using Flit.Tramites.Domain.Tramites.Estados;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Tramites.Application.Tests.UseCases.ProcedureInstances;

/// <summary>
/// HU #10590/#10592 (F11, R12) — cableado runtime del wizard server-driven para el traspaso unilateral:
/// la modalidad <c>traspaso_unilateral</c> despacha a su journey de 5 pasos (Consulta, Documentos,
/// Arrendadora, Locatario, Generar FUR), NO al de matrícula. La ARRENDADORA es la única parte que valida
/// identidad (D3); el LOCATARIO es documental. El paso 5 conserva <c>key == "fur"</c> (contrato con el FE
/// #10591/#10593) y emite <c>pendiente_biometria</c>/<c>fur_pendiente</c> como paso diferido.
/// </summary>
public sealed class WizardTraspasoUnilateralStateTests
{
    private readonly IProcedureInstanceRepository _repo = Substitute.For<IProcedureInstanceRepository>();
    private readonly GetWizardStateHandler _handler;

    public WizardTraspasoUnilateralStateTests()
    {
        _handler = new GetWizardStateHandler(_repo);
    }

    private static ProcedureInstance Base() =>
        new()
        {
            Id = Guid.NewGuid(),
            TenantId = Guid.NewGuid(),
            ProcedureTypeId = Guid.NewGuid(),
            ReferenceNumber = "TRM-2026-000300",
            Status = TramiteEstado.Borrador,
            ModalidadEntrada = TramiteModalidadEntradaCodes.TraspasoUnilateral,
            TipologiaCodigo = TramiteTipologiaCatalog.CodigoTraspasoUnilateral,
            CreatedAt = DateTimeOffset.UtcNow,
        };

    private static ProcedureInstanceActor Actor(string actorType, string doc) =>
        new()
        {
            Id = Guid.NewGuid(),
            ActorType = actorType,
            DocumentType = "CC",
            DocumentNumber = doc,
            FullName = "Persona",
            Email = "p@x.com",
            CreatedAt = DateTimeOffset.UtcNow,
        };

    private static ProcedureInstanceBiometricValidation BioAprobada(string parte, string doc) =>
        new()
        {
            Id = Guid.NewGuid(),
            PartyRole = parte,
            Status = BiometricEstados.Aprobado,
            Name = "Persona",
            DocumentType = "CC",
            DocumentNumber = doc,
            Email = "p@x.com",
            TokenHash = Guid.NewGuid().ToString("N"),
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(1),
            CreatedAt = DateTimeOffset.UtcNow,
        };

    private static ProcedureInstancePreflightSnapshot Preflight(string overall) =>
        new() { Id = Guid.NewGuid(), Overall = overall, Checks = "[]", CreatedAt = DateTimeOffset.UtcNow };

    private static ProcedureInstanceAttachment Doc(string tipo) =>
        new() { Id = Guid.NewGuid(), Tipo = tipo, Filename = $"{tipo}.pdf", StoragePath = $"x/{tipo}", UploadedAt = DateTimeOffset.UtcNow };

    /// <summary>Placa + docs obligatorios del checklist unilateral → pasos 1-2 completos, 3-5 alcanzables.</summary>
    private static void CompletarConsultaYDocs(ProcedureInstance instance)
    {
        instance.FieldValues.Add(new ProcedureInstanceFieldValue { FieldKey = "plate", ValueText = "ABC123", Source = "user" });
        instance.PreflightSnapshots.Add(Preflight("green"));
        instance.Attachments.Add(Doc("paz_salvo_locatario"));
        instance.Attachments.Add(Doc("doc_locatario"));
        instance.Attachments.Add(Doc("contrato_leasing"));
        instance.Attachments.Add(Doc("declaracion_arrendadora"));
    }

    private void Setup(ProcedureInstance instance) =>
        _repo.GetByIdWithWizardGraphAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(instance);

    // ── Despacho de modalidad + conteo de pasos ──────────────────────────────

    [Fact]
    public async Task Get_Unilateral_ReportsModalidadAnd5Steps()
    {
        var ct = TestContext.Current.CancellationToken;
        Setup(Base());

        var (result, _) = await _handler.HandleAsync(Guid.NewGuid(), Guid.NewGuid(), ct);

        result!.Modalidad.Should().Be(TramiteModalidadEntradaCodes.TraspasoUnilateral);
        result.TotalSteps.Should().Be(5);
        result.Steps.Should().HaveCount(5);
    }

    [Fact]
    public async Task Get_Unilateral_StepKeysMatchFrontendContract()
    {
        var ct = TestContext.Current.CancellationToken;
        Setup(Base());

        var (result, _) = await _handler.HandleAsync(Guid.NewGuid(), Guid.NewGuid(), ct);

        result!.Steps.Select(s => s.Key).Should()
            .ContainInOrder("consulta", "documentos", "arrendadora", "locatario", "fur");
        // Contrato con el FE (#10591/#10593): el paso 5 DEBE tener key 'fur'.
        result.Steps.Single(s => s.Index == 5).Key.Should().Be("fur");
    }

    // ── Cascada de pasos ─────────────────────────────────────────────────────

    [Fact]
    public async Task Get_Unilateral_EmptyInstance_DeferredStepsLocked()
    {
        var ct = TestContext.Current.CancellationToken;
        Setup(Base()); // sin placa ni docs → pasos 3-5 no alcanzables.

        var (result, _) = await _handler.HandleAsync(Guid.NewGuid(), Guid.NewGuid(), ct);

        result!.Steps.Single(s => s.Index == 1).Status.Should().Be("incomplete"); // consulta pendiente
        result.Steps.Single(s => s.Index == 3).Status.Should().Be("locked");
        result.Steps.Single(s => s.Index == 4).Status.Should().Be("locked");
        result.Steps.Single(s => s.Index == 5).Status.Should().Be("locked");
    }

    // ── Paso 3 (Arrendadora): única parte que valida identidad ────────────────

    [Fact]
    public async Task Get_Unilateral_ArrendadoraPendiente_Step3IncompleteWithBiometria()
    {
        var ct = TestContext.Current.CancellationToken;
        var instance = Base();
        CompletarConsultaYDocs(instance);
        instance.Actors.Add(Actor(BiometricRules.ParteArrendadora, "900"));
        // Sin biométrica aprobada de la arrendadora.
        Setup(instance);

        var (result, _) = await _handler.HandleAsync(Guid.NewGuid(), Guid.NewGuid(), ct);

        var s3 = result!.Steps.Single(s => s.Index == 3);
        s3.Status.Should().Be("incomplete");
        s3.Reasons.Should().Contain(GetWizardStateHandler.PendienteBiometria);
    }

    [Fact]
    public async Task Get_Unilateral_ArrendadoraAprobada_Step3Complete()
    {
        var ct = TestContext.Current.CancellationToken;
        var instance = Base();
        CompletarConsultaYDocs(instance);
        instance.Actors.Add(Actor(BiometricRules.ParteArrendadora, "900"));
        instance.BiometricValidations.Add(BioAprobada(BiometricRules.ParteArrendadora, "900"));
        Setup(instance);

        var (result, _) = await _handler.HandleAsync(Guid.NewGuid(), Guid.NewGuid(), ct);

        var s3 = result!.Steps.Single(s => s.Index == 3);
        s3.Status.Should().Be("complete");
        s3.Reasons.Should().BeEmpty();
    }

    [Fact]
    public async Task Get_Unilateral_Locatario_IsDocumental_NoBiometrica()
    {
        // El locatario (paso 4) es DOCUMENTAL: con docs completos queda 'complete' aunque NO tenga
        // ninguna validación biométrica (solo la arrendadora valida identidad).
        var ct = TestContext.Current.CancellationToken;
        var instance = Base();
        CompletarConsultaYDocs(instance);
        instance.Actors.Add(Actor(BiometricRules.ParteLocatario, "800"));
        Setup(instance);

        var (result, _) = await _handler.HandleAsync(Guid.NewGuid(), Guid.NewGuid(), ct);

        result!.Steps.Single(s => s.Index == 4).Status.Should().Be("complete");
    }

    // ── Paso 5 (FUR) diferido ────────────────────────────────────────────────

    [Fact]
    public async Task Get_Unilateral_FurReachable_DefersArrendadoraBiometriaAndFur()
    {
        var ct = TestContext.Current.CancellationToken;
        var instance = Base();
        CompletarConsultaYDocs(instance); // datos completos → FUR (5) alcanzable
        // Sin biométrica de arrendadora ni FUR generado.
        Setup(instance);

        var (result, _) = await _handler.HandleAsync(Guid.NewGuid(), Guid.NewGuid(), ct);

        var fur = result!.Steps.Single(s => s.Index == 5);
        fur.Key.Should().Be("fur");
        fur.Status.Should().Be("incomplete");
        fur.Reasons.Should().Contain(GetWizardStateHandler.PendienteBiometria);
        fur.Reasons.Should().Contain(GetWizardStateHandler.FurPendiente);
        // Sin firma de compraventa: el unilateral no la exige.
        fur.Reasons.Should().NotContain(GetWizardStateHandler.PendienteFirma);
    }

    [Fact]
    public async Task Get_Unilateral_ArrendadoraApprobada_FurStillPending()
    {
        var ct = TestContext.Current.CancellationToken;
        var instance = Base();
        CompletarConsultaYDocs(instance);
        instance.Actors.Add(Actor(BiometricRules.ParteArrendadora, "900"));
        instance.BiometricValidations.Add(BioAprobada(BiometricRules.ParteArrendadora, "900"));
        // Aún sin FUR generado.
        Setup(instance);

        var (result, _) = await _handler.HandleAsync(Guid.NewGuid(), Guid.NewGuid(), ct);

        var fur = result!.Steps.Single(s => s.Index == 5);
        fur.Status.Should().Be("incomplete");
        fur.Reasons.Should().NotContain(GetWizardStateHandler.PendienteBiometria);
        fur.Reasons.Should().Contain(GetWizardStateHandler.FurPendiente);
    }

    [Fact]
    public async Task Get_Unilateral_TodoCompleto_FurComplete()
    {
        var ct = TestContext.Current.CancellationToken;
        var instance = Base();
        CompletarConsultaYDocs(instance);
        instance.Actors.Add(Actor(BiometricRules.ParteArrendadora, "900"));
        instance.BiometricValidations.Add(BioAprobada(BiometricRules.ParteArrendadora, "900"));
        instance.Attachments.Add(Doc("fur"));
        Setup(instance);

        var (result, _) = await _handler.HandleAsync(Guid.NewGuid(), Guid.NewGuid(), ct);

        var fur = result!.Steps.Single(s => s.Index == 5);
        fur.Status.Should().Be("complete");
        fur.Reasons.Should().BeEmpty();
    }

    // ── canSubmit / blockers ─────────────────────────────────────────────────

    [Fact]
    public async Task Get_Unilateral_ArrendadoraPendiente_BlocksSubmit()
    {
        var ct = TestContext.Current.CancellationToken;
        var instance = Base();
        CompletarConsultaYDocs(instance);
        instance.Actors.Add(Actor(BiometricRules.ParteArrendadora, "900"));
        Setup(instance);

        var (result, _) = await _handler.HandleAsync(Guid.NewGuid(), Guid.NewGuid(), ct);

        result!.Blockers.Should().Contain(TramiteEstadoErrores.IdentidadNoAprobada);
        result.CanSubmit.Should().BeFalse();
    }

    [Fact]
    public async Task Get_Unilateral_DocsIncompletos_Step2ReasonAndGlobalBlocker()
    {
        var ct = TestContext.Current.CancellationToken;
        var instance = Base();
        instance.FieldValues.Add(new ProcedureInstanceFieldValue { FieldKey = "plate", ValueText = "ABC123", Source = "user" });
        instance.PreflightSnapshots.Add(Preflight("green"));
        // Sin adjuntos → faltan documentos obligatorios del checklist unilateral.
        Setup(instance);

        var (result, _) = await _handler.HandleAsync(Guid.NewGuid(), Guid.NewGuid(), ct);

        var s2 = result!.Steps.Single(s => s.Index == 2);
        s2.Status.Should().Be("incomplete");
        s2.Reasons.Should().Contain(TramiteEstadoErrores.DocumentosIncompletos);
        result.Blockers.Should().Contain(TramiteEstadoErrores.DocumentosIncompletos);
        result.CanSubmit.Should().BeFalse();
    }

    [Fact]
    public async Task Get_Unilateral_ArrendadoraAprobadaAndDocsComplete_CanSubmitTrue()
    {
        // FUR (paso 5) es diferido → no cuenta contra el submit; con datos + arrendadora aprobada,
        // los pasos 1-4 quedan complete y sin blockers → submit habilitado.
        var ct = TestContext.Current.CancellationToken;
        var instance = Base();
        CompletarConsultaYDocs(instance);
        instance.Actors.Add(Actor(BiometricRules.ParteArrendadora, "900"));
        instance.BiometricValidations.Add(BioAprobada(BiometricRules.ParteArrendadora, "900"));
        Setup(instance);

        var (result, _) = await _handler.HandleAsync(Guid.NewGuid(), Guid.NewGuid(), ct);

        result!.Steps.Single(s => s.Index == 1).Status.Should().Be("complete");
        result.Steps.Single(s => s.Index == 2).Status.Should().Be("complete");
        result.Steps.Single(s => s.Index == 3).Status.Should().Be("complete");
        result.Steps.Single(s => s.Index == 4).Status.Should().Be("complete");
        result.Blockers.Should().BeEmpty();
        result.CanSubmit.Should().BeTrue();
    }

    // ── HU #10548 — identidad deshabilitada por el OT ────────────────────────

    [Fact]
    public async Task Get_Unilateral_IdentityDisabled_ArrendadoraTreatedAsSatisfied()
    {
        // Si el OT destino deshabilita la identidad, la arrendadora se trata como satisfecha: el
        // blocker de identidad desaparece y el paso 3 se completa sin biométrica real.
        var ct = TestContext.Current.CancellationToken;
        var instance = Base();
        CompletarConsultaYDocs(instance);
        instance.Actors.Add(Actor(BiometricRules.ParteArrendadora, "900"));

        var policy = Substitute.For<IIdentityValidationPolicy>();
        policy.IsIdentityValidationRequiredAsync(Arg.Any<Guid>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .Returns(false);
        var handler = new GetWizardStateHandler(_repo, policy);
        Setup(instance);

        var (result, _) = await handler.HandleAsync(Guid.NewGuid(), Guid.NewGuid(), ct);

        result!.IdentityValidationEnabled.Should().BeFalse();
        result.Blockers.Should().NotContain(TramiteEstadoErrores.IdentidadNoAprobada);
        result.Steps.Single(s => s.Index == 3).Status.Should().Be("complete");
        result.CanSubmit.Should().BeTrue();
    }
}
