using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Enums;
using Flit.Tramites.Domain.Integration;
using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.Tramites.Catalog;
using FluentAssertions;
using NSubstitute;
using Xunit;
using Flit.Tramites.Domain.Tramites.Estados;

namespace Flit.Tramites.Application.Tests.UseCases.ProcedureInstances;

public sealed class WizardStateHandlerTests
{
    private readonly IProcedureInstanceRepository _repo = Substitute.For<IProcedureInstanceRepository>();
    private readonly GetWizardStateHandler _handler;

    public WizardStateHandlerTests()
    {
        _handler = new GetWizardStateHandler(_repo);
    }

    private static ProcedureInstance Base(string modalidad, string? tipologia = null) =>
        new()
        {
            Id = Guid.NewGuid(),
            TenantId = Guid.NewGuid(),
            ProcedureTypeId = Guid.NewGuid(),
            ReferenceNumber = "TRM-2026-000001",
            Status = TramiteEstado.Borrador,
            ModalidadEntrada = modalidad,
            TipologiaCodigo = tipologia,
            CreatedAt = DateTimeOffset.UtcNow,
        };

    private static ProcedureInstanceActor Actor(string actorType, string doc = "123") =>
        new()
        {
            ActorType = actorType,
            DocumentType = "CC",
            DocumentNumber = doc,
            FullName = "Persona",
            Email = "p@x.com",
        };

    private static ProcedureInstancePreflightSnapshot Preflight(string overall, string checks = "[]") =>
        new()
        {
            Id = Guid.NewGuid(),
            Overall = overall,
            Checks = checks,
            CreatedAt = DateTimeOffset.UtcNow,
        };

    private static ProcedureInstanceAttachment Attachment(string tipo) =>
        new() { Id = Guid.NewGuid(), Tipo = tipo, Filename = $"{tipo}.pdf" };

    /// <summary>
    /// Satisface el checklist obligatorio de matrícula inicial: 3 docs por adjunto
    /// (factura, aduana, impronta). SOAT y los demás ítems son opcionales.
    /// </summary>
    private static void CompletarDocsMatricula(ProcedureInstance instance)
    {
        instance.Attachments.Add(Attachment("factura"));
        instance.Attachments.Add(Attachment("aduana"));
        instance.Attachments.Add(Attachment("impronta"));
    }

    /// <summary>
    /// Satisface el checklist obligatorio de traspaso_standard marcando todos sus ítems
    /// (vía estado manual). Necesario porque los documentos ahora gobiernan el paso 2: sin
    /// ellos el flujo se bloquea en 2 y los pasos posteriores quedan locked.
    /// </summary>
    private static void CompletarDocsTraspaso(ProcedureInstance instance)
    {
        instance.ChecklistEstado =
            "{\"contrato_compraventa\":true,\"impronta\":true,\"soat\":true,\"rtm\":true,\"paz_salvo\":true,\"cedulas\":true}";
    }

    /// <summary>
    /// Identidad del comprador aprobada y vigente (N 03, RF03): el gate borrador→preparado y el
    /// blocker <c>identidad_no_aprobada</c> del wizard exigen biométrica del documento del actor.
    /// </summary>
    private static void AprobarIdentidadComprador(ProcedureInstance instance, string doc = "123") =>
        AprobarIdentidad(instance, "comprador", doc);

    /// <summary>Agrega una validación biométrica PROPIA aprobada+vigente para la parte y documento dados.</summary>
    private static void AprobarIdentidad(ProcedureInstance instance, string parte, string doc)
    {
        instance.BiometricValidations.Add(new ProcedureInstanceBiometricValidation
        {
            Id = Guid.NewGuid(),
            TenantId = instance.TenantId,
            ProcedureInstanceId = instance.Id,
            PartyRole = parte,
            Status = BiometricEstados.Aprobado,
            Name = "Persona",
            DocumentType = "CC",
            DocumentNumber = doc,
            Email = "p@x.com",
            TokenHash = Guid.NewGuid().ToString("N"),
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(1),
            CreatedAt = DateTimeOffset.UtcNow,
        });
    }

    private void Setup(ProcedureInstance instance) =>
        _repo.GetByIdWithWizardGraphAsync(Arg.Any<Guid>(), Arg.Any<Guid>(),
            Arg.Any<CancellationToken>()).Returns(instance);

