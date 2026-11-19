using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Application.UseCases.ProcedureInstances.Estados;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Enums;
using Flit.Tramites.Domain.Integration;
using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.Tramites.Estados;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Flit.Tramites.Application.Tests.UseCases.ProcedureInstances.Estados;

/// <summary>
/// Matrícula inicial por preasignación de placa: SIN placa el trámite se puede enviar aunque las partes
/// no hayan firmado (identidad aprobada y vigente); CON placa asignada la firma sigue siendo obligatoria.
/// El correo de validación de identidad se sigue enviando en ambos casos. Traspaso y demás familias no
/// cambian. La excepción vive en <see cref="FirmaGate.EximidaPorPreasignacion"/>.
/// </summary>
/// <remarks>
/// Uso de ejemplo:
/// <code>
/// var (result, error) = await submit.HandleAsync(id, tenant, null, ct);
/// // matrícula sin placa y sin firma: error == null, result.Status == "preasignacion"
/// </code>
/// Datos ficticios: documentos 9000000xxx, correos @example.test.
/// </remarks>
public sealed class FirmaExentaPorPreasignacionTests
{
    private readonly IProcedureInstanceRepository _repo = Substitute.For<IProcedureInstanceRepository>();
    private readonly IProcedureTypeRepository _typeRepo = Substitute.For<IProcedureTypeRepository>();
    private readonly ITransitOfficeGrantGate _grantGate = Substitute.For<ITransitOfficeGrantGate>();
    private readonly IOtOperabilityGate _operabilityGate = Substitute.For<IOtOperabilityGate>();
    private readonly IFirmaPendienteNotifier _notifier = Substitute.For<IFirmaPendienteNotifier>();
    private readonly ITransitOfficeQuipuxCanalReader _quipuxCanal = Substitute.For<ITransitOfficeQuipuxCanalReader>();
    private static readonly Guid OtQuipux = Guid.Parse("00000000-0000-0000-0000-0000000000d1");
    private const string RefsQuipuxMatricula = """{"quipux":{"familia":"MATRICULA","tipoTramite":16,"tipoRequisito":51,"prefijo":"MT","campoVin":"vin","maxLongitudEmpresa":25}}""";
    private readonly RecordingTransitionRecorder _recorder = new();
    private readonly RecordingTransitionPublisher _publisher = new();

    public FirmaExentaPorPreasignacionTests()
    {
        _grantGate.IsEnabledForTenantAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(true);
        _operabilityGate.IsOperableAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(true);
        _repo.SaveChangesWithConcurrencyGuardAsync(Arg.Any<CancellationToken>()).Returns(true);
        _notifier.NotificarAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(FirmaNotificacionEstados.Enviada);
    }

    private TramiteLifecycleService Lifecycle() =>
        new(_repo, _typeRepo, _grantGate, _operabilityGate, NullOtRuleGate.Instance, _recorder, _publisher,
            firmaNotifier: _notifier, quipuxCanalReader: _quipuxCanal);

    private SubmitProcedureInstanceHandler Submit() =>
        new(Lifecycle(), _repo, NullLogger<SubmitProcedureInstanceHandler>.Instance, _quipuxCanal);

    /// <summary>Canal Quipux ACTIVO para el OT de prueba (DIVIPO + bandera de matrícula).</summary>
    private void CanalQuipuxActivo() =>
        _quipuxCanal.GetAsync(OtQuipux, Arg.Any<CancellationToken>())
            .Returns(new TransitOfficeQuipuxCanal("11001", true, true, true));

    /// <summary>Copia del tipo de fixture con su propio ExternalRefs (los fixtures son instancias compartidas).</summary>
    private static ProcedureType Tipo(ProcedureType baseType, string code, string family, string? externalRefs = null) => new()
    {
        Id = Guid.NewGuid(),
        Code = code,
        Name = code,
        Family = family,
        GateProfile = baseType.GateProfile,
        Steps = baseType.Steps,
        ExternalRefs = externalRefs ?? "{}",
    };

