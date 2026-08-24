using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Application.UseCases.ProcedureInstances.Estados;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Enums;
using Flit.Tramites.Domain.Integration;
using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.Tramites.Catalog;
using Flit.Tramites.Domain.Tramites.Estados;
using Flit.Tramites.Domain.Tramites.ValueObjects;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Tramites.Application.Tests.UseCases.ProcedureInstances.Estados;

/// <summary>
/// Servicio único de ciclo de vida (N 03, ADR-0022): máquina RF02, finales RF04, gate de
/// preparación RF03 con causa exacta, motivo obligatorio RF05, y atomicidad RNF01 (recorder y
/// publisher exactamente una vez por transición exitosa; cero en fallo; conflicto → 409).
/// </summary>
public sealed class TramiteLifecycleServiceTests
{
    private readonly IProcedureInstanceRepository _repo = Substitute.For<IProcedureInstanceRepository>();
    private readonly IProcedureTypeRepository _typeRepo = Substitute.For<IProcedureTypeRepository>();
    private readonly ITransitOfficeGrantGate _grantGate = Substitute.For<ITransitOfficeGrantGate>();
    private readonly IOtOperabilityGate _operabilityGate = Substitute.For<IOtOperabilityGate>();
    private readonly IPrendaDocumentRequirementPolicy _prendaPolicy = Substitute.For<IPrendaDocumentRequirementPolicy>();
    private readonly RecordingTransitionRecorder _recorder = new();
    private readonly RecordingTransitionPublisher _publisher = new();
    private readonly TramiteLifecycleService _sut;

    public TramiteLifecycleServiceTests()
    {
        _grantGate
            .IsEnabledForTenantAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(true);
        _operabilityGate
            .IsOperableAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(true);
        _repo.SaveChangesWithConcurrencyGuardAsync(Arg.Any<CancellationToken>()).Returns(true);
        // 2026-08-12 — el override del OT ya no exige documento con CUALQUIER decisión, solo con las
        // que lo piden (solicitar/registrar/levantar), así que estos tests necesitan una decisión
        // vigente para ejercitarlo. Por defecto `registrar`; los casos que prueban lo contrario
        // construyen su propio servicio con otra decisión.
        _sut = new TramiteLifecycleService(
            _repo, _typeRepo, _grantGate, _operabilityGate, NullOtRuleGate.Instance, _recorder, _publisher,
            prendaDocumentRequirementPolicy: _prendaPolicy,
            prendaRepo: StubPrendaRepo(PrendaDecision.Registrar));
    }

