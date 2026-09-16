using Flit.Tramites.Application.UseCases.ProcedureInstances.Estados;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Enums;
using Flit.Tramites.Domain.Integration;
using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.Tramites.Estados;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Tramites.Application.Tests.UseCases.ProcedureInstances.Estados;

/// <summary>
/// ADR-0059 / HU #12597 — la ruta de placa en el ciclo de vida REAL: la política con contexto se
/// alimenta del tipo (<c>requiresPlateRequest</c>), de <c>field_values.plate</c>, del actor de la orden y
/// del flag de subsanación; la radicación a <c>preasignacion</c> corre los gates de entrega y sella
/// <c>submitted_at</c>; «Enviar al OT» no; el rechazo deja su origen en <c>rejected_from</c>.
/// </summary>
public sealed class TramiteLifecycleServicePlateRouteTests
{
    private readonly IProcedureInstanceRepository _repo = Substitute.For<IProcedureInstanceRepository>();
    private readonly IProcedureTypeRepository _typeRepo = Substitute.For<IProcedureTypeRepository>();
    private readonly ITransitOfficeGrantGate _grantGate = Substitute.For<ITransitOfficeGrantGate>();
    private readonly IOtOperabilityGate _operabilityGate = Substitute.For<IOtOperabilityGate>();
    private readonly RecordingTransitionRecorder _recorder = new();
    private readonly RecordingTransitionPublisher _publisher = new();
    private readonly TramiteLifecycleService _sut;

    private static readonly Guid BogotaOfficeId = Guid.Parse("aaaaaaaa-0001-4000-8000-000000000001");

    public TramiteLifecycleServicePlateRouteTests()
    {
        _grantGate
            .IsEnabledForTenantAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(true);
        _operabilityGate
            .IsOperableAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(true);
        _repo.SaveChangesWithConcurrencyGuardAsync(Arg.Any<CancellationToken>()).Returns(true);
        _sut = new TramiteLifecycleService(
            _repo, _typeRepo, _grantGate, _operabilityGate, NullOtRuleGate.Instance, _recorder, _publisher);
    }