    /// <summary>
    /// Trámite NO firmado: matrícula (pide placa) con todos los gates menos la firma, sin placa salvo que se
    /// indique. Sin validaciones de identidad.
    /// </summary>
    private ProcedureInstance Wire(string status, ProcedureType? tipo = null, bool conPlaca = false,
        bool subsanacion = false, bool conDocumentos = true, string? origin = null,
        Guid? otColumna = null, Guid? otEnFieldValues = null)
    {
        var id = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var i = new ProcedureInstance
        {
            ProcedureType = tipo ?? ProcedureTypeFixture.Matricula,
            Id = id,
            TenantId = tenantId,
            ProcedureTypeId = Guid.NewGuid(),
            ReferenceNumber = "TRM-2026-000099",
            Status = status,
            SubsanacionActiva = subsanacion,
            Origin = origin,
            TransitOfficeId = otColumna,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        if (otEnFieldValues is { } otFv)
            i.FieldValues.Add(Campo(i, "transit_office_id", otFv.ToString()));
        if (conDocumentos)
        {
            foreach (var t in new[] { "factura", "aduana", "impronta", "fur" })
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
        }

        // Matrícula inicial: el tipo de servicio es obligatorio al radicar (SubmitGate.TipoServicioRequerido).
        i.FieldValues.Add(new ProcedureInstanceFieldValue
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ProcedureInstanceId = id,
            FieldKey = "vehicle_service",
            ValueText = "PARTICULAR",
            Source = "user",
        });
        i.Actors.Add(Natural(i, "comprador", "9000000301"));
        i.FieldValues.Add(Campo(i, "transit_office_code", "11001000"));
        if (conPlaca)
            i.FieldValues.Add(Campo(i, "plate", "ABC123"));

        _repo.GetByIdWithDetailsAsync(id, tenantId, Arg.Any<CancellationToken>()).Returns(i);
        _repo.GetByIdWithWizardGraphAsync(id, tenantId, Arg.Any<CancellationToken>()).Returns(i);
        _typeRepo.GetByIdAsync(i.ProcedureTypeId, Arg.Any<CancellationToken>()).Returns(new ProcedureType
        {
            Id = i.ProcedureTypeId,
            Code = "X",
            Name = "X",
            Family = i.ProcedureType!.Family,
            PublicationStatus = PublicationStatus.Published,
            WizardEnabled = true,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        return i;
    }

    private static ProcedureInstanceFieldValue Campo(ProcedureInstance i, string key, string value) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = i.TenantId,
        ProcedureInstanceId = i.Id,
        FieldKey = key,
        ValueText = value,
        Source = "user",
    };

    private static ProcedureInstanceActor Natural(ProcedureInstance i, string parte, string documento) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = i.TenantId,
        ProcedureInstanceId = i.Id,
        ActorType = parte,
        DocumentType = "CC",
        DocumentNumber = documento,
        FullName = $"Persona {parte}",
        PersonType = "natural",
    };

    // ── Predicado ──

    [Fact]
    public void Predicado_MatriculaSinPlaca_EstaExenta()
    {
        var i = Wire(TramiteEstado.Borrador);

        FirmaGate.EximidaPorPreasignacion(i).Should().BeTrue();
    }

    [Fact]
    public void Predicado_MatriculaConPlaca_NoEstaExenta()
    {
        var i = Wire(TramiteEstado.Asignado, conPlaca: true);

        FirmaGate.EximidaPorPreasignacion(i).Should().BeFalse();
    }

