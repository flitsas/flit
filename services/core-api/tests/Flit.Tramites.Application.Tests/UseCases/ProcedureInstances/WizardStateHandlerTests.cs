using Flit.Tramites.Application.UseCases.Consultations;
using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Enums;
using Flit.Tramites.Domain.Integration;
using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.Tramites.Catalog;
using Flit.Tramites.Domain.Tramites.ValueObjects;
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

    /// <summary>Stub de exigibilidad RNMC por OT (para probar el flag que muestra la fecha de expedición).</summary>
    private sealed class StubRnmcPolicy(bool required) : IRnmcRequirementPolicy
    {
        public Task<bool> IsRnmcRequiredAsync(
            Guid tenantId, Guid? transitOfficeId, CancellationToken cancellationToken = default) =>
            Task.FromResult(required);
    }

    /// <summary>Stub de restricciones con ajustes explícitos por kind (para el opt-in RNMC de la compañía).</summary>
    private sealed class StubRestrictionPolicy(params (string kind, bool enabled)[] settings) : IConsultationRestrictionPolicy
    {
        public Task<ConsultationRestrictions> GetAsync(
            Guid tenantId, Guid? transitOfficeId, CancellationToken cancellationToken = default) =>
            Task.FromResult(ConsultationRestrictions.FromSettings(
                settings.Select(s => new KeyValuePair<string, bool>(s.kind, s.enabled))));
    }

    /// <summary>
    /// CF-06 (HU #10881) — stub del override OT del documento de prenda. El comportamiento SNAPSHOT
    /// (AC2) se prueba aparte, en la implementación EF (Infraestructura); aquí solo se fija si "ya
    /// aplica" para poder probar el cableado en <see cref="GetWizardStateHandler"/> (canSubmit/blockers).
    /// </summary>
    private sealed class StubPrendaDocumentRequirementPolicy(bool required) : IPrendaDocumentRequirementPolicy
    {
        public Task<bool> IsRequiredAsync(
            Guid tenantId, Guid? transitOfficeId, DateTimeOffset procedureCreatedAt,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(required);
    }

    /// <summary>
    /// 2026-08-12 — la decisión de prenda vive en un agregado aparte, y desde el arreglo del override
    /// el wizard la necesita: sin ella no puede distinguir "constituyo prenda y me falta el soporte"
    /// de "no hay prenda que soportar". <c>null</c> ⇒ sin decisión registrada.
    /// </summary>
    private sealed class StubPrendaRepo(string? decision) : IProcedureInstancePrendaRepository
    {
        public Task<ProcedureInstancePrenda?> GetVigenteAsync(
            Guid procedureInstanceId, Guid tenantId, CancellationToken ct = default) =>
            Task.FromResult<ProcedureInstancePrenda?>(
                decision is null
                    ? null
                    : new ProcedureInstancePrenda { Decision = decision, Estado = PrendaEstado.Vigente });

        public Task<IReadOnlyList<ProcedureInstancePrenda>> ListByInstanceAsync(
            Guid procedureInstanceId, Guid tenantId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<ProcedureInstancePrenda>>([]);

        public Task AddAsync(ProcedureInstancePrenda prenda, CancellationToken ct = default) => Task.CompletedTask;

        public Task SaveChangesAsync(CancellationToken ct = default) => Task.CompletedTask;
    }

    // ── FEATURE 05 — RnmcEnabled controla la visibilidad de la fecha de expedición ──

    [Fact] // El OT exige RNMC → RnmcEnabled=true (el frontend muestra la fecha de expedición).
    public async Task Get_RnmcExigidoPorOt_RnmcEnabledTrue()
    {
        var ct = TestContext.Current.CancellationToken;
        var instance = Base("matricula_inicial");
        instance.Actors.Add(Actor("comprador"));
        Setup(instance);
        var handler = new GetWizardStateHandler(_repo, rnmcPolicy: new StubRnmcPolicy(required: true));

        var (result, _) = await handler.HandleAsync(Guid.NewGuid(), Guid.NewGuid(), ct);

        result!.RnmcEnabled.Should().BeTrue();
    }

    [Fact] // Default (OT no exige RNMC) → RnmcEnabled=false (la fecha de expedición se oculta).
    public async Task Get_RnmcNoExigido_RnmcEnabledFalse()
    {
        var ct = TestContext.Current.CancellationToken;
        var instance = Base("matricula_inicial");
        instance.Actors.Add(Actor("comprador"));
        Setup(instance);
        var handler = new GetWizardStateHandler(_repo, rnmcPolicy: new StubRnmcPolicy(required: false));

        var (result, _) = await handler.HandleAsync(Guid.NewGuid(), Guid.NewGuid(), ct);

        result!.RnmcEnabled.Should().BeFalse();
    }

    [Fact] // FEATURE 05 — la compañía activa RNMC (opt-in) → RnmcEnabled=true aunque el OT no lo exija.
    public async Task Get_RnmcActivadoPorCompania_RnmcEnabledTrue()
    {
        var ct = TestContext.Current.CancellationToken;
        var instance = Base("matricula_inicial");
        instance.Actors.Add(Actor("comprador"));
        Setup(instance);
        var handler = new GetWizardStateHandler(
            _repo,
            rnmcPolicy: new StubRnmcPolicy(required: false),
            restrictionPolicy: new StubRestrictionPolicy((ConsultationRestrictionKinds.Rnmc, true)));

        var (result, _) = await handler.HandleAsync(Guid.NewGuid(), Guid.NewGuid(), ct);

        result!.RnmcEnabled.Should().BeTrue();
    }

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
        // Pasos 1-3 completos (VIN, comprador+RUNT, docs) → Identidad (4) alcanzable y,
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

    // ── CF-06 (HU #10881) — override OT del documento de prenda ───────────────

    private static ProcedureInstance TraspasoListoParaRadicar()
    {
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
        instance.Attachments.Add(Attachment("fur"));
        return instance;
    }

    [Fact]
    public async Task Ac1_Get_Traspaso_OverrideOtPrendaActivo_SinDocumento_CanSubmitFalseConBlocker()
    {
        // AC1 — con el override activo, una decisión que SÍ pide soporte y sin el documento adjunto,
        // canSubmit refleja el bloqueo con el MISMO código que emite el gate de preparación por este
        // camino (prenda_documento_requerido_ot, propio del override desde 2026-08-12).
        var ct = TestContext.Current.CancellationToken;
        var instance = TraspasoListoParaRadicar();
        Setup(instance);
        var handler = new GetWizardStateHandler(
            _repo,
            prendaDocumentRequirementPolicy: new StubPrendaDocumentRequirementPolicy(required: true),
            prendaRepo: new StubPrendaRepo(PrendaDecision.Registrar));

        var (result, _) = await handler.HandleAsync(Guid.NewGuid(), Guid.NewGuid(), ct);

        result!.CanSubmit.Should().BeFalse();
        result.Blockers.Should().Contain(TramiteEstadoErrores.PrendaDocumentoRequeridoOt);
    }

    [Fact]
    public async Task Ac1_Get_Traspaso_OverrideOtPrendaActivo_ConDocumentoAdjunto_CanSubmitTrue()
    {
        var ct = TestContext.Current.CancellationToken;
        var instance = TraspasoListoParaRadicar();
        instance.Attachments.Add(Attachment("prenda_registro"));
        Setup(instance);
        var handler = new GetWizardStateHandler(
            _repo,
            prendaDocumentRequirementPolicy: new StubPrendaDocumentRequirementPolicy(required: true),
            prendaRepo: new StubPrendaRepo(PrendaDecision.Registrar));

        var (result, _) = await handler.HandleAsync(Guid.NewGuid(), Guid.NewGuid(), ct);

        result!.CanSubmit.Should().BeTrue();
        result.Blockers.Should().NotContain(TramiteEstadoErrores.PrendaDocumentoRequeridoOt);
    }

    [Fact]
    public async Task Get_Traspaso_OverrideOtPrendaInactivo_NoAgregaBlocker()
    {
        // Sin override (comportamiento previo / matrícula sin concepto de prenda): sin regresión.
        var ct = TestContext.Current.CancellationToken;
        var instance = TraspasoListoParaRadicar();
        Setup(instance);

        var (result, _) = await _handler.HandleAsync(Guid.NewGuid(), Guid.NewGuid(), ct);

        result!.CanSubmit.Should().BeTrue();
        result.Blockers.Should().NotContain(TramiteEstadoErrores.PrendaDocumentoRequeridoOt);
    }

    [Fact]
    public async Task Get_PrendaDocumentRequired_TrueCuandoPoliticaExige()
    {
        var ct = TestContext.Current.CancellationToken;
        var instance = TraspasoListoParaRadicar();
        Setup(instance);
        var handler = new GetWizardStateHandler(
            _repo, prendaDocumentRequirementPolicy: new StubPrendaDocumentRequirementPolicy(required: true));

        var (result, _) = await handler.HandleAsync(Guid.NewGuid(), Guid.NewGuid(), ct);

        result!.PrendaDocumentRequired.Should().BeTrue();
    }

    [Fact]
    public async Task Get_PrendaDocumentRequired_FalseCuandoOptOutCompañiaOt()
    {
        var ct = TestContext.Current.CancellationToken;
        var instance = TraspasoListoParaRadicar();
        instance.Attachments.Add(Attachment("prenda_registro"));
        Setup(instance);
        var handler = new GetWizardStateHandler(
            _repo, prendaDocumentRequirementPolicy: new StubPrendaDocumentRequirementPolicy(required: false));

        var (result, _) = await handler.HandleAsync(Guid.NewGuid(), Guid.NewGuid(), ct);

        result!.PrendaDocumentRequired.Should().BeFalse();
        result.Blockers.Should().NotContain(TramiteEstadoErrores.PrendaDocumentoRequeridoOt);
    }

    /// <summary>
    /// Matrícula inicial que CONSTITUYE prenda (vehículo nuevo financiado): el override sigue
    /// exigiendo el soporte aunque no haya gravamen previo que detectar. Es lo que el override aporta
    /// sobre <c>PrendaGate.Evaluate</c>, que solo actúa en traspaso con el semáforo en warn.
    /// </summary>
    [Fact]
    public async Task Get_Matricula_PoliticaPrendaObligatoria_DecisionQuePideSoporte_SinDocumento_Bloquea()
    {
        var ct = TestContext.Current.CancellationToken;
        var instance = Base("matricula_inicial");
        instance.TransitOfficeId = Guid.NewGuid();
        instance.Actors.Add(Actor("comprador"));
        Setup(instance);
        var handler = new GetWizardStateHandler(
            _repo,
            prendaDocumentRequirementPolicy: new StubPrendaDocumentRequirementPolicy(required: true),
            prendaRepo: new StubPrendaRepo(PrendaDecision.Registrar));

        var (result, _) = await handler.HandleAsync(Guid.NewGuid(), Guid.NewGuid(), ct);

        result!.PrendaDocumentRequired.Should().BeTrue();
        result.Blockers.Should().Contain(TramiteEstadoErrores.PrendaDocumentoRequeridoOt);
        result.CanSubmit.Should().BeFalse();
    }

    /// <summary>
    /// EL CASO REPORTADO (2026-08-12). Matrícula inicial con <c>sin_prenda</c> u <c>omitir</c> y el
    /// override del OT activo — que es su valor por DEFECTO mientras nadie configure el opt-out.
    ///
    /// <para>Antes bloqueaba: el trámite quedaba atascado en "Finalizar" pidiendo un adjunto que el
    /// paso de prenda no ofrece cargar para esas decisiones, sin ninguna vía para cumplirlo desde el
    /// trámite. Y el banner lo atribuía a "la decisión de prenda seleccionada", que era justo la que
    /// decía que no había prenda.</para>
    ///
    /// <para>La versión anterior de este test fijaba ese comportamiento (misma política, SIN decisión
    /// registrada, esperando el blocker), que es por lo que el defecto sobrevivió a la suite.</para>
    /// </summary>
    [Theory]
    [InlineData(PrendaDecision.SinPrenda)]
    [InlineData(PrendaDecision.Omitir)]
    public async Task Get_Matricula_PoliticaPrendaObligatoria_DecisionSinSoporte_NoBloquea(string decision)
    {
        var ct = TestContext.Current.CancellationToken;
        var instance = Base("matricula_inicial");
        instance.TransitOfficeId = Guid.NewGuid();
        instance.Actors.Add(Actor("comprador"));
        Setup(instance);
        var handler = new GetWizardStateHandler(
            _repo,
            prendaDocumentRequirementPolicy: new StubPrendaDocumentRequirementPolicy(required: true),
            prendaRepo: new StubPrendaRepo(decision));

        var (result, _) = await handler.HandleAsync(Guid.NewGuid(), Guid.NewGuid(), ct);

        result!.Blockers.Should().NotContain(
            TramiteEstadoErrores.PrendaDocumentoRequeridoOt,
            "con esta decisión no hay documento de prenda que cargar: exigirlo deja el trámite sin salida");
    }

    /// <summary>
    /// El hueco que abrió la corrección de arriba, cerrado por el otro lado. Matrícula inicial con el
    /// override activo y SIN decisión de prenda registrada: si el override callara, bastaría con no
    /// abrir el paso de prenda para radicar sin el certificado que el OT exige — y en matrícula no hay
    /// ningún otro gate que lo recoja (<c>PrendaGate.Evaluate</c> solo actúa en traspaso con warn).
    /// El código es el accionable: registra la decisión, que siempre está a mano.
    /// </summary>
    [Fact]
    public async Task Get_Matricula_PoliticaPrendaObligatoria_SinDecision_ExigeLaDecision()
    {
        var ct = TestContext.Current.CancellationToken;
        var instance = Base("matricula_inicial");
        instance.TransitOfficeId = Guid.NewGuid();
        instance.Actors.Add(Actor("comprador"));
        Setup(instance);
        var handler = new GetWizardStateHandler(
            _repo,
            prendaDocumentRequirementPolicy: new StubPrendaDocumentRequirementPolicy(required: true),
            prendaRepo: new StubPrendaRepo(decision: null));

        var (result, _) = await handler.HandleAsync(Guid.NewGuid(), Guid.NewGuid(), ct);

        result!.Blockers.Should().Contain(TramiteEstadoErrores.PrendaDecisionRequerida);
        result.CanSubmit.Should().BeFalse();
    }

    [Fact]
    public async Task Get_Matricula_PoliticaPrendaOpcional_SinDocumento_NoAgregaBlocker()
    {
        var ct = TestContext.Current.CancellationToken;
        var instance = Base("matricula_inicial");
        instance.TransitOfficeId = Guid.NewGuid();
        instance.Actors.Add(Actor("comprador"));
        Setup(instance);
        var handler = new GetWizardStateHandler(
            _repo, prendaDocumentRequirementPolicy: new StubPrendaDocumentRequirementPolicy(required: false));

        var (result, _) = await handler.HandleAsync(Guid.NewGuid(), Guid.NewGuid(), ct);

        result!.PrendaDocumentRequired.Should().BeFalse();
        result.Blockers.Should().NotContain(TramiteEstadoErrores.PrendaDocumentoRequeridoOt);
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
        Setup(instance);

        var (result, _) = await _handler.HandleAsync(Guid.NewGuid(), Guid.NewGuid(), ct);

        // HU #10935 — los actores van antes de documentos.
        // Paso 2 (vendedor) completo: parte completa + RUNT consultado (documento presente).
        result!.Steps.Single(s => s.Index == 2).Status.Should().Be("complete");
        // Paso 3 (comprador) incompleto: sin comprador.
        result.Steps.Single(s => s.Index == 3).Status.Should().Be("incomplete");
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
        CompletarDocsTraspaso(instance); // HU #10935: documentos (paso 4) tras los actores; habilita el 5.
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
    public async Task Get_PreflightRed_RiesgoAceptado_LiftsBlockerAndDocsStepCompletes()
    {
        // "Asumo el riesgo" (riesgo_aceptado=true en field_values) levanta el blocker de
        // preflight rojo subsanable: el paso de documentos (HU #10935: paso 3 en matrícula, tras el
        // comprador) deja de bloquearse por preflight y el submit ya no se veta por ese motivo.
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
        // Paso 3 (documentos) completo: preflight rojo ya no bloquea y los docs obligatorios están.
        result.Steps.Single(s => s.Index == 3).Status.Should().Be("complete");
        // Pasos 1-3 completos (vin, comprador, documentos); identidad aprobada; FUR diferido → submit habilitado.
        result.CanSubmit.Should().BeTrue();
    }

    [Fact]
    public async Task Get_PreflightProviderError_AddsHardBlocker_NotLiftedByRiesgo()
    {
        // Una consulta no verificable (check "error") es un bloqueo DURO: aunque el gestor acepte
        // el riesgo, el blocker preflight_provider_error se mantiene, el paso de documentos NO se
        // completa (HU #10935: paso 3 en matrícula) y el submit sigue vetado. Solo se levanta
        // reejecutando la consulta con éxito.
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
        result.Steps.Single(s => s.Index == 3).Status.Should().NotBe("complete");
        result.CanSubmit.Should().BeFalse();
    }

    [Fact]
    public async Task Get_Traspaso_PreflightRedPorSoat_NoBloqueaComprador_ConSimitMultas()
    {
        // Bug #10728: el paso de comprador (HU #10935: paso 3) NO debe bloquearse con simit_multas
        // cuando el preflight está en rojo por el VEHÍCULO (SOAT vencido) pero el check SIMIT del
        // comprador no es fail (aquí unknown: el preflight corrió antes de que el comprador tuviera
        // documento). El comprador va antes que documentos, así que su gate se evalúa aunque el
        // preflight esté rojo.
        var ct = TestContext.Current.CancellationToken;
        var instance = Base("traspaso", TramiteTipologiaCatalog.CodigoTraspasoStandard);
        instance.FieldValues.Add(new ProcedureInstanceFieldValue { FieldKey = "plate", ValueText = "ABC123", Source = "user" });
        instance.FieldValues.Add(new ProcedureInstanceFieldValue { FieldKey = "riesgo_aceptado", ValueText = "true", Source = "user" });
        instance.Actors.Add(Actor("vendedor", "555"));
        instance.Actors.Add(Actor("comprador", "666"));
        instance.PreflightSnapshots.Add(Preflight("red",
            "[{\"Key\":\"soat\",\"Label\":\"SOAT\",\"Status\":\"fail\",\"Source\":\"kyverum_runt\",\"Message\":\"SOAT vencido o no vigente\"}," +
            "{\"Key\":\"simit_comprador\",\"Label\":\"SIMIT comprador\",\"Status\":\"unknown\",\"Source\":\"verifik_simit\",\"Message\":\"Actor sin documento para consultar SIMIT\"}]"));
        CompletarDocsTraspaso(instance);
        Setup(instance);

        var (result, _) = await _handler.HandleAsync(Guid.NewGuid(), Guid.NewGuid(), ct);

        var pasoComprador = result!.Steps.Single(s => s.Index == 3);
        pasoComprador.Reasons.Should().NotContain("simit_multas");
        pasoComprador.Status.Should().Be("complete");
    }

    [Fact]
    public async Task Get_Traspaso_SimitCompradorFail_BloqueaConSimitMultas()
    {
        // Bug #10728 (contraparte): cuando el check SIMIT ESPECÍFICO del comprador es fail (comparendos
        // reales), el paso 4 SÍ debe bloquearse con simit_multas. Preserva la detección real de multas.
        // FEATURE 05 — este caso sobrevive como RETROCOMPATIBILIDAD: el mapper ya no emite "fail" para
        // multas (ahora es "warn"), pero los snapshots persistidos antes del cambio son JSON inmutable
        // y siguen diciendo "fail". El gate debe seguir leyéndolos bien.
        var ct = TestContext.Current.CancellationToken;
        var instance = Base("traspaso", TramiteTipologiaCatalog.CodigoTraspasoStandard);
        instance.FieldValues.Add(new ProcedureInstanceFieldValue { FieldKey = "plate", ValueText = "ABC123", Source = "user" });
        instance.FieldValues.Add(new ProcedureInstanceFieldValue { FieldKey = "riesgo_aceptado", ValueText = "true", Source = "user" });
        instance.Actors.Add(Actor("vendedor", "555"));
        instance.Actors.Add(Actor("comprador", "666"));
        instance.PreflightSnapshots.Add(Preflight("red",
            "[{\"Key\":\"simit_comprador_multas\",\"Label\":\"Comparendos SIMIT\",\"Status\":\"fail\",\"Source\":\"verifik_simit\",\"Message\":\"El comprador tiene comparendos\"}]"));
        CompletarDocsTraspaso(instance);
        Setup(instance);

        var (result, _) = await _handler.HandleAsync(Guid.NewGuid(), Guid.NewGuid(), ct);

        var pasoComprador = result!.Steps.Single(s => s.Index == 3);
        pasoComprador.Status.Should().Be("incomplete");
        pasoComprador.Reasons.Should().Contain("simit_multas");
    }

    [Fact]
    public async Task Get_Traspaso_SimitCompradorMultasWarn_SigueBloqueandoConSimitMultas()
    {
        // FEATURE 05 (AC2) — TEST DE AMARRE. Los comparendos pasaron de fail a warn para no bloquear
        // la CREACIÓN del trámite, pero la RADICACIÓN al OT conserva su gate: el paso 4 debe seguir
        // bloqueándose. Si este test cae, quitar el bloqueo de la creación se llevó por delante la
        // radicación en silencio — que es el modo de fallo principal de esta HU.
        var ct = TestContext.Current.CancellationToken;
        var instance = Base("traspaso", TramiteTipologiaCatalog.CodigoTraspasoStandard);
        instance.FieldValues.Add(new ProcedureInstanceFieldValue { FieldKey = "plate", ValueText = "ABC123", Source = "user" });
        instance.Actors.Add(Actor("vendedor", "555"));
        instance.Actors.Add(Actor("comprador", "666"));
        instance.PreflightSnapshots.Add(Preflight("yellow",
            "[{\"Key\":\"simit_comprador_multas\",\"Label\":\"Multas SIMIT\",\"Status\":\"warn\",\"Source\":\"verifik_simit\",\"Message\":\"2 multa(s) pendiente(s) por $500.000 COP\"}]"));
        CompletarDocsTraspaso(instance);
        Setup(instance);

        var (result, _) = await _handler.HandleAsync(Guid.NewGuid(), Guid.NewGuid(), ct);

        var pasoComprador = result!.Steps.Single(s => s.Index == 3);
        pasoComprador.Status.Should().Be("incomplete");
        pasoComprador.Reasons.Should().Contain("simit_multas");
    }

    [Fact]
    public async Task Get_Traspaso_SoloAcuerdoPagoWarn_NoBloqueaConSimitMultas()
    {
        // FEATURE 05 (AC3) — TEST DE AMARRE. simit_comprador_acuerdos_pago TAMBIÉN es warn y NO es un
        // comparendo. Sin el guard del sufijo "_multas" en SimitOf, todo comprador con un acuerdo de
        // pago activo dispararía un simit_multas falso y se le bloquearía la radicación sin motivo.
        var ct = TestContext.Current.CancellationToken;
        var instance = Base("traspaso", TramiteTipologiaCatalog.CodigoTraspasoStandard);
        instance.FieldValues.Add(new ProcedureInstanceFieldValue { FieldKey = "plate", ValueText = "ABC123", Source = "user" });
        instance.Actors.Add(Actor("vendedor", "555"));
        instance.Actors.Add(Actor("comprador", "666"));
        instance.PreflightSnapshots.Add(Preflight("yellow",
            "[{\"Key\":\"simit_comprador_multas\",\"Label\":\"Multas SIMIT\",\"Status\":\"ok\",\"Source\":\"verifik_simit\",\"Message\":null}," +
            "{\"Key\":\"simit_comprador_acuerdos_pago\",\"Label\":\"Acuerdos de pago SIMIT\",\"Status\":\"warn\",\"Source\":\"verifik_simit\",\"Message\":\"1 acuerdo(s) activo(s) por $837.716 COP\"}]"));
        CompletarDocsTraspaso(instance);
        Setup(instance);

        var (result, _) = await _handler.HandleAsync(Guid.NewGuid(), Guid.NewGuid(), ct);

        var pasoComprador = result!.Steps.Single(s => s.Index == 3);
        pasoComprador.Reasons.Should().NotContain("simit_multas");
        pasoComprador.Status.Should().Be("complete");
    }

    [Fact]
    public async Task Get_Traspaso_ComparendosWarn_NoAgregaBlockerPreflightRed()
    {
        // FEATURE 05 (AC1) — el criterio de negocio de la HU: con comparendos pendientes el pre-vuelo
        // queda en amarillo, no se agrega el bloqueo preflight_red y el gestor NO necesita marcar
        // "asumo el riesgo" para crear el trámite.
        var ct = TestContext.Current.CancellationToken;
        var instance = Base("traspaso", TramiteTipologiaCatalog.CodigoTraspasoStandard);
        instance.FieldValues.Add(new ProcedureInstanceFieldValue { FieldKey = "plate", ValueText = "ABC123", Source = "user" });
        instance.Actors.Add(Actor("vendedor", "555"));
        instance.Actors.Add(Actor("comprador", "666"));
        instance.PreflightSnapshots.Add(Preflight("yellow",
            "[{\"Key\":\"simit_comprador_multas\",\"Label\":\"Multas SIMIT\",\"Status\":\"warn\",\"Source\":\"verifik_simit\",\"Message\":\"2 multa(s) pendiente(s)\"}]"));
        CompletarDocsTraspaso(instance);
        Setup(instance);

        var (result, _) = await _handler.HandleAsync(Guid.NewGuid(), Guid.NewGuid(), ct);

        result!.Blockers.Should().NotContain("preflight_red");
        result.Blockers.Should().NotContain("preflight_provider_error");
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

        // Pasos 1-3 completos (vin, comprador+runt, documentos completos); FUR diferido → no cuenta.
        result!.Steps.Single(s => s.Index == 1).Status.Should().Be("complete");
        result.Steps.Single(s => s.Index == 2).Status.Should().Be("complete");
        result.Steps.Single(s => s.Index == 3).Status.Should().Be("complete");
        result.CanSubmit.Should().BeTrue();
    }

    // ── Gating ESTRICTO de documentos obligatorios (Slice 4a-fix) ─────────────

    [Fact]
    public async Task Get_Matricula_DocsIncompletos_DocsStepIncompleteAndBlocksSubmit()
    {
        var ct = TestContext.Current.CancellationToken;
        var instance = Base("matricula_inicial");
        instance.FieldValues.Add(new ProcedureInstanceFieldValue { FieldKey = "vin", ValueText = "1HGCM82633A004352", Source = "user" });
        instance.PreflightSnapshots.Add(Preflight("green"));
        instance.Actors.Add(Actor("comprador", "777"));
        // Sin adjuntos → faltan documentos obligatorios.
        Setup(instance);

        var (result, _) = await _handler.HandleAsync(Guid.NewGuid(), Guid.NewGuid(), ct);

        // HU #10935 — documentos es el paso 3 (después del comprador).
        var docs = result!.Steps.Single(s => s.Index == 3);
        docs.Status.Should().Be("incomplete");
        docs.Reasons.Should().Contain("documentos_incompletos");
        result.Blockers.Should().Contain("documentos_incompletos");
        result.CanSubmit.Should().BeFalse();
    }

    [Fact]
    public async Task Get_Traspaso_DocsIncompletos_DocsStepReasonAndGlobalBlocker()
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

        // HU #10935 — los documentos gobiernan el paso 4 (tras los actores): su reason vive ahí; el
        // blocker GLOBAL veta el submit.
        var docs = result!.Steps.Single(s => s.Index == 4);
        docs.Status.Should().Be("incomplete");
        docs.Reasons.Should().Contain("documentos_incompletos");
        result.Blockers.Should().Contain("documentos_incompletos");
        result.CanSubmit.Should().BeFalse();
    }

    // ── HU #10935 — documentos DESPUÉS de comprador/vendedor ──────────────────

    [Fact]
    public async Task Get_Traspaso_DocumentosStepIsAfterComprador()
    {
        var ct = TestContext.Current.CancellationToken;
        Setup(Base("traspaso"));

        var (result, _) = await _handler.HandleAsync(Guid.NewGuid(), Guid.NewGuid(), ct);

        var documentos = result!.Steps.Single(s => s.Key == "documentos");
        var comprador = result.Steps.Single(s => s.Key == "comprador");
        var vendedor = result.Steps.Single(s => s.Key == "vendedor");
        // Orden: consulta → vendedor → comprador → documentos → comercial → fur.
        documentos.Index.Should().Be(4);
        documentos.Index.Should().BeGreaterThan(comprador.Index);
        documentos.Index.Should().BeGreaterThan(vendedor.Index);
    }

    [Fact]
    public async Task Get_Matricula_DocumentosStepIsAfterComprador()
    {
        var ct = TestContext.Current.CancellationToken;
        Setup(Base("matricula_inicial"));

        var (result, _) = await _handler.HandleAsync(Guid.NewGuid(), Guid.NewGuid(), ct);

        var documentos = result!.Steps.Single(s => s.Key == "documentos");
        var comprador = result.Steps.Single(s => s.Key == "comprador");
        // Orden: consulta_vin → comprador → documentos → identidad → fur.
        documentos.Index.Should().Be(3);
        documentos.Index.Should().BeGreaterThan(comprador.Index);
    }

    [Fact]
    public async Task Get_Matricula_IncompleteCoreStep_CanSubmitFalse()
    {
        var ct = TestContext.Current.CancellationToken;
        Setup(Base("matricula_inicial")); // sin vin ni comprador.

        var (result, _) = await _handler.HandleAsync(Guid.NewGuid(), Guid.NewGuid(), ct);

        result!.CanSubmit.Should().BeFalse();
    }

    // ── HU #10645 (ADR-0025 §4) — baúl de firmas cuenta como identidad aprobada en el gate ──────

    [Fact]
    public async Task Get_Matricula_CompradorNitCubiertoPorBaul_IdentidadAprobadaSinBiometrica()
    {
        // Un comprador JURÍDICO (NIT) con firma de baúl activa+vigente cuenta como identidad APROBADA en
        // el gate (IdentityApprovalResolver ruta per-instancia): el paso 4 queda complete SIN fila biométrica
        // y el submit se habilita. Esto es lo que consumen SubmitGate y el gate del FUR.
        var ct = TestContext.Current.CancellationToken;
        var instance = Base("matricula_inicial");
        instance.FieldValues.Add(new ProcedureInstanceFieldValue { FieldKey = "vin", ValueText = "1HGCM82633A004352", Source = "user" });
        instance.PreflightSnapshots.Add(Preflight("green"));
        instance.Actors.Add(ActorNit("comprador"));
        CompletarDocsMatricula(instance);
        Setup(instance);

        var handler = new GetWizardStateHandler(_repo, vaultPolicy: new StubVaultPolicy(VaultMatch()));

        var (result, _) = await handler.HandleAsync(Guid.NewGuid(), Guid.NewGuid(), ct);

        result!.Steps.Single(s => s.Index == 4).Status.Should().Be("complete");
        result.Blockers.Should().NotContain(TramiteEstadoErrores.IdentidadNoAprobada);
        result.CanSubmit.Should().BeTrue();
    }

    [Fact]
    public async Task Get_Matricula_CompradorNitSinBaul_IdentidadNoAprobada()
    {
        // Control negativo: el MISMO comprador NIT sin baúl (política nula por defecto) NO queda aprobado
        // (no hay biométrica de una persona jurídica) → paso 4 incomplete y submit vetado. Prueba que es el
        // baúl —y no otra cosa— quien habilita la identidad en el test anterior.
        var ct = TestContext.Current.CancellationToken;
        var instance = Base("matricula_inicial");
        instance.FieldValues.Add(new ProcedureInstanceFieldValue { FieldKey = "vin", ValueText = "1HGCM82633A004352", Source = "user" });
        instance.PreflightSnapshots.Add(Preflight("green"));
        instance.Actors.Add(ActorNit("comprador"));
        CompletarDocsMatricula(instance);
        Setup(instance);

        var (result, _) = await _handler.HandleAsync(Guid.NewGuid(), Guid.NewGuid(), ct);

        result!.Steps.Single(s => s.Index == 4).Status.Should().Be("incomplete");
        result.CanSubmit.Should().BeFalse();
    }

    private static ProcedureInstanceActor ActorNit(string actorType, string nit = "900123456") =>
        new()
        {
            ActorType = actorType,
            DocumentType = "NIT",
            DocumentNumber = nit,
            FullName = "Renting SAS",
            Email = "renting@x.com",
        };

    private static SignatureVaultMatch VaultMatch() => new(
        Guid.NewGuid(), "Renting SAS", "sig-hash", "vault/firma.png", "art-sha",
        DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-30)),
        DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30)),
        "900123456");

    private sealed class StubVaultPolicy(SignatureVaultMatch? match) : ISignatureVaultPolicy
    {
        public Task<SignatureVaultMatch?> ResolveAsync(
            Guid tenantId, string documentType, string documentNumber, CancellationToken cancellationToken = default)
            => Task.FromResult(match);
    }
}