    private ProcedureInstance Wire(string status, string tipo = "matricula_inicial", string? plate = null)
    {
        var id = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var i = new ProcedureInstance
        {
            ProcedureType = ProcedureTypeFixture.For(tipo),
            Id = id,
            TenantId = tenantId,
            ProcedureTypeId = Guid.NewGuid(),
            ReferenceNumber = "TRM-2026-000001",
            Status = status,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        if (plate is not null)
            AddField(i, "plate", plate, "consultation");
        AddField(i, "transit_office_id", BogotaOfficeId.ToString(), "user");

        _repo.GetByIdWithWizardGraphAsync(id, tenantId, Arg.Any<CancellationToken>()).Returns(i);
        _typeRepo.GetByIdAsync(i.ProcedureTypeId, Arg.Any<CancellationToken>()).Returns(new ProcedureType
        {
            Id = i.ProcedureTypeId,
            Code = "X",
            Name = "X",
            Family = "matriculas",
            PublicationStatus = PublicationStatus.Published,
            WizardEnabled = true,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        return i;
    }

    private static void AddField(ProcedureInstance i, string key, string value, string source) =>
        i.FieldValues.Add(new ProcedureInstanceFieldValue
        {
            Id = Guid.NewGuid(),
            TenantId = i.TenantId,
            ProcedureInstanceId = i.Id,
            FieldKey = key,
            ValueText = value,
            Source = source,
        });

    private Task<TramiteTransitionOutcome> Transition(
        ProcedureInstance i, string to, TramiteActor actor, string? reason = null) =>
        _sut.TransitionAsync(
            new TramiteTransitionCommand(i.Id, i.TenantId, to, reason, ChangedByUserId: null, actor),
            TestContext.Current.CancellationToken);

    // ── Radicación ────────────────────────────────────────────────────────────────

    [Fact] // AC1 — sin placa: preasignacion es una radicación completa (gates de entrega + submitted_at).
    public async Task Radicar_SinPlaca_APreasignacion_CorreGatesDeEntregaYSellaSubmittedAt()
    {
        var i = Wire(TramiteEstado.Preparado);

        var outcome = await Transition(i, TramiteEstado.Preasignacion, TramiteActor.Gestor);

        outcome.Success.Should().BeTrue(outcome.ErrorDetail);
        i.Status.Should().Be(TramiteEstado.Preasignacion);
        i.SubmittedAt.Should().NotBeNull();
        i.TransitOfficeId.Should().Be(BogotaOfficeId, "los gates de entrega promueven el OT elegido");
        await _grantGate.Received(1).IsEnabledForTenantAsync(i.TenantId, BogotaOfficeId, Arg.Any<CancellationToken>());
        _recorder.Records.Should().ContainSingle(r => r.ToStatus == TramiteEstado.Preasignacion);
    }

    [Fact] // AC1 (−) — organismo no habilitado bloquea también la radicación a preasignacion.
    public async Task Radicar_SinPlaca_OrganismoNoHabilitado_Bloquea()
    {
        var i = Wire(TramiteEstado.Preparado);
        _grantGate.IsEnabledForTenantAsync(i.TenantId, BogotaOfficeId, Arg.Any<CancellationToken>()).Returns(false);

        var outcome = await Transition(i, TramiteEstado.Preasignacion, TramiteActor.Gestor);

        outcome.ErrorCode.Should().Be(TramiteEstadoErrores.OrganismoNoHabilitado);
        i.Status.Should().Be(TramiteEstado.Preparado);
    }

    [Fact] // AC2 — con placa la ruta corta va directo a entregado.
    public async Task Radicar_ConPlaca_AEntregado()
    {
        var i = Wire(TramiteEstado.Preparado, plate: "ABC123");

        var outcome = await Transition(i, TramiteEstado.Entregado, TramiteActor.Gestor);

        outcome.Success.Should().BeTrue(outcome.ErrorDetail);
        i.Status.Should().Be(TramiteEstado.Entregado);
    }

    [Fact] // El gestor no puede saltarse la cola de placa: la política lo veta con contexto real.
    public async Task Radicar_SinPlaca_AEntregado_ActorGestor_RequierePreasignacion()
    {
        var i = Wire(TramiteEstado.Preparado);

        var outcome = await Transition(i, TramiteEstado.Entregado, TramiteActor.Gestor);

        outcome.ErrorCode.Should().Be(TramiteEstadoErrores.TransicionRequierePreasignacion);
        i.Status.Should().Be(TramiteEstado.Preparado);
        _recorder.Records.Should().BeEmpty();
    }

    [Fact] // AC8 — Quipux entrega en un salto aunque el tipo pida placa y no la tenga (ADR-0051).
    public async Task Radicar_SinPlaca_AEntregado_ActorQuipux_Permitido()
    {
        var i = Wire(TramiteEstado.Preparado);

        var outcome = await Transition(i, TramiteEstado.Entregado, TramiteActor.Quipux);

        outcome.Success.Should().BeTrue(outcome.ErrorDetail);
        i.Status.Should().Be(TramiteEstado.Entregado);
    }

    [Fact] // AC3 — un traspaso jamás entra a la ruta de placa.
    public async Task Traspaso_APreasignacion_TransicionRequierePlaca()
    {
        var i = Wire(TramiteEstado.Preparado, tipo: "traspaso");

        var outcome = await Transition(i, TramiteEstado.Preasignacion, TramiteActor.Gestor);

        outcome.ErrorCode.Should().Be(TramiteEstadoErrores.TransicionRequierePlaca);
    }

    // ── Cola de placa ─────────────────────────────────────────────────────────────

    [Fact] // AC6 — rechazo desde preasignacion: motivo obligatorio y rejected_from = preasignacion.
    public async Task Rechazar_DesdePreasignacion_GuardaElOrigen()
    {
        var i = Wire(TramiteEstado.Preasignacion);

        var sinMotivo = await Transition(i, TramiteEstado.Rechazado, TramiteActor.Ot);
        sinMotivo.ErrorCode.Should().Be(TramiteEstadoErrores.MotivoRequerido);

        var outcome = await Transition(i, TramiteEstado.Rechazado, TramiteActor.Ot, "falta documento");

        outcome.Success.Should().BeTrue(outcome.ErrorDetail);
        i.Status.Should().Be(TramiteEstado.Rechazado);
        i.RejectedFrom.Should().Be(TramiteEstado.Preasignacion);
        i.SubmittedAt.Should().BeNull("rechazar no es radicar");
    }

    [Fact] // El origen del rechazo es genérico: desde entregado también se guarda (el distintivo solo aplica a preasignacion).
    public async Task Rechazar_DesdeEntregado_GuardaElOrigen()
    {
        var i = Wire(TramiteEstado.Entregado, plate: "ABC123");

        var outcome = await Transition(i, TramiteEstado.Rechazado, TramiteActor.Ot, "motivo");

        outcome.Success.Should().BeTrue(outcome.ErrorDetail);
        i.RejectedFrom.Should().Be(TramiteEstado.Entregado);
    }

    [Fact] // La marca se limpia al salir de rechazado por cualquier arista (aquí: anular).
    public async Task SalirDeRechazado_LimpiaElOrigen()
    {
        var i = Wire(TramiteEstado.Rechazado);
        i.RejectedFrom = TramiteEstado.Preasignacion;

        var outcome = await Transition(i, TramiteEstado.Anulado, TramiteActor.Gestor, "desistido");

        outcome.Success.Should().BeTrue(outcome.ErrorDetail);
        i.RejectedFrom.Should().BeNull();
    }

    [Fact] // AC4 — «Enviar al OT» no es radicación: ni gates de entrega ni submitted_at nuevo.
    public async Task EnviarAlOt_DesdeAsignado_NoReRadica()
    {
        var i = Wire(TramiteEstado.Asignado, plate: "ABC123");
        var radicadoEl = DateTimeOffset.UtcNow.AddDays(-2);
        i.SubmittedAt = radicadoEl;

        var outcome = await Transition(i, TramiteEstado.Entregado, TramiteActor.Gestor);

        outcome.Success.Should().BeTrue(outcome.ErrorDetail);
        i.Status.Should().Be(TramiteEstado.Entregado);
        i.SubmittedAt.Should().Be(radicadoEl);
        await _grantGate.DidNotReceive().IsEnabledForTenantAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Theory] // Las aristas del OT se rechazan con actor gestor y viceversa, con contexto real.
    [InlineData("asignado", "preasignacion", TramiteActor.Gestor, TramiteEstadoErrores.TransicionSoloOt)]
    [InlineData("preasignacion", "asignado", TramiteActor.Gestor, TramiteEstadoErrores.TransicionSoloOt)]
    [InlineData("asignado", "entregado", TramiteActor.Ot, TramiteEstadoErrores.TransicionSoloGestor)]
    public async Task ActorEquivocado_SeRechaza(string from, string to, TramiteActor actor, string esperado)
    {
        var i = Wire(from, plate: "ABC123");

        var outcome = await Transition(i, to, actor);

        outcome.ErrorCode.Should().Be(esperado);
        i.Status.Should().Be(from);
    }

    [Fact] // El OT libera la placa: asignado → preasignacion, la placa sigue en field_values.
    public async Task LiberarPlaca_DesdeAsignado_ActorOt()
    {
        var i = Wire(TramiteEstado.Asignado, plate: "ABC123");

        var outcome = await Transition(i, TramiteEstado.Preasignacion, TramiteActor.Ot);

        outcome.Success.Should().BeTrue(outcome.ErrorDetail);
        i.Status.Should().Be(TramiteEstado.Preasignacion);
        i.FieldValues.Should().Contain(f => f.FieldKey == "plate" && f.ValueText == "ABC123");
    }

    // ── Re-radicación ─────────────────────────────────────────────────────────────

    [Theory] // AC7 — desde rechazado con subsanación activa, la placa decide; sin el flag no hay atajo.
    [InlineData(null, "preasignacion", true)]
    [InlineData("ABC123", "entregado", true)]
    [InlineData(null, "preasignacion", false)]
    public async Task ReRadicar_DesdeRechazado(string? plate, string destino, bool subsanacionActiva)
    {
        var i = Wire(TramiteEstado.Rechazado, plate: plate);
        i.SubsanacionActiva = subsanacionActiva;
        i.RejectedFrom = TramiteEstado.Preasignacion;
        _repo.GetLatestSubsanacionMetadataAsync(i.Id, i.TenantId, Arg.Any<CancellationToken>()).Returns((string?)null);

        var outcome = await Transition(i, destino, TramiteActor.Gestor);

        if (!subsanacionActiva)
        {
            outcome.ErrorCode.Should().Be(TramiteEstadoErrores.TransicionNoPermitida);
            i.Status.Should().Be(TramiteEstado.Rechazado);
            return;
        }

        outcome.Success.Should().BeTrue(outcome.ErrorDetail);
        i.Status.Should().Be(destino);
        i.SubsanacionActiva.Should().BeFalse();
        i.RejectedFrom.Should().BeNull();
        i.SubmittedAt.Should().NotBeNull();
    }
}