    [Theory]
    [InlineData("traspaso")]
    [InlineData("cancelacion")]
    [InlineData("otros")]
    public void Predicado_FamiliasQueNoSonMatriculaInicial_NoEstanExentas(string tipo)
    {
        var tipoFixture = tipo switch
        {
            "traspaso" => ProcedureTypeFixture.Traspaso,
            "cancelacion" => ProcedureTypeFixture.Cancelacion, // MATRICULAS pero no pide placa
            _ => ProcedureTypeFixture.Blindaje,
        };
        var i = Wire(TramiteEstado.Borrador, tipoFixture);

        FirmaGate.EximidaPorPreasignacion(i).Should().BeFalse();
    }

    // ── Matrícula sin placa y sin firma: no bloquea, pero sí notifica ──

    [Fact]
    public async Task MatriculaSinPlaca_SinFirma_BorradorAPreasignacion_Pasa_YNotifica()
    {
        var ct = TestContext.Current.CancellationToken;
        var i = Wire(TramiteEstado.Borrador);

        var (result, error) = await Submit().HandleAsync(i.Id, i.TenantId, changedBy: null, ct);

        error.Should().BeNull();
        result!.Status.Should().Be(TramiteEstado.Preasignacion);
        _recorder.Records.Should().Contain(r => r.ToStatus == TramiteEstado.Preasignacion);
        await _notifier.Received().NotificarAsync(i.Id, i.TenantId, "comprador", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task MatriculaSinPlaca_SinFirma_PreparadoAPreasignacion_Pasa_YNotifica()
    {
        var ct = TestContext.Current.CancellationToken;
        var i = Wire(TramiteEstado.Preparado);

        var outcome = await Lifecycle().TransitionAsync(
            new TramiteTransitionCommand(i.Id, i.TenantId, TramiteEstado.Preasignacion, null, null), ct);

        outcome.Success.Should().BeTrue();
        outcome.PartesSinFirma.Should().BeNull("la firma pendiente no se reporta como bloqueo");
        i.Status.Should().Be(TramiteEstado.Preasignacion);
        await _notifier.Received().NotificarAsync(i.Id, i.TenantId, "comprador", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task MatriculaSinPlaca_SinFirma_NotificadorQueFalla_NoRompeLaTransicion()
    {
        var ct = TestContext.Current.CancellationToken;
        var i = Wire(TramiteEstado.Preparado);
        _notifier.NotificarAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<string>(_ => throw new InvalidOperationException("proveedor caído"));

        var outcome = await Lifecycle().TransitionAsync(
            new TramiteTransitionCommand(i.Id, i.TenantId, TramiteEstado.Preasignacion, null, null), ct);

        outcome.Success.Should().BeTrue();
        i.Status.Should().Be(TramiteEstado.Preasignacion);
    }

    [Fact]
    public async Task MatriculaSinPlaca_Subsanacion_SinFirma_ReRadicaAPreasignacion()
    {
        var ct = TestContext.Current.CancellationToken;
        var i = Wire(TramiteEstado.Rechazado, subsanacion: true);
        i.RejectedFrom = TramiteEstado.Preasignacion;

        var (result, error) = await Submit().HandleAsync(i.Id, i.TenantId, changedBy: null, ct);

        error.Should().BeNull();
        result!.Status.Should().Be(TramiteEstado.Preasignacion);
    }

    // ── Matrícula CON placa y sin firma: la firma sigue siendo obligatoria ──

    [Fact]
    public async Task MatriculaConPlaca_SinFirma_PreparadoAEntregado_BloqueaFirmaPendiente()
    {
        var ct = TestContext.Current.CancellationToken;
        var i = Wire(TramiteEstado.Preparado, conPlaca: true);

        var outcome = await Lifecycle().TransitionAsync(
            new TramiteTransitionCommand(i.Id, i.TenantId, TramiteEstado.Entregado, null, null), ct);

        outcome.Success.Should().BeFalse();
        outcome.ErrorCode.Should().Be(TramiteEstadoErrores.FirmaPendiente);
        outcome.PartesSinFirma.Should().NotBeNullOrEmpty();
        i.Status.Should().Be(TramiteEstado.Preparado);
    }

    [Fact]
    public async Task MatriculaConPlaca_SinFirma_BorradorAPreparado_BloqueaPorIdentidad()
    {
        var ct = TestContext.Current.CancellationToken;
        var i = Wire(TramiteEstado.Borrador, conPlaca: true);

        var (result, error) = await Submit().HandleAsync(i.Id, i.TenantId, changedBy: null, ct);

        result.Should().BeNull();
        error.Should().Be(TramiteEstadoErrores.IdentidadNoAprobada);
        i.Status.Should().Be(TramiteEstado.Borrador);
    }

    // ── Asignado → Entregado («Enviar al OT») sigue exigiendo firma: Asignado siempre tiene placa ──

    [Fact]
    public async Task EnviarAlOt_AsignadoSinFirma_Bloquea()
    {
        var ct = TestContext.Current.CancellationToken;
        var i = Wire(TramiteEstado.Asignado, conPlaca: true);
        var handler = new EnviarAlOtHandler(
            _repo, Lifecycle(),
            firmaGate: new TramiteFirmaGate(_repo), firmaNotifier: _notifier);

        var (result, error, _) = await handler.HandleAsync(
            i.Id, i.TenantId, changedBy: null, new EnviarAlOtRequest(), ct);

        result.Should().BeNull();
        error.Should().Be(TramiteEstadoErrores.FirmaPendiente);
        i.Status.Should().Be(TramiteEstado.Asignado);
    }

    [Fact]
    public async Task Lifecycle_AsignadoAEntregado_SinFirma_Bloquea()
    {
        var ct = TestContext.Current.CancellationToken;
        var i = Wire(TramiteEstado.Asignado, conPlaca: true);

        var outcome = await Lifecycle().TransitionAsync(
            new TramiteTransitionCommand(i.Id, i.TenantId, TramiteEstado.Entregado, null, null), ct);

        outcome.ErrorCode.Should().Be(TramiteEstadoErrores.FirmaPendiente);
    }

    // ── Traspaso: sin cambios ──

    [Fact]
    public async Task Traspaso_SinFirma_SigueBloqueado()
    {
        var ct = TestContext.Current.CancellationToken;
        var i = Wire(TramiteEstado.Preparado, ProcedureTypeFixture.Traspaso);
        i.Actors.Add(Natural(i, "vendedor", "9000000302"));

        var outcome = await Lifecycle().TransitionAsync(
            new TramiteTransitionCommand(i.Id, i.TenantId, TramiteEstado.Entregado, null, null), ct);

        outcome.Success.Should().BeFalse();
        outcome.ErrorCode.Should().Be(TramiteEstadoErrores.FirmaPendiente);
    }

    // ── Los demás gates siguen aplicando ──

    [Fact]
    public async Task MatriculaSinPlaca_SinFirma_ConDocumentosIncompletos_SigueBloqueadaPorDocumentos()
    {
        var ct = TestContext.Current.CancellationToken;
        var i = Wire(TramiteEstado.Borrador, conDocumentos: false);

        var (result, error) = await Submit().HandleAsync(i.Id, i.TenantId, changedBy: null, ct);

        result.Should().BeNull();
        error.Should().Be(TramiteEstadoErrores.DocumentosIncompletos);
        i.Status.Should().Be(TramiteEstado.Borrador);
    }

    // ── Canal Quipux: ITramiteFirmaGate (lo consume EncolarEnvioQuipuxHandler al encolar) ──

    [Fact]
    public async Task TramiteFirmaGate_MatriculaSinPlaca_SinFirma_SigueReportandoElComprador()
    {
        // El canal Quipux queda EXACTAMENTE como antes: encolar sigue respondiendo firma_pendiente.
        var i = Wire(TramiteEstado.Preparado);

        var faltantes = await new TramiteFirmaGate(_repo)
            .PartesSinFirmaAsync(i.Id, i.TenantId, TestContext.Current.CancellationToken);

        faltantes.Should().Equal("comprador");
    }

    [Fact]
    public async Task TramiteFirmaGate_MatriculaConPlaca_SinFirma_ReportaElComprador()
    {
        var i = Wire(TramiteEstado.Preparado, conPlaca: true);

        var faltantes = await new TramiteFirmaGate(_repo)
            .PartesSinFirmaAsync(i.Id, i.TenantId, TestContext.Current.CancellationToken);

        faltantes.Should().Equal("comprador");
    }

    [Fact]
    public async Task TramiteFirmaGate_Traspaso_SinFirma_SigueReportandoPartes()
    {
        var i = Wire(TramiteEstado.Preparado, ProcedureTypeFixture.Traspaso);

        var faltantes = await new TramiteFirmaGate(_repo)
            .PartesSinFirmaAsync(i.Id, i.TenantId, TestContext.Current.CancellationToken);

        faltantes.Should().Contain("comprador");
    }

    // ── Alcance: solo FLIT 2 por el canal gestor/OT ──

    [Fact]
    public async Task MatriculaLeasing_SinPlaca_CanalGestorOt_EstaExenta()
    {
        var ct = TestContext.Current.CancellationToken;
        var i = Wire(TramiteEstado.Preparado, ProcedureTypeFixture.MatriculaLeasing);

        var outcome = await Lifecycle().TransitionAsync(
            new TramiteTransitionCommand(i.Id, i.TenantId, TramiteEstado.Preasignacion, null, null), ct);

        outcome.Success.Should().BeTrue();
        (await FirmaGate.EximidaPorPreasignacionAsync(i, _quipuxCanal, ct)).Should().BeTrue();
    }

    [Fact]
    public async Task Rematricula_SinPlaca_NoEstaExenta_Bloquea()
    {
        var ct = TestContext.Current.CancellationToken;
        var rematricula = Tipo(ProcedureTypeFixture.Matricula, "REMATRICULA", "MATRICULAS");
        var i = Wire(TramiteEstado.Preparado, rematricula);

        var outcome = await Lifecycle().TransitionAsync(
            new TramiteTransitionCommand(i.Id, i.TenantId, TramiteEstado.Preasignacion, null, null), ct);

        FirmaGate.EximidaPorPreasignacion(i).Should().BeFalse();
        outcome.Success.Should().BeFalse();
        outcome.ErrorCode.Should().Be(TramiteEstadoErrores.FirmaPendiente);
    }

    [Theory]
    [InlineData("persona_natural")] // PersonaNaturalEventos.Origen: portal Persona Natural, no se toca
    [InlineData("ict")]
    public async Task TramiteQueNoNaceEnLaPlataforma_NoEstaExento_Bloquea(string origin)
    {
        var ct = TestContext.Current.CancellationToken;
        var i = Wire(TramiteEstado.Preparado, origin: origin);

        var outcome = await Lifecycle().TransitionAsync(
            new TramiteTransitionCommand(i.Id, i.TenantId, TramiteEstado.Preasignacion, null, null), ct);

        FirmaGate.EximidaPorPreasignacion(i).Should().BeFalse();
        outcome.Success.Should().BeFalse();
        outcome.ErrorCode.Should().Be(TramiteEstadoErrores.FirmaPendiente);
    }

    // ── Canal Quipux activo: vuelve a como funcionaba antes (la firma se exige desde borrador) ──

    [Fact]
    public async Task CanalQuipuxActivo_SinPlaca_SinFirma_BloqueaEnPreparar_YNoEstaExenta()
    {
        var ct = TestContext.Current.CancellationToken;
        CanalQuipuxActivo();
        var tipo = Tipo(ProcedureTypeFixture.Matricula, "MATRICULA_NUEVA", "MATRICULAS", RefsQuipuxMatricula);
        var i = Wire(TramiteEstado.Borrador, tipo, otColumna: OtQuipux);

        var (result, error) = await Submit().HandleAsync(i.Id, i.TenantId, changedBy: null, ct);

        result.Should().BeNull();
        error.Should().Be(TramiteEstadoErrores.IdentidadNoAprobada, "la firma se exige desde borrador→preparado, como antes");
        i.Status.Should().Be(TramiteEstado.Borrador);
        (await FirmaGate.EximidaPorPreasignacionAsync(i, _quipuxCanal, ct)).Should().BeFalse();
    }

    [Fact]
    public async Task CanalQuipuxActivo_ConOtSoloEnFieldValues_ColumnaVacia_NoSeEscapa()
    {
        // En borrador la columna TransitOfficeId puede estar null: el OT efectivo sale de field_values.
        var ct = TestContext.Current.CancellationToken;
        CanalQuipuxActivo();
        var tipo = Tipo(ProcedureTypeFixture.Matricula, "MATRICULA_NUEVA", "MATRICULAS", RefsQuipuxMatricula);
        var i = Wire(TramiteEstado.Borrador, tipo, otColumna: null, otEnFieldValues: OtQuipux);

        (await FirmaGate.EximidaPorPreasignacionAsync(i, _quipuxCanal, ct)).Should().BeFalse();
        var (result, error) = await Submit().HandleAsync(i.Id, i.TenantId, changedBy: null, ct);
        result.Should().BeNull();
        error.Should().Be(TramiteEstadoErrores.IdentidadNoAprobada);
    }

    [Fact]
    public async Task ConOtPeroSinLectorDeCanal_FailSafe_NoHayExencion()
    {
        var ct = TestContext.Current.CancellationToken;
        var i = Wire(TramiteEstado.Borrador, otEnFieldValues: OtQuipux);

        (await FirmaGate.EximidaPorPreasignacionAsync(i, reader: null, ct)).Should().BeFalse();
    }

    [Fact]
    public async Task LecturaDeCanalQueFalla_FailSafe_NoHayExencion()
    {
        var ct = TestContext.Current.CancellationToken;
        _quipuxCanal.GetAsync(OtQuipux, Arg.Any<CancellationToken>())
            .Returns<TransitOfficeQuipuxCanal?>(_ => throw new InvalidOperationException("db caída"));
        var i = Wire(TramiteEstado.Borrador, otColumna: OtQuipux);

        (await FirmaGate.EximidaPorPreasignacionAsync(i, _quipuxCanal, ct)).Should().BeFalse();
    }

    [Fact]
    public async Task OtSinCanalQuipuxActivo_CanalGestor_SigueExenta()
    {
        var ct = TestContext.Current.CancellationToken;
        _quipuxCanal.GetAsync(OtQuipux, Arg.Any<CancellationToken>())
            .Returns(new TransitOfficeQuipuxCanal("11001", false, false, false));
        var tipo = Tipo(ProcedureTypeFixture.Matricula, "MATRICULA_NUEVA", "MATRICULAS", RefsQuipuxMatricula);
        var i = Wire(TramiteEstado.Borrador, tipo, otColumna: OtQuipux);

        (await FirmaGate.EximidaPorPreasignacionAsync(i, _quipuxCanal, ct)).Should().BeTrue();
    }

    [Fact]
    public async Task CanalQuipuxActivo_ElGateDeEncolarSigueExigiendoFirma()
    {
        // ITramiteFirmaGate (lo consume EncolarEnvioQuipuxHandler) no cambia: sin firma ⇒ firma_pendiente.
        var i = Wire(TramiteEstado.Preparado, otColumna: OtQuipux);

        var faltantes = await new TramiteFirmaGate(_repo)
            .PartesSinFirmaAsync(i.Id, i.TenantId, TestContext.Current.CancellationToken);

        faltantes.Should().Equal("comprador");
    }
}