    private ProcedureInstance Wire(string status, bool conGates = false)
    {
        var id = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var i = new ProcedureInstance
        {
            ProcedureType = ProcedureTypeFixture.For(TramiteTipologiaCatalog.CodigoMatriculaInicial ?? "matricula_inicial"),
            Id = id,
            TenantId = tenantId,
            ProcedureTypeId = Guid.NewGuid(),
            ReferenceNumber = "TRM-2026-000001",
            Status = status,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        if (conGates)
        {
            foreach (var t in new[] { "factura", "aduana", "impronta" })
            {
                i.Attachments.Add(new ProcedureInstanceAttachment
                {
                    Id = Guid.NewGuid(),
                    TenantId = tenantId,
                    ProcedureInstanceId = id,
                    Tipo = t,
                    StoragePath = $"p/{t}",
                    UploadedAt = DateTimeOffset.UtcNow,
                });
            }
            i.BiometricValidations.Add(new ProcedureInstanceBiometricValidation
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                ProcedureInstanceId = id,
                PartyRole = "comprador",
                Status = BiometricEstados.Aprobado,
                Name = "X",
                DocumentType = "CC",
                DocumentNumber = "1",
                Email = "x@y.com",
                TokenHash = Guid.NewGuid().ToString("N"),
                ExpiresAt = DateTimeOffset.UtcNow.AddHours(1),
                CreatedAt = DateTimeOffset.UtcNow,
            });
        }
        _repo.GetByIdWithWizardGraphAsync(id, tenantId, Arg.Any<CancellationToken>()).Returns(i);
        _typeRepo.GetByIdAsync(i.ProcedureTypeId, Arg.Any<CancellationToken>()).Returns(new ProcedureType
        {
            Id = i.ProcedureTypeId,
            Code = "X",
            Name = "X",
            Family = "matriculas",
            PublicationStatus = PublicationStatus.Published,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        return i;
    }

    /// <summary>Repo de prenda que siempre devuelve la misma decisión vigente (o ninguna si es null).</summary>
    private static IProcedureInstancePrendaRepository StubPrendaRepo(string? decision)
    {
        var repo = Substitute.For<IProcedureInstancePrendaRepository>();
        repo.GetVigenteAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(decision is null
                ? null
                : new ProcedureInstancePrenda { Decision = decision, Estado = PrendaEstado.Vigente });
        return repo;
    }

    private Task<TramiteTransitionOutcome> Transition(
        ProcedureInstance i, string to, string? reason = null, Guid? changedBy = null) =>
        _sut.TransitionAsync(
            new TramiteTransitionCommand(i.Id, i.TenantId, to, reason, changedBy),
            TestContext.Current.CancellationToken);

    [Fact]
    public async Task NotFound_CuandoLaInstanciaNoExiste()
    {
        var outcome = await _sut.TransitionAsync(
            new TramiteTransitionCommand(Guid.NewGuid(), Guid.NewGuid(), TramiteEstado.Anulado, "x", null),
            TestContext.Current.CancellationToken);

        outcome.Success.Should().BeFalse();
        outcome.ErrorCode.Should().Be(TramiteEstadoErrores.NoEncontrado);
    }

    [Fact]
    public async Task EstadoDesconocido_RechazaVocabularioViejo()
    {
        var i = Wire(TramiteEstado.Borrador);

        var outcome = await Transition(i, "submitted");

        outcome.ErrorCode.Should().Be(TramiteEstadoErrores.EstadoDesconocido);
        i.Status.Should().Be(TramiteEstado.Borrador);
    }

    [Theory]
    [InlineData("aprobado")]
    [InlineData("anulado")]
    public async Task EstadoFinal_NoAdmiteNingunaTransicion(string estadoFinal)
    {
        var i = Wire(estadoFinal);

        foreach (var destino in TramiteEstado.Todos)
        {
            var outcome = await Transition(i, destino, reason: "x");
            outcome.ErrorCode.Should().Be(TramiteEstadoErrores.EstadoFinal, $"destino {destino}");
        }

        i.Status.Should().Be(estadoFinal);
        _recorder.Records.Should().BeEmpty();
        _publisher.Published.Should().BeEmpty();
    }

    [Fact]
    public async Task TransicionNoPermitida_ConDetalleDesdeHacia()
    {
        var i = Wire(TramiteEstado.Borrador);

        var outcome = await Transition(i, TramiteEstado.Aprobado);

        outcome.ErrorCode.Should().Be(TramiteEstadoErrores.TransicionNoPermitida);
        outcome.ErrorDetail.Should().Contain("borrador").And.Contain("aprobado");
    }

    [Theory]
    [InlineData("anulado")]
    [InlineData("rechazado")]
    public async Task MotivoRequerido_ParaAnularYRechazar(string destino)
    {
        // borrador→anulado y entregado→rechazado son válidas en máquina; sin motivo → error RF05.
        var from = destino == "anulado" ? TramiteEstado.Borrador : TramiteEstado.Entregado;
        var i = Wire(from);

        var sinMotivo = await Transition(i, destino, reason: "  ");
        sinMotivo.ErrorCode.Should().Be(TramiteEstadoErrores.MotivoRequerido);

        var conMotivo = await Transition(i, destino, reason: "Motivo de negocio");
        conMotivo.Success.Should().BeTrue();
    }

    // ── CF-03 (HU #10877): precondición registral, SEGUNDO momento (radicar) ────────────────────

    private static void ConVin(ProcedureInstance instance, string vin) =>
        instance.FieldValues.Add(new ProcedureInstanceFieldValue
        {
            Id = Guid.NewGuid(),
            TenantId = instance.TenantId,
            ProcedureInstanceId = instance.Id,
            FieldKey = "vin",
            ValueText = vin,
            Source = "user",
        });

    [Fact]
    public async Task Radicar_VinConMatriculaAprobadaEnFlit_BloqueaConVehicleStateInvalid()
    {
        // CF-03 (AC2) — el estado registral pudo cambiar ENTRE el preflight y la radicación: si otro
        // trámite del mismo VIN llegó a 'aprobado' mientras este seguía en curso, el gate de
        // preparación lo atrapa antes de avanzar (no solo el preflight).
        var i = Wire(TramiteEstado.Borrador, conGates: true);
        ConVin(i, "1HGCM82633A004352");
        _repo.FindTramitesByVinAsync(i.TenantId, "1HGCM82633A004352", i.Id, Arg.Any<CancellationToken>())
            .Returns(new List<VinTramiteExistente>
            {
                new(Guid.NewGuid(), TramiteEstado.Aprobado, Paso: 5, Placa: null, Vin: "1HGCM82633A004352"),
            });

        var outcome = await Transition(i, TramiteEstado.Preparado);

        outcome.Success.Should().BeFalse();
        outcome.ErrorCode.Should().Be(VehicleStatePolicy.ErrorCode);
        i.Status.Should().Be(TramiteEstado.Borrador);
        _recorder.Records.Should().BeEmpty();
    }

    [Fact]
    public async Task Radicar_VinSinMatriculaAprobada_Permite()
    {
        // Sin conflicto registral (sin previas, o previa no aprobada): el gate CF-03 no bloquea.
        var i = Wire(TramiteEstado.Borrador, conGates: true);
        ConVin(i, "1HGCM82633A004352");
        _repo.FindTramitesByVinAsync(i.TenantId, "1HGCM82633A004352", i.Id, Arg.Any<CancellationToken>())
            .Returns(new List<VinTramiteExistente>());

        var outcome = await Transition(i, TramiteEstado.Preparado);

        outcome.Success.Should().BeTrue();
        i.Status.Should().Be(TramiteEstado.Preparado);
    }

    [Fact]
    public async Task Radicar_SinVinPersistido_NoConsultaRepoYPermite()
    {
        // Instancias sin VIN (p. ej. otras familias, o tests que no lo ejercitan): el gate CF-03 es
        // no-op y NO dispara IO contra el repo (guarda por VinNormalizer.Normalize(null) == null).
        var i = Wire(TramiteEstado.Borrador, conGates: true);

        var outcome = await Transition(i, TramiteEstado.Preparado);

        outcome.Success.Should().BeTrue();
        await _repo.DidNotReceive().FindTramitesByVinAsync(
            Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// HU #10970 — mismo servicio pero con el modo de CF-03 configurado por ambiente, para verificar
    /// que fuera de <c>block</c> el gate de radicación deja pasar la transición.
    /// </summary>
    private TramiteLifecycleService SutConModoRegistral(TramiteValidationMode modo) =>
        new(_repo, _typeRepo, _grantGate, _operabilityGate, NullOtRuleGate.Instance, _recorder, _publisher,
            prendaDocumentRequirementPolicy: _prendaPolicy,
            validationPolicy: new TramiteValidationPolicy(TramiteValidationMode.Block, modo));

    [Theory]
    [InlineData(TramiteValidationMode.Warn)]
    [InlineData(TramiteValidationMode.Off)]
    public async Task Radicar_VinConMatriculaAprobada_ModoNoBlock_NoBloquea(TramiteValidationMode modo)
    {
        // HU #10970 (AC5) — en warn/off el gate registral no corta la radicación. Warn y off se
        // comportan igual aquí: una transición se permite o no, no hay semáforo donde dejar el aviso.
        var i = Wire(TramiteEstado.Borrador, conGates: true);
        ConVin(i, "1HGCM82633A004352");
        _repo.FindTramitesByVinAsync(i.TenantId, "1HGCM82633A004352", i.Id, Arg.Any<CancellationToken>())
            .Returns(new List<VinTramiteExistente>
            {
                new(Guid.NewGuid(), TramiteEstado.Aprobado, Paso: 5, Placa: null, Vin: "1HGCM82633A004352"),
            });

        var outcome = await SutConModoRegistral(modo).TransitionAsync(
            new TramiteTransitionCommand(i.Id, i.TenantId, TramiteEstado.Preparado, null, null),
            TestContext.Current.CancellationToken);

        outcome.Success.Should().BeTrue();
        outcome.ErrorCode.Should().NotBe(VehicleStatePolicy.ErrorCode);
        i.Status.Should().Be(TramiteEstado.Preparado);
    }

    [Fact]
    public async Task Radicar_ModoRegistralOff_NoConsultaElRepoPorVin()
    {
        // off no evalúa: ni siquiera se paga el viaje a BD para releer los trámites del VIN.
        var i = Wire(TramiteEstado.Borrador, conGates: true);
        ConVin(i, "1HGCM82633A004352");

        var outcome = await SutConModoRegistral(TramiteValidationMode.Off).TransitionAsync(
            new TramiteTransitionCommand(i.Id, i.TenantId, TramiteEstado.Preparado, null, null),
            TestContext.Current.CancellationToken);

        outcome.Success.Should().BeTrue();
        await _repo.DidNotReceive().FindTramitesByVinAsync(
            Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GatePreparacion_IdentidadNoAprobada_ConCausaExacta()
    {
        var i = Wire(TramiteEstado.Borrador, conGates: true);
        i.BiometricValidations.Clear();

        var outcome = await Transition(i, TramiteEstado.Preparado);

        outcome.ErrorCode.Should().Be(TramiteEstadoErrores.IdentidadNoAprobada);
        outcome.ErrorDetail.Should().Contain("identidad");
        i.Status.Should().Be(TramiteEstado.Borrador);
    }

    [Fact]
    public async Task GatePreparacion_DocumentosIncompletos_ConCausaExacta()
    {
        var i = Wire(TramiteEstado.Borrador, conGates: true);
        i.Attachments.Clear();

        var outcome = await Transition(i, TramiteEstado.Preparado);

        outcome.ErrorCode.Should().Be(TramiteEstadoErrores.DocumentosIncompletos);
        outcome.ErrorDetail.Should().Contain("documentos");
    }

    [Fact]
    public async Task TransicionExitosa_RegistraYPublicaExactamenteUnaVez()
    {
        var i = Wire(TramiteEstado.Borrador, conGates: true);
        var userId = Guid.NewGuid();

        var outcome = await Transition(i, TramiteEstado.Preparado, changedBy: userId);

        outcome.Success.Should().BeTrue();
        i.Status.Should().Be(TramiteEstado.Preparado);
        _recorder.Records.Should().ContainSingle(r =>
            r.FromStatus == TramiteEstado.Borrador
            && r.ToStatus == TramiteEstado.Preparado
            && r.ChangedByUserId == userId
            && r.ProcedureInstanceId == i.Id
            && r.TenantId == i.TenantId);
        _publisher.Published.Should().ContainSingle(r => r.ToStatus == TramiteEstado.Preparado);
        await _repo.Received(1).SaveChangesWithConcurrencyGuardAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TransicionFallida_NoRegistraNiPublica()
    {
        var i = Wire(TramiteEstado.Borrador);

        await Transition(i, TramiteEstado.Entregado); // inválida por máquina

        _recorder.Records.Should().BeEmpty();
        _publisher.Published.Should().BeEmpty();
        await _repo.DidNotReceive().SaveChangesWithConcurrencyGuardAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ConflictoConcurrencia_Devuelve409SinEfectosParciales()
    {
        var i = Wire(TramiteEstado.Borrador, conGates: true);
        _repo.SaveChangesWithConcurrencyGuardAsync(Arg.Any<CancellationToken>()).Returns(false);

        var outcome = await Transition(i, TramiteEstado.Preparado);

        outcome.Success.Should().BeFalse();
        outcome.ErrorCode.Should().Be(TramiteEstadoErrores.ConflictoConcurrencia);
        outcome.Instance.Should().BeNull();
    }

    [Fact]
    public async Task EntregadoSellaSubmittedAt()
    {
        var i = Wire(TramiteEstado.Preparado);

        var outcome = await Transition(i, TramiteEstado.Entregado);

        outcome.Success.Should().BeTrue();
        i.SubmittedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task TransicionExitosa_InvalidaConsolidadoMaestro()
    {
        // Feature #10701 — un cambio de estado baja la marca de vigencia del consolidado maestro
        // para que el próximo "Ver consolidado" lo regenere reflejando el nuevo estado del expediente.
        var i = Wire(TramiteEstado.Preparado);
        i.ConsolidadoMaestroVigente = true;

        var outcome = await Transition(i, TramiteEstado.Entregado);

        outcome.Success.Should().BeTrue();
        i.ConsolidadoMaestroVigente.Should().BeFalse();
    }

    [Fact]
    public async Task RechazadoVuelveABorradorParaSubsanar()
    {
        var i = Wire(TramiteEstado.Rechazado);

        var outcome = await Transition(i, TramiteEstado.Borrador);

        outcome.Success.Should().BeTrue();
        i.Status.Should().Be(TramiteEstado.Borrador);
    }

    // ── Subsanación por flag (rechazado + subsanacion_activa → entregado) ─────

    [Fact]
    public async Task Subsanacion_NoEsTransicionDeEstado_DesdeEntregadoORechazado()
    {
        foreach (var desde in new[] { TramiteEstado.Entregado, TramiteEstado.Rechazado })
        {
            var i = Wire(desde);
            var outcome = await Transition(i, TramiteEstado.Subsanacion);
            outcome.Success.Should().BeFalse();
            outcome.ErrorCode.Should().Be(TramiteEstadoErrores.EstadoDesconocido);
            i.Status.Should().Be(desde);
        }
    }

    [Fact]
    public async Task Ac2_RechazadoConFlag_ReRadicaAEntregado_YApagaFlag()
    {
        var i = Wire(TramiteEstado.Rechazado);
        i.SubsanacionActiva = true;
        i.SubsanacionCount = 1;

        var reRadicado = await Transition(i, TramiteEstado.Entregado);

        reRadicado.Success.Should().BeTrue();
        i.Status.Should().Be(TramiteEstado.Entregado);
        i.SubsanacionActiva.Should().BeFalse();
        _recorder.Records.Should().ContainSingle(r =>
            r.FromStatus == TramiteEstado.Rechazado && r.ToStatus == TramiteEstado.Entregado);
    }

    [Fact]
    public async Task RechazadoSinFlag_NoPuedePasarAEntregado()
    {
        var i = Wire(TramiteEstado.Rechazado);
        i.SubsanacionActiva = false;

        var outcome = await Transition(i, TramiteEstado.Entregado);

        outcome.Success.Should().BeFalse();
        outcome.ErrorCode.Should().Be(TramiteEstadoErrores.TransicionNoPermitida);
        i.Status.Should().Be(TramiteEstado.Rechazado);
    }

    [Fact]
    public async Task Ac1_Metadata_SePasaVerbatimAlRecorder()
    {
        var i = Wire(TramiteEstado.Rechazado);
        i.SubsanacionActiva = true;
        const string metadata = "{\"motivo\":\"x\",\"items\":[{\"campo\":\"factura\",\"detalle\":\"falta\"}]}";

        var outcome = await _sut.TransitionAsync(
            new TramiteTransitionCommand(
                i.Id, i.TenantId, TramiteEstado.Entregado, "x", null, Metadata: metadata),
            TestContext.Current.CancellationToken);

        outcome.Success.Should().BeTrue();
        _recorder.Records.Should().ContainSingle(r => r.Metadata == metadata);
    }

    [Fact]
    public async Task Metadata_PorDefecto_EsNull()
    {
        var i = Wire(TramiteEstado.Rechazado);
        i.SubsanacionActiva = true;

        var outcome = await Transition(i, TramiteEstado.Entregado);

        outcome.Success.Should().BeTrue();
        _recorder.Records.Should().ContainSingle(r => r.Metadata == null);
    }

    [Fact]
    public async Task RechazadoConFlag_PuedeAnularOVolverABorrador()
    {
        var i = Wire(TramiteEstado.Rechazado);
        i.SubsanacionActiva = true;

        var aBorrador = await Transition(i, TramiteEstado.Borrador);
        aBorrador.Success.Should().BeTrue();
        i.SubsanacionActiva.Should().BeFalse();
    }

    /// <summary>
    /// HU #11200 (AC4) — adelantar la comprobación del organismo al paso 1 NO la retira de la
    /// radicación. Entre una cosa y la otra pueden pasar días: el trámite queda en borrador y el
    /// administrador puede revocar el grant. Sin esta segunda comprobación, un trámite que pasó el paso
    /// 1 se entregaría a un organismo que ya no lo recibe y no aparecería en ninguna bandeja.
    /// </summary>
    [Fact]
    public async Task HU11200_AC4_Entrega_GrantRevocadoDespuesDelPaso1_SigueBloqueando()
    {
        var i = Wire(TramiteEstado.Preparado);
        SeleccionarOt(i, Guid.NewGuid());
        _grantGate
            .IsEnabledForTenantAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(false);

        var outcome = await Transition(i, TramiteEstado.Entregado);

        outcome.Success.Should().BeFalse();
        outcome.ErrorCode.Should().Be(TramiteEstadoErrores.OrganismoNoHabilitado);
        i.Status.Should().Be(TramiteEstado.Preparado);
        _publisher.Published.Should().BeEmpty();
    }

    // HU #10518 — enforcement runtime: con grant, el OT debe estar OPERATIVO para entregar.
    [Fact]
    public async Task Entrega_OtConGrantPeroNoOperable_BloqueaConOrganismoNoOperable()
    {
        var i = Wire(TramiteEstado.Preparado);
        SeleccionarOt(i, Guid.NewGuid());
        // Grant vigente (default true), pero el OT está inactivo a nivel plataforma.
        _operabilityGate
            .IsOperableAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(false);

        var outcome = await Transition(i, TramiteEstado.Entregado);

        outcome.Success.Should().BeFalse();
        outcome.ErrorCode.Should().Be("organismo_no_operable");
        i.Status.Should().Be(TramiteEstado.Preparado);
        _recorder.Records.Should().BeEmpty();
        _publisher.Published.Should().BeEmpty();
    }

    [Fact]
    public async Task Entrega_OtConGrantYOperable_Permite()
    {
        var officeId = Guid.NewGuid();
        var i = Wire(TramiteEstado.Preparado);
        SeleccionarOt(i, officeId);
        // Grant vigente + OT operativo (ambos gates pasan).

        var outcome = await Transition(i, TramiteEstado.Entregado);

        outcome.Success.Should().BeTrue();
        i.Status.Should().Be(TramiteEstado.Entregado);
        i.TransitOfficeId.Should().Be(officeId); // se promueve el id elegido
        await _operabilityGate.Received(1).IsOperableAsync(officeId, Arg.Any<CancellationToken>());
    }

    // HU #10604 (R19) — RNMC no es bloqueante: una medida correctiva pendiente NO veta el envío al OT.
    [Fact]
    public async Task Entrega_RnmcMedidaPendiente_NoBloqueaEnvio()
    {
        var i = Wire(TramiteEstado.Preparado);
        ConSenalRnmcMedida(i); // el preflight detectó una medida correctiva (informativa)

        var outcome = await Transition(i, TramiteEstado.Entregado);

        outcome.Success.Should().BeTrue();
        i.Status.Should().Be(TramiteEstado.Entregado); // se envía pese a la medida
    }

    private static void SeleccionarOt(ProcedureInstance instance, Guid officeId) =>
        instance.FieldValues.Add(new ProcedureInstanceFieldValue
        {
            Id = Guid.NewGuid(),
            TenantId = instance.TenantId,
            ProcedureInstanceId = instance.Id,
            FieldKey = "transit_office_id",
            ValueText = officeId.ToString(),
            Source = "user",
        });

    private static void ConSenalRnmcMedida(ProcedureInstance instance) =>
        instance.FieldValues.Add(new ProcedureInstanceFieldValue
        {
            Id = Guid.NewGuid(),
            TenantId = instance.TenantId,
            ProcedureInstanceId = instance.Id,
            FieldKey = "rnmc_medida_pendiente",
            ValueText = "true",
            Source = "system",
        });

    // ── CF-06 (HU #10881) — override OT del documento de prenda, independiente del gravamen ─────
    // La resolución del comportamiento SNAPSHOT (AC2: solo overrides ya activos al crear el trámite)
    // vive en IPrendaDocumentRequirementPolicy (Infraestructura, probada aparte con EF); aquí se
    // prueba que TramiteLifecycleService respeta EXACTAMENTE lo que la política resuelve.

    private ProcedureInstance WireTraspaso(
        string status, Guid transitOfficeId, DateTimeOffset createdAt, bool conDocumentoPrenda = false)
    {
        var id = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var i = new ProcedureInstance
        {
            ProcedureType = ProcedureTypeFixture.For(TramiteTipologiaCatalog.CodigoTraspasoStandard ?? "traspaso"),
            Id = id,
            TenantId = tenantId,
            ProcedureTypeId = Guid.NewGuid(),
            ReferenceNumber = "TRM-2026-000900",
            Status = status,
            TransitOfficeId = transitOfficeId,
            CreatedAt = createdAt,
        };

        foreach (var t in new[] { "compraventa", "soat", "rtm", "paz_salvo", "cedulas", "fur", "impronta" })
        {
            i.Attachments.Add(new ProcedureInstanceAttachment
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                ProcedureInstanceId = id,
                Tipo = t,
                StoragePath = $"p/{t}",
                UploadedAt = DateTimeOffset.UtcNow,
            });
        }

        if (conDocumentoPrenda)
        {
            i.Attachments.Add(new ProcedureInstanceAttachment
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                ProcedureInstanceId = id,
                Tipo = "prenda_registro",
                StoragePath = "p/prenda_registro",
                UploadedAt = DateTimeOffset.UtcNow,
            });
        }

        foreach (var parte in new[] { "comprador", "vendedor" })
        {
            i.BiometricValidations.Add(new ProcedureInstanceBiometricValidation
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                ProcedureInstanceId = id,
                PartyRole = parte,
                Status = BiometricEstados.Aprobado,
                Name = parte,
                DocumentType = "CC",
                DocumentNumber = parte == "comprador" ? "1" : "2",
                Email = $"{parte}@x.com",
                TokenHash = Guid.NewGuid().ToString("N"),
                ExpiresAt = DateTimeOffset.UtcNow.AddHours(1),
                CreatedAt = DateTimeOffset.UtcNow,
            });
        }

        i.FieldValues.Add(new ProcedureInstanceFieldValue
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ProcedureInstanceId = id,
            FieldKey = "transit_office_code",
            ValueText = "11001000",
            Source = "user",
        });

        _repo.GetByIdWithWizardGraphAsync(id, tenantId, Arg.Any<CancellationToken>()).Returns(i);
        _typeRepo.GetByIdAsync(i.ProcedureTypeId, Arg.Any<CancellationToken>()).Returns(new ProcedureType
        {
            Id = i.ProcedureTypeId,
            Code = "TRASPASO_STANDARD",
            Name = "Traspaso estándar",
            Family = "traspaso",
            PublicationStatus = PublicationStatus.Published,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        return i;
    }

    [Fact]
    public async Task Ac1_OverrideOtActivo_SinDocumentoDePrenda_BloqueaConCodigoExacto()
    {
        var otId = Guid.NewGuid();
        var i = WireTraspaso(TramiteEstado.Borrador, otId, DateTimeOffset.UtcNow, conDocumentoPrenda: false);
        _prendaPolicy
            .IsRequiredAsync(i.TenantId, otId, i.CreatedAt, Arg.Any<CancellationToken>())
            .Returns(true);

        var outcome = await Transition(i, TramiteEstado.Preparado);

        outcome.Success.Should().BeFalse();
        outcome.ErrorCode.Should().Be(TramiteEstadoErrores.PrendaDocumentoRequeridoOt);
        i.Status.Should().Be(TramiteEstado.Borrador);
        _recorder.Records.Should().BeEmpty();
    }

    /// <summary>
    /// EL CASO REPORTADO (2026-08-12) en el gate de preparación, hermano del que cubre
    /// <c>WizardStateHandlerTests</c>: con <c>sin_prenda</c> u <c>omitir</c> no hay documento que
    /// adjuntar —el paso de prenda ni siquiera lo ofrece—, así que el override del OT no puede
    /// exigirlo. Antes bloqueaba, y el trámite se quedaba sin salida.
    /// </summary>
    [Theory]
    [InlineData(PrendaDecision.SinPrenda)]
    [InlineData(PrendaDecision.Omitir)]
    public async Task OverrideOtActivo_ConDecisionSinSoporte_NoBloquea(string decision)
    {
        var otId = Guid.NewGuid();
        var i = WireTraspaso(TramiteEstado.Borrador, otId, DateTimeOffset.UtcNow, conDocumentoPrenda: false);
        _prendaPolicy
            .IsRequiredAsync(i.TenantId, otId, i.CreatedAt, Arg.Any<CancellationToken>())
            .Returns(true);
        var sut = new TramiteLifecycleService(
            _repo, _typeRepo, _grantGate, _operabilityGate, NullOtRuleGate.Instance, _recorder, _publisher,
            prendaDocumentRequirementPolicy: _prendaPolicy,
            prendaRepo: StubPrendaRepo(decision));

        var outcome = await sut.TransitionAsync(
            new TramiteTransitionCommand(i.Id, i.TenantId, TramiteEstado.Preparado, null, null),
            TestContext.Current.CancellationToken);

        outcome.ErrorCode.Should().NotBe(TramiteEstadoErrores.PrendaDocumentoRequeridoOt);
        outcome.ErrorCode.Should().NotBe(TramiteEstadoErrores.PrendaDocumentoRequerido);
    }

    /// <summary>
    /// Hermano del anterior por el otro lado: con el override activo y SIN decisión registrada, el
    /// trámite NO pasa a preparado. Callar aquí convertía "no abrir el paso de prenda" en la vía para
    /// radicar sin el certificado que el OT exige. El código es el accionable (registra la decisión),
    /// no el documento imposible que motivó la corrección de la tanda.
    /// </summary>
    [Fact]
    public async Task OverrideOtActivo_SinDecisionRegistrada_ExigeLaDecision()
    {
        var otId = Guid.NewGuid();
        var i = WireTraspaso(TramiteEstado.Borrador, otId, DateTimeOffset.UtcNow, conDocumentoPrenda: false);
        _prendaPolicy
            .IsRequiredAsync(i.TenantId, otId, i.CreatedAt, Arg.Any<CancellationToken>())
            .Returns(true);
        var sut = new TramiteLifecycleService(
            _repo, _typeRepo, _grantGate, _operabilityGate, NullOtRuleGate.Instance, _recorder, _publisher,
            prendaDocumentRequirementPolicy: _prendaPolicy,
            prendaRepo: StubPrendaRepo(decision: null));

        var outcome = await sut.TransitionAsync(
            new TramiteTransitionCommand(i.Id, i.TenantId, TramiteEstado.Preparado, null, null),
            TestContext.Current.CancellationToken);

        outcome.Success.Should().BeFalse();
        outcome.ErrorCode.Should().Be(TramiteEstadoErrores.PrendaDecisionRequerida);
        i.Status.Should().Be(TramiteEstado.Borrador);
    }

    [Fact]
    public async Task Ac1_OverrideOtActivo_ConDocumentoDePrendaAdjunto_Permite()
    {
        var otId = Guid.NewGuid();
        var i = WireTraspaso(TramiteEstado.Borrador, otId, DateTimeOffset.UtcNow, conDocumentoPrenda: true);
        _prendaPolicy
            .IsRequiredAsync(i.TenantId, otId, i.CreatedAt, Arg.Any<CancellationToken>())
            .Returns(true);

        var outcome = await Transition(i, TramiteEstado.Preparado);

        outcome.Success.Should().BeTrue();
        i.Status.Should().Be(TramiteEstado.Preparado);
    }

    [Fact]
    public async Task Ac2_TramiteEnCurso_ConOverrideNoAplicable_NoBloquea()
    {
        // Snapshot: la política resuelve `false` para un trámite que ya estaba en curso cuando el
        // override se activó (la comparación de fechas la hace IPrendaDocumentRequirementPolicy).
        var otId = Guid.NewGuid();
        var i = WireTraspaso(TramiteEstado.Borrador, otId, DateTimeOffset.UtcNow, conDocumentoPrenda: false);
        _prendaPolicy
            .IsRequiredAsync(i.TenantId, otId, i.CreatedAt, Arg.Any<CancellationToken>())
            .Returns(false);

        var outcome = await Transition(i, TramiteEstado.Preparado);

        outcome.Success.Should().BeTrue();
        i.Status.Should().Be(TramiteEstado.Preparado);
    }

    // ── HU #11592 — bloqueo duro de prenda en matrícula inicial ────────────────────────────────
    // Invierte deliberadamente la HU #10596 (prenda declarativa/informativa en matrícula, sin gate).

    [Fact]
    public async Task MatriculaInicial_ConDecisionSinPrenda_Avanza()
    {
        // AC1 — matrícula inicial con decisión "sin_prenda" vigente avanza.
        var i = Wire(TramiteEstado.Borrador, conGates: true);
        var sut = new TramiteLifecycleService(
            _repo, _typeRepo, _grantGate, _operabilityGate, NullOtRuleGate.Instance, _recorder, _publisher,
            prendaDocumentRequirementPolicy: _prendaPolicy,
            prendaRepo: StubPrendaRepo(PrendaDecision.SinPrenda));

        var outcome = await sut.TransitionAsync(
            new TramiteTransitionCommand(i.Id, i.TenantId, TramiteEstado.Preparado, null, null),
            TestContext.Current.CancellationToken);

        outcome.Success.Should().BeTrue();
        i.Status.Should().Be(TramiteEstado.Preparado);
    }

    [Fact]
    public async Task MatriculaInicial_SinNingunaDecisionDePrenda_Bloquea()
    {
        // AC2 — sin prenda vigente registrada y el override del OT desactivado (política sin
        // configurar ⇒ default false), el gate bloquea con independencia del semáforo de
        // gravámenes: no hay preflight snapshot cableado en `Wire`, así que HasGravamenWarn ni
        // siquiera se evalúa para este disparador.
        var i = Wire(TramiteEstado.Borrador, conGates: true);
        var sut = new TramiteLifecycleService(
            _repo, _typeRepo, _grantGate, _operabilityGate, NullOtRuleGate.Instance, _recorder, _publisher,
            prendaDocumentRequirementPolicy: _prendaPolicy,
            prendaRepo: StubPrendaRepo(decision: null));

        var outcome = await sut.TransitionAsync(
            new TramiteTransitionCommand(i.Id, i.TenantId, TramiteEstado.Preparado, null, null),
            TestContext.Current.CancellationToken);

        outcome.Success.Should().BeFalse();
        outcome.ErrorCode.Should().Be(TramiteEstadoErrores.PrendaDecisionRequerida);
        i.Status.Should().Be(TramiteEstado.Borrador);
        _recorder.Records.Should().BeEmpty();
    }

    [Fact]
    public async Task MatriculaInicial_ConSolicitarSinDocumento_ExigeDocumento()
    {
        // AC3 (rama documento) — "solicitar" sin el adjunto de soporte bloquea por documento. Con la
        // política compañía+OT exigiendo el certificado (default), el override CF-06 (HU #10881) —que
        // ya corre para CUALQUIER modalidad y es INDEPENDIENTE del semáforo, ver EvaluateOtOverride—
        // captura el caso primero y devuelve su código propio "_ot"; es el MISMO bloqueo por documento
        // faltante que exige la HU #11592, solo que vía el mecanismo que ya lo cubría.
        var i = Wire(TramiteEstado.Borrador, conGates: true);
        _prendaPolicy
            .IsRequiredAsync(i.TenantId, i.TransitOfficeId, i.CreatedAt, Arg.Any<CancellationToken>())
            .Returns(true);
        var sut = new TramiteLifecycleService(
            _repo, _typeRepo, _grantGate, _operabilityGate, NullOtRuleGate.Instance, _recorder, _publisher,
            prendaDocumentRequirementPolicy: _prendaPolicy,
            prendaRepo: StubPrendaRepo(PrendaDecision.Solicitar));

        var outcome = await sut.TransitionAsync(
            new TramiteTransitionCommand(i.Id, i.TenantId, TramiteEstado.Preparado, null, null),
            TestContext.Current.CancellationToken);

        outcome.Success.Should().BeFalse();
        outcome.ErrorCode.Should().Be(TramiteEstadoErrores.PrendaDocumentoRequeridoOt);
        i.Status.Should().Be(TramiteEstado.Borrador);
    }

    [Fact]
    public async Task MatriculaInicial_ConSolicitarDocumentoSinAcreedor_ExigeAcreedor()
    {
        // AC3 (rama acreedor) — con el documento adjunto pero sin acreedor diligenciado, el
        // faltante pasa a ser el acreedor (HU #11591 aplicado también en matrícula).
        var i = Wire(TramiteEstado.Borrador, conGates: true);
        i.Attachments.Add(new ProcedureInstanceAttachment
        {
            Id = Guid.NewGuid(),
            TenantId = i.TenantId,
            ProcedureInstanceId = i.Id,
            Tipo = "prenda_solicitud",
            StoragePath = "p/prenda_solicitud",
            UploadedAt = DateTimeOffset.UtcNow,
        });
        _prendaPolicy
            .IsRequiredAsync(i.TenantId, i.TransitOfficeId, i.CreatedAt, Arg.Any<CancellationToken>())
            .Returns(true);
        var sut = new TramiteLifecycleService(
            _repo, _typeRepo, _grantGate, _operabilityGate, NullOtRuleGate.Instance, _recorder, _publisher,
            prendaDocumentRequirementPolicy: _prendaPolicy,
            prendaRepo: StubPrendaRepo(PrendaDecision.Solicitar));

        var outcome = await sut.TransitionAsync(
            new TramiteTransitionCommand(i.Id, i.TenantId, TramiteEstado.Preparado, null, null),
            TestContext.Current.CancellationToken);

        outcome.Success.Should().BeFalse();
        outcome.ErrorCode.Should().Be(TramiteEstadoErrores.PrendaAcreedorRequerido);
        i.Status.Should().Be(TramiteEstado.Borrador);
    }

    [Fact]
    public async Task Traspaso_ConSemaforoGreenYSinPrendaVigente_NoRegresiona()
    {
        // AC4 (no regresión, R10/HU #10597) — el traspaso conserva su comportamiento previo: fuera
        // de warn el gate no bloquea aunque no haya prenda vigente registrada. El bloqueo duro de la
        // HU #11592 es exclusivo de matrícula inicial.
        var otId = Guid.NewGuid();
        var i = WireTraspaso(TramiteEstado.Borrador, otId, DateTimeOffset.UtcNow, conDocumentoPrenda: false);
        var sut = new TramiteLifecycleService(
            _repo, _typeRepo, _grantGate, _operabilityGate, NullOtRuleGate.Instance, _recorder, _publisher,
            prendaDocumentRequirementPolicy: _prendaPolicy,
            prendaRepo: StubPrendaRepo(decision: null));

        var outcome = await sut.TransitionAsync(
            new TramiteTransitionCommand(i.Id, i.TenantId, TramiteEstado.Preparado, null, null),
            TestContext.Current.CancellationToken);

        outcome.Success.Should().BeTrue();
        i.Status.Should().Be(TramiteEstado.Preparado);
    }

    // ── HU #10872 — re-radicar re-evaluando SOLO los gates de lo corregido ────────────────

    private static string BaselineMetadata(Dictionary<string, string?> fieldSnapshot, string? motivo = null) =>
        new SubsanacionObservation { Motivo = motivo, FieldSnapshot = fieldSnapshot }.ToJson();

    private ProcedureInstance WireEnSubsanacion(bool conGates = false)
    {
        var i = Wire(TramiteEstado.Rechazado, conGates);
        i.SubsanacionActiva = true;
        i.SubsanacionCount = 1;
        return i;
    }

    [Fact]
    public async Task Ac1_Reradicar_SoloVinCambio_NoReevaluaGateDePreparacion()
    {
        // Sin conGates: sin attachments ni biométrica — si el gate de preparación (RF03) corriera,
        // fallaría por documentos incompletos. Solo el VIN cambió → SOLO se re-evalúa VehicleState.
        var i = WireEnSubsanacion();
        ConVin(i, "1HGCM82633A004352");
        _repo.FindTramitesByVinAsync(i.TenantId, "1HGCM82633A004352", i.Id, Arg.Any<CancellationToken>())
            .Returns(new List<VinTramiteExistente>());
        _repo.GetLatestSubsanacionMetadataAsync(i.Id, i.TenantId, Arg.Any<CancellationToken>())
            .Returns(BaselineMetadata(new Dictionary<string, string?> { ["vin"] = "VINANTERIORCORREG" }));

        var outcome = await Transition(i, TramiteEstado.Entregado);

        outcome.Success.Should().BeTrue();
        i.Status.Should().Be(TramiteEstado.Entregado);
    }

    [Fact]
    public async Task Ac1_Reradicar_VinConMatriculaAprobadaEnFlit_SigueBloqueando()
    {
        // El VIN corregido SÍ se re-valida contra duplicidad (CF-03): sigue siendo un gate "de lo
        // corregido" cuando el campo cambiado es justamente el vin.
        var i = WireEnSubsanacion();
        ConVin(i, "1HGCM82633A004352");
        _repo.FindTramitesByVinAsync(i.TenantId, "1HGCM82633A004352", i.Id, Arg.Any<CancellationToken>())
            .Returns(new List<VinTramiteExistente>
            {
                new(Guid.NewGuid(), TramiteEstado.Aprobado, Paso: 5, Placa: null, Vin: "1HGCM82633A004352"),
            });
        _repo.GetLatestSubsanacionMetadataAsync(i.Id, i.TenantId, Arg.Any<CancellationToken>())
            .Returns(BaselineMetadata(new Dictionary<string, string?> { ["vin"] = "VINANTERIORCORREG" }));

        var outcome = await Transition(i, TramiteEstado.Entregado);

        outcome.Success.Should().BeFalse();
        outcome.ErrorCode.Should().Be(VehicleStatePolicy.ErrorCode);
    }

    [Fact]
    public async Task Ac1_Reradicar_OtroCampoCambio_ReevaluaGateDePreparacion_YFallaSinDocumentos()
    {
        // "color" no es el vin: cae en el bucket PreparationGate (HU #10872) — SIN attachments, el
        // gate de documentos SÍ bloquea (prueba que la re-evaluación selectiva realmente se disparó).
        var i = WireEnSubsanacion();
        i.FieldValues.Add(new ProcedureInstanceFieldValue
        {
            Id = Guid.NewGuid(),
            TenantId = i.TenantId,
            ProcedureInstanceId = i.Id,
            FieldKey = "color",
            ValueText = "rojo",
            Source = "user",
        });
        _repo.GetLatestSubsanacionMetadataAsync(i.Id, i.TenantId, Arg.Any<CancellationToken>())
            .Returns(BaselineMetadata(new Dictionary<string, string?> { ["color"] = "azul" }));

        var outcome = await Transition(i, TramiteEstado.Entregado);

        outcome.Success.Should().BeFalse();
        outcome.ErrorCode.Should().Be(TramiteEstadoErrores.DocumentosIncompletos);
    }

    [Fact]
    public async Task Ac1_Reradicar_SinCambios_NoBloqueaPorGatesDeCorreccion()
    {
        // Snapshot idéntico al estado actual (nada cambió): el diff es vacío → ninguna categoría de
        // gate de corrección corre; solo el gate final de entrega (siempre incondicional).
        var i = WireEnSubsanacion();
        ConVin(i, "1HGCM82633A004352");
        _repo.GetLatestSubsanacionMetadataAsync(i.Id, i.TenantId, Arg.Any<CancellationToken>())
            .Returns(BaselineMetadata(new Dictionary<string, string?> { ["vin"] = "1HGCM82633A004352" }));

        var outcome = await Transition(i, TramiteEstado.Entregado);

        outcome.Success.Should().BeTrue();
        await _repo.DidNotReceive().FindTramitesByVinAsync(
            Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Ac1_Reradicar_SinSnapshotBase_SoloReevaluaVehicleState_PreservaComportamientoPrevio()
    {
        // Fail-safe (dato legado anterior a HU #10872, sin GetLatestSubsanacionMetadataAsync
        // configurado → null): el gate de preparación NUNCA corrió para re-radicación antes
        // de esta HU — se preserva, así que sin attachments/biométrica igual puede re-radicarse.
        var i = WireEnSubsanacion();

        var outcome = await Transition(i, TramiteEstado.Entregado);

        outcome.Success.Should().BeTrue();
    }

    [Fact]
    public async Task Ac2_Reradicar_IdentidadYDocumentosVigentes_NoLosVuelveASolicitar_Permite()
    {
        // conGates:true ya deja attachments + biométrica APROBADA/VIGENTE del comprador — el gate de
        // preparación (disparado por el cambio de "color", no identidad) los REUTILIZA sin exigir una
        // nueva biométrica ni una consulta cross-trámite (AC2: "no se vuelven a solicitar").
        var i = WireEnSubsanacion(conGates: true);
        i.FieldValues.Add(new ProcedureInstanceFieldValue
        {
            Id = Guid.NewGuid(),
            TenantId = i.TenantId,
            ProcedureInstanceId = i.Id,
            FieldKey = "color",
            ValueText = "rojo",
            Source = "user",
        });
        _repo.GetLatestSubsanacionMetadataAsync(i.Id, i.TenantId, Arg.Any<CancellationToken>())
            .Returns(BaselineMetadata(new Dictionary<string, string?> { ["color"] = "azul" }));

        var outcome = await Transition(i, TramiteEstado.Entregado);

        outcome.Success.Should().BeTrue();
        i.Status.Should().Be(TramiteEstado.Entregado);
        // AC2 — la identidad se resolvió con la fila PROPIA ya vigente del trámite: nunca se disparó
        // la referencia cross-trámite (que sí implicaría "pedir"/buscar otra validación).
        await _repo.DidNotReceive().FindVigenteApprovedByDocumentAsync(
            Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
    }
}