    // ── 404 ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Get_InstanceNotFound_ReturnsNotFound()
    {
        var ct = TestContext.Current.CancellationToken;
        _repo.GetByIdWithWizardGraphAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), ct).Returns((ProcedureInstance?)null);

        var (result, error) = await _handler.HandleAsync(Guid.NewGuid(), Guid.NewGuid(), ct);

        error.Should().Be("not_found");
        result.Should().BeNull();
    }

    // ── Conteo de pasos por modalidad ─────────────────────────────────────────

    [Fact]
    public async Task Get_Matricula_Has5Steps()
    {
        var ct = TestContext.Current.CancellationToken;
        Setup(Base("matricula_inicial"));

        var (result, _) = await _handler.HandleAsync(Guid.NewGuid(), Guid.NewGuid(), ct);

        result!.Modalidad.Should().Be("matricula_inicial");
        result.TotalSteps.Should().Be(5);
        result.Steps.Should().HaveCount(5);
    }

    // ── HU #10548 — flag de exigibilidad de identidad por OT ─────────────────

    [Fact] // Por defecto (política permisiva/no cableada) la identidad se exige.
    public async Task Get_IdentityValidation_EnabledByDefault()
    {
        var ct = TestContext.Current.CancellationToken;
        Setup(Base("matricula_inicial"));

        var (result, _) = await _handler.HandleAsync(Guid.NewGuid(), Guid.NewGuid(), ct);

        result!.IdentityValidationEnabled.Should().BeTrue();
    }

    [Fact] // AC3 — OT con identidad deshabilitada: el flag viaja en false para que el wizard oculte el paso.
    public async Task Get_IdentityValidationDisabled_FlagFalse()
    {
        var ct = TestContext.Current.CancellationToken;
        Setup(Base("matricula_inicial"));

        var policy = Substitute.For<IIdentityValidationPolicy>();
        policy.IsIdentityValidationRequiredAsync(
            Arg.Any<Guid>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>()).Returns(false);
        var handler = new GetWizardStateHandler(_repo, policy);

        var (result, _) = await handler.HandleAsync(Guid.NewGuid(), Guid.NewGuid(), ct);

        result!.IdentityValidationEnabled.Should().BeFalse();
        result.Blockers.Should().NotContain(TramiteEstadoErrores.IdentidadNoAprobada);
    }

    [Fact]
    public async Task Get_Traspaso_Has6Steps()
    {
        var ct = TestContext.Current.CancellationToken;
        Setup(Base("traspaso"));

        var (result, _) = await _handler.HandleAsync(Guid.NewGuid(), Guid.NewGuid(), ct);

        result!.Modalidad.Should().Be("traspaso");
        result.TotalSteps.Should().Be(6);
        result.Steps.Should().HaveCount(6);
    }

    // ── Pasos diferidos (biométrica / firma) ──────────────────────────────────

    // Cascada: en una instancia vacía los pasos diferidos NO son alcanzables → locked
    // (no se puede saltar a Identidad sin Comprador). Solo cuando el flujo llega a ellos
    // se evalúan con sus reasons diferidas.

    [Fact]
    public async Task Get_Matricula_EmptyInstance_DeferredStepsLocked()
    {
        var ct = TestContext.Current.CancellationToken;
        Setup(Base("matricula_inicial")); // maxAlcanzable = 1 (sin VIN).

        var (result, _) = await _handler.HandleAsync(Guid.NewGuid(), Guid.NewGuid(), ct);

        var identidad = result!.Steps.Single(s => s.Index == 4);
        identidad.Status.Should().Be("locked");
        identidad.Reasons.Should().BeEmpty();

        var fur = result.Steps.Single(s => s.Index == 5);
        fur.Status.Should().Be("locked");
        fur.Reasons.Should().BeEmpty();
    }

    [Fact]
    public async Task Get_Matricula_IdentidadReachable_IncompleteWithBiometriaReason()
    {
        // Pasos 1-3 completos (VIN, docs, comprador+RUNT) → Identidad (4) alcanzable y,
        // sin biométrica aprobada, incomplete con 'pendiente_biometria'. HU #10350: FUR (5) ahora
        // también es ALCANZABLE en cuanto los datos están completos, aunque la identidad siga
        // pendiente — es el último paso donde el gestor finaliza el borrador → incomplete + fur_pendiente.
        var ct = TestContext.Current.CancellationToken;
        var instance = Base("matricula_inicial");
        instance.FieldValues.Add(new ProcedureInstanceFieldValue { FieldKey = "vin", ValueText = "1HGCM82633A004352", Source = "user" });
        instance.PreflightSnapshots.Add(Preflight("green"));
        instance.Actors.Add(Actor("comprador", "777"));
        CompletarDocsMatricula(instance);
        Setup(instance);

        var (result, _) = await _handler.HandleAsync(Guid.NewGuid(), Guid.NewGuid(), ct);

        var identidad = result!.Steps.Single(s => s.Index == 4);
        identidad.Status.Should().Be("incomplete");
        identidad.Reasons.Should().Contain(GetWizardStateHandler.PendienteBiometria);

        var fur = result.Steps.Single(s => s.Index == 5);
        fur.Status.Should().Be("incomplete");
        fur.Reasons.Should().Contain(GetWizardStateHandler.FurPendiente);
    }

    [Fact]
    public async Task Get_Traspaso_EmptyInstance_FurStepLocked()
    {
        var ct = TestContext.Current.CancellationToken;
        Setup(Base("traspaso")); // maxAlcanzable = 1 (sin placa consultada → paso 1 es la frontera).

        var (result, _) = await _handler.HandleAsync(Guid.NewGuid(), Guid.NewGuid(), ct);

        var fur = result!.Steps.Single(s => s.Index == 6);
        fur.Status.Should().Be("locked");
        fur.Reasons.Should().BeEmpty();
    }

    [Fact]
    public async Task Get_Traspaso_FurReachable_DefersBiometric()
    {
        // Pasos 1-5 completos → FUR (6) alcanzable. Sin biométrica/FUR generado → incomplete con la
        // razón diferida de biométrica. B12 (HU #10661, ADR-0028): la firma de compraventa YA NO
        // aporta `pendiente_firma` ni condiciona el completado del paso 6.
        var ct = TestContext.Current.CancellationToken;
        var instance = Base("traspaso", TramiteTipologiaCatalog.CodigoTraspasoStandard);
        instance.FieldValues.Add(new ProcedureInstanceFieldValue { FieldKey = "plate", ValueText = "ABC123", Source = "user" });
        instance.Actors.Add(Actor("vendedor", "555"));
        instance.Actors.Add(Actor("comprador", "666"));
        instance.PreflightSnapshots.Add(Preflight("green"));
        CompletarDocsTraspaso(instance); // docs gobiernan el paso 2; sin ellos FUR queda locked.
        instance.Commercial = new ProcedureInstanceCommercial { Id = Guid.NewGuid(), ValorVenta = 100m, CreatedAt = DateTimeOffset.UtcNow };
        Setup(instance);

        var (result, _) = await _handler.HandleAsync(Guid.NewGuid(), Guid.NewGuid(), ct);

        var fur = result!.Steps.Single(s => s.Index == 6);
        fur.Status.Should().Be("incomplete");
        fur.Reasons.Should().Contain(GetWizardStateHandler.PendienteBiometria);
        // B12: la firma ya no es una razón de incompletitud del paso 6.
        fur.Reasons.Should().NotContain(GetWizardStateHandler.PendienteFirma);
    }

    [Fact]
    public async Task Get_Traspaso_BiometriaYFurSinFirma_Step6CompleteAndCanSubmit()
    {
        // B12 (HU #10661, ADR-0028) AC1/AC4: con biométrica de AMBAS partes + FUR generado pero SIN
        // firma de compraventa, el paso 6 completa y el trámite puede radicarse (canSubmit=true).
        var ct = TestContext.Current.CancellationToken;
        var instance = Base("traspaso", TramiteTipologiaCatalog.CodigoTraspasoStandard);
        instance.FieldValues.Add(new ProcedureInstanceFieldValue { FieldKey = "plate", ValueText = "ABC123", Source = "user" });
        instance.FieldValues.Add(new ProcedureInstanceFieldValue { FieldKey = "transit_office_code", ValueText = "11001000", Source = "user" });
        instance.Actors.Add(Actor("vendedor", "555"));
        instance.Actors.Add(Actor("comprador", "666"));
        instance.PreflightSnapshots.Add(Preflight("green"));
        CompletarDocsTraspaso(instance);
        instance.Commercial = new ProcedureInstanceCommercial { Id = Guid.NewGuid(), ValorVenta = 100m, CreatedAt = DateTimeOffset.UtcNow };
        AprobarIdentidad(instance, "comprador", "666");
        AprobarIdentidad(instance, "vendedor", "555");
        instance.Attachments.Add(Attachment("fur")); // FUR generado; NO se agrega ninguna firma.
        Setup(instance);

        var (result, _) = await _handler.HandleAsync(Guid.NewGuid(), Guid.NewGuid(), ct);

        var fur = result!.Steps.Single(s => s.Index == 6);
        fur.Status.Should().Be("complete");
        fur.Reasons.Should().NotContain(GetWizardStateHandler.PendienteFirma);
        result.CanSubmit.Should().BeTrue();
    }

    // ── Mapeo persistencia → GateContext (pasos completan al llenarlos) ───────

    [Fact]
    public async Task Get_Matricula_VinPresent_Step1Complete()
    {
        var ct = TestContext.Current.CancellationToken;
        var instance = Base("matricula_inicial");
        instance.FieldValues.Add(new ProcedureInstanceFieldValue { FieldKey = "vin", ValueText = "1HGCM82633A004352", Source = "user" });
        Setup(instance);

        var (result, _) = await _handler.HandleAsync(Guid.NewGuid(), Guid.NewGuid(), ct);

        result!.Steps.Single(s => s.Index == 1).Status.Should().Be("complete");
    }

    [Fact]
    public async Task Get_Matricula_NoVin_Step1IncompleteWithReason()
    {
        var ct = TestContext.Current.CancellationToken;
        Setup(Base("matricula_inicial"));

        var (result, _) = await _handler.HandleAsync(Guid.NewGuid(), Guid.NewGuid(), ct);

        var s1 = result!.Steps.Single(s => s.Index == 1);
        s1.Status.Should().Be("incomplete");
        s1.Reasons.Should().Contain("vin_pendiente");
    }

    [Fact]
    public async Task Get_Traspaso_VendedorMappedToParteAndRunt()
    {
        var ct = TestContext.Current.CancellationToken;
        var instance = Base("traspaso");
        instance.FieldValues.Add(new ProcedureInstanceFieldValue { FieldKey = "plate", ValueText = "ABC123", Source = "user" });
        instance.Actors.Add(Actor("vendedor", "555"));
        CompletarDocsTraspaso(instance); // habilita pasar el paso 2 (documentos) para evaluar el 3.
        Setup(instance);

        var (result, _) = await _handler.HandleAsync(Guid.NewGuid(), Guid.NewGuid(), ct);

        // Paso 3 (vendedor) completo: parte completa + RUNT consultado (documento presente).
        result!.Steps.Single(s => s.Index == 3).Status.Should().Be("complete");
        // Paso 4 (comprador) incompleto: sin comprador.
        result.Steps.Single(s => s.Index == 4).Status.Should().Be("incomplete");
    }

    [Fact]
    public async Task Get_Traspaso_ValorVentaMappedFromCommercial()
    {
        var ct = TestContext.Current.CancellationToken;
        var instance = Base("traspaso");
        instance.FieldValues.Add(new ProcedureInstanceFieldValue { FieldKey = "plate", ValueText = "ABC123", Source = "user" });
        instance.Actors.Add(Actor("vendedor", "555"));
        instance.Actors.Add(Actor("comprador", "666"));
        instance.PreflightSnapshots.Add(Preflight("green"));
        CompletarDocsTraspaso(instance); // habilita pasar el paso 2 (documentos) para evaluar el 5.
        instance.Commercial = new ProcedureInstanceCommercial { Id = Guid.NewGuid(), ValorVenta = 100m, CreatedAt = DateTimeOffset.UtcNow };
        Setup(instance);

        var (result, _) = await _handler.HandleAsync(Guid.NewGuid(), Guid.NewGuid(), ct);

        // Paso 5 (comercial) completo gracias a ValorVenta > 0.
        result!.Steps.Single(s => s.Index == 5).Status.Should().Be("complete");
    }

    // ── Blockers / canSubmit ──────────────────────────────────────────────────

    [Fact]
    public async Task Get_PreflightRed_AddsBlockerAndBlocksSubmit()
    {
        var ct = TestContext.Current.CancellationToken;
        var instance = Base("matricula_inicial");
        instance.FieldValues.Add(new ProcedureInstanceFieldValue { FieldKey = "vin", ValueText = "1HGCM82633A004352", Source = "user" });
        instance.PreflightSnapshots.Add(Preflight("red"));
        Setup(instance);

        var (result, _) = await _handler.HandleAsync(Guid.NewGuid(), Guid.NewGuid(), ct);

        result!.Blockers.Should().Contain("preflight_red");
        result.CanSubmit.Should().BeFalse();
    }

    [Fact]
    public async Task Get_PreflightRed_RiesgoAceptado_LiftsBlockerAndStep2Completes()
    {
        // "Asumo el riesgo" (riesgo_aceptado=true en field_values) levanta el blocker de
        // preflight rojo subsanable: el paso 2 (documentos) deja de bloquearse por preflight
        // y el submit ya no se veta por ese motivo.
        var ct = TestContext.Current.CancellationToken;
        var instance = Base("matricula_inicial");
        instance.FieldValues.Add(new ProcedureInstanceFieldValue { FieldKey = "vin", ValueText = "1HGCM82633A004352", Source = "user" });
        instance.FieldValues.Add(new ProcedureInstanceFieldValue { FieldKey = "riesgo_aceptado", ValueText = "true", Source = "user" });
        instance.PreflightSnapshots.Add(Preflight("red"));
        instance.Actors.Add(Actor("comprador", "777"));
        CompletarDocsMatricula(instance);
        AprobarIdentidadComprador(instance, "777"); // N 03: la identidad también gatea el submit
        Setup(instance);

        var (result, _) = await _handler.HandleAsync(Guid.NewGuid(), Guid.NewGuid(), ct);

        result!.Blockers.Should().NotContain("preflight_red");
        // Paso 2 completo: preflight rojo ya no bloquea y los docs obligatorios están.
        result.Steps.Single(s => s.Index == 2).Status.Should().Be("complete");
        // Pasos 1-3 completos; identidad aprobada; FUR diferido → submit habilitado.
        result.CanSubmit.Should().BeTrue();
    }

    [Fact]
    public async Task Get_PreflightProviderError_AddsHardBlocker_NotLiftedByRiesgo()
    {
        // Una consulta no verificable (check "error") es un bloqueo DURO: aunque el gestor acepte
        // el riesgo, el blocker preflight_provider_error se mantiene, el paso 2 NO se completa y el
        // submit sigue vetado. Solo se levanta reejecutando la consulta con éxito.
        var ct = TestContext.Current.CancellationToken;
        var instance = Base("matricula_inicial");
        instance.FieldValues.Add(new ProcedureInstanceFieldValue { FieldKey = "vin", ValueText = "1HGCM82633A004352", Source = "user" });
        instance.FieldValues.Add(new ProcedureInstanceFieldValue { FieldKey = "riesgo_aceptado", ValueText = "true", Source = "user" });
        instance.Actors.Add(Actor("comprador", "777"));
        CompletarDocsMatricula(instance);
        instance.PreflightSnapshots.Add(Preflight("red",
            "[{\"Key\":\"provider\",\"Label\":\"Consulta de vehículo\",\"Status\":\"error\",\"Source\":\"verifik\",\"Message\":\"No fue posible verificar\"}]"));
        Setup(instance);

        var (result, _) = await _handler.HandleAsync(Guid.NewGuid(), Guid.NewGuid(), ct);

        result!.Blockers.Should().Contain("preflight_provider_error");
        result.Blockers.Should().NotContain("preflight_red");
        result.Steps.Single(s => s.Index == 2).Status.Should().NotBe("complete");
        result.CanSubmit.Should().BeFalse();
    }

    [Fact]
    public async Task Get_Matricula_AllNonDeferredComplete_CanSubmitTrue()
    {
        var ct = TestContext.Current.CancellationToken;
        var instance = Base("matricula_inicial");
        instance.FieldValues.Add(new ProcedureInstanceFieldValue { FieldKey = "vin", ValueText = "1HGCM82633A004352", Source = "user" });
        instance.PreflightSnapshots.Add(Preflight("green"));
        instance.Actors.Add(Actor("comprador", "777"));
        CompletarDocsMatricula(instance);
        AprobarIdentidadComprador(instance, "777"); // N 03: la identidad también gatea el submit
        Setup(instance);

        var (result, _) = await _handler.HandleAsync(Guid.NewGuid(), Guid.NewGuid(), ct);

        // Pasos 1-3 completos (vin, documentos completos, comprador+runt); FUR diferido → no cuenta.
        result!.Steps.Single(s => s.Index == 1).Status.Should().Be("complete");
        result.Steps.Single(s => s.Index == 2).Status.Should().Be("complete");
        result.Steps.Single(s => s.Index == 3).Status.Should().Be("complete");
        result.CanSubmit.Should().BeTrue();
    }

    // ── Gating ESTRICTO de documentos obligatorios (Slice 4a-fix) ─────────────

    [Fact]
    public async Task Get_Matricula_DocsIncompletos_Step2IncompleteAndBlocksSubmit()
    {
        var ct = TestContext.Current.CancellationToken;
        var instance = Base("matricula_inicial");
        instance.FieldValues.Add(new ProcedureInstanceFieldValue { FieldKey = "vin", ValueText = "1HGCM82633A004352", Source = "user" });
        instance.PreflightSnapshots.Add(Preflight("green"));
        instance.Actors.Add(Actor("comprador", "777"));
        // Sin adjuntos → faltan documentos obligatorios.
        Setup(instance);

        var (result, _) = await _handler.HandleAsync(Guid.NewGuid(), Guid.NewGuid(), ct);

        var s2 = result!.Steps.Single(s => s.Index == 2);
        s2.Status.Should().Be("incomplete");
        s2.Reasons.Should().Contain("documentos_incompletos");
        result.Blockers.Should().Contain("documentos_incompletos");
        result.CanSubmit.Should().BeFalse();
    }

    [Fact]
    public async Task Get_Traspaso_DocsIncompletos_Step2ReasonAndGlobalBlocker()
    {
        var ct = TestContext.Current.CancellationToken;
        // Tipología real → su checklist obligatorio aplica (sin adjuntos = faltan docs).
        var instance = Base("traspaso", TramiteTipologiaCatalog.CodigoTraspasoStandard);
        instance.FieldValues.Add(new ProcedureInstanceFieldValue { FieldKey = "plate", ValueText = "ABC123", Source = "user" });
        instance.Actors.Add(Actor("vendedor", "555"));
        instance.Actors.Add(Actor("comprador", "666"));
        instance.PreflightSnapshots.Add(Preflight("green"));
        instance.Commercial = new ProcedureInstanceCommercial { Id = Guid.NewGuid(), ValorVenta = 100m, CreatedAt = DateTimeOffset.UtcNow };
        Setup(instance);

        var (result, _) = await _handler.HandleAsync(Guid.NewGuid(), Guid.NewGuid(), ct);

        // Los documentos ahora gobiernan el paso 2: su reason vive ahí; el blocker GLOBAL veta el submit.
        var s2 = result!.Steps.Single(s => s.Index == 2);
        s2.Status.Should().Be("incomplete");
        s2.Reasons.Should().Contain("documentos_incompletos");
        result.Blockers.Should().Contain("documentos_incompletos");
        result.CanSubmit.Should().BeFalse();
    }

    [Fact]
    public async Task Get_Traspaso_Step2KeyIsDocumentos()
    {
        var ct = TestContext.Current.CancellationToken;
        Setup(Base("traspaso"));

        var (result, _) = await _handler.HandleAsync(Guid.NewGuid(), Guid.NewGuid(), ct);

        result!.Steps.Single(s => s.Index == 2).Key.Should().Be("documentos");
    }

    [Fact]
    public async Task Get_Matricula_Step2KeyRemainsDocumentos()
    {
        var ct = TestContext.Current.CancellationToken;
        Setup(Base("matricula_inicial"));

        var (result, _) = await _handler.HandleAsync(Guid.NewGuid(), Guid.NewGuid(), ct);

        result!.Steps.Single(s => s.Index == 2).Key.Should().Be("documentos");
    }

    [Fact]
    public async Task Get_Matricula_IncompleteCoreStep_CanSubmitFalse()
    {
        var ct = TestContext.Current.CancellationToken;
        Setup(Base("matricula_inicial")); // sin vin ni comprador.

        var (result, _) = await _handler.HandleAsync(Guid.NewGuid(), Guid.NewGuid(), ct);

        result!.CanSubmit.Should().BeFalse();
    }
}
