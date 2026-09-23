using System.Reflection;
using Flit.Tramites.Application.UseCases.ProcedureInstances;
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
/// HU #12796 (Épica #12760, D1) — hitos de regeneración anticipada que pasan por el ciclo de vida del
/// gestor (<see cref="TramiteLifecycleService"/>): la radicación (AC1) encola el consolidado maestro; un
/// entregado devuelto por esta vía (decisión sincronizada) encola los dos (AC2); aprobar no anticipa (AC4)
/// y la edición de campos tampoco (AC5). Siempre después del commit y con el tenant dueño del trámite.
///
/// <para>Uso de ejemplo:</para>
/// <code>
/// var queue = new RecordingRegeneracionQueue();
/// var sut = new TramiteLifecycleService(repo, typeRepo, grant, operability, NullOtRuleGate.Instance,
///     recorder, publisher, regeneracionQueue: queue);
/// await sut.TransitionAsync(new TramiteTransitionCommand(id, tenantId, "entregado", motivo, null), ct);
/// // queue.Solicitudes == [(tenantId, id, TipoConsolidado.Maestro)]
/// </code>
/// </summary>
public sealed class ConsolidadoRegeneracionHitosLifecycleTests
{
    private readonly IProcedureInstanceRepository _repo = Substitute.For<IProcedureInstanceRepository>();
    private readonly IProcedureTypeRepository _typeRepo = Substitute.For<IProcedureTypeRepository>();
    private readonly ITransitOfficeGrantGate _grantGate = Substitute.For<ITransitOfficeGrantGate>();
    private readonly IOtOperabilityGate _operabilityGate = Substitute.For<IOtOperabilityGate>();
    private readonly RecordingRegeneracionQueue _queue = new();

    private static readonly Guid OfficeId = Guid.Parse("aaaaaaaa-0001-4000-8000-000000000001");

    public ConsolidadoRegeneracionHitosLifecycleTests()
    {
        _grantGate
            .IsEnabledForTenantAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(true);
        _operabilityGate
            .IsOperableAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(true);
        _repo.SaveChangesWithConcurrencyGuardAsync(Arg.Any<CancellationToken>()).Returns(true);
    }

    private TramiteLifecycleService Sut(IConsolidadoRegeneracionQueue? queue) =>
        new(
            _repo, _typeRepo, _grantGate, _operabilityGate, NullOtRuleGate.Instance,
            new RecordingTransitionRecorder(), new RecordingTransitionPublisher(),
            regeneracionQueue: queue);

    private ProcedureInstance Wire(string status, string? plate = "ABC123")
    {
        var id = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var i = new ProcedureInstance
        {
            ProcedureType = ProcedureTypeFixture.For("matricula_inicial"),
            Id = id,
            TenantId = tenantId,
            ProcedureTypeId = Guid.NewGuid(),
            ReferenceNumber = "TRM-2026-000001",
            Status = status,
            CreatedAt = DateTimeOffset.UtcNow,
            ConsolidadoMaestroVigente = true,
            ConsolidadoWizardVigente = true,
        };
        if (plate is not null)
            AddField(i, "plate", plate, "consultation");
        AddField(i, "transit_office_id", OfficeId.ToString(), "user");

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

    private static Task<TramiteTransitionOutcome> Transition(
        TramiteLifecycleService sut, ProcedureInstance i, string to,
        TramiteActor actor = TramiteActor.Gestor, string? reason = null) =>
        sut.TransitionAsync(
            new TramiteTransitionCommand(i.Id, i.TenantId, to, reason, ChangedByUserId: null, actor),
            TestContext.Current.CancellationToken);

    // ── AC1 — Radicación ────────────────────────────────────────────────────────────

    [Fact] // AC1 happy path — preparado → entregado encola SOLO el maestro, con el tenant dueño.
    public async Task AC1_Radicacion_AEntregado_EncolaSoloElMaestro()
    {
        var i = Wire(TramiteEstado.Preparado);

        var outcome = await Transition(Sut(_queue), i, TramiteEstado.Entregado);

        outcome.Success.Should().BeTrue(outcome.ErrorDetail);
        _queue.Solicitudes.Should().Equal((i.TenantId, i.Id, TipoConsolidado.Maestro));
    }

    [Fact] // HU #12787 (AC2, F1) — la radicación del canal Quipux NO encola el maestro: queda fijo.
    public async Task HU12787_RadicacionQuipux_PreparadoAEntregado_NoEncolaElMaestro()
    {
        var i = Wire(TramiteEstado.Preparado);

        var outcome = await Transition(Sut(_queue), i, TramiteEstado.Entregado, TramiteActor.Quipux);

        outcome.Success.Should().BeTrue(outcome.ErrorDetail);
        _queue.Solicitudes.Should().BeEmpty(
            "el maestro recién radicado ante Quipux es el documento de la secretaría: el worker no lo pisa");
        i.ConsolidadoMaestroVigente.Should().BeFalse("la invalidación del cambio de estado se conserva");
    }

    [Fact] // AC1 — la Ruta Larga (sin placa → preasignacion) también es radicación hacia el OT.
    public async Task AC1_Radicacion_APreasignacion_EncolaElMaestro()
    {
        var i = Wire(TramiteEstado.Preparado, plate: null);

        var outcome = await Transition(Sut(_queue), i, TramiteEstado.Preasignacion);

        outcome.Success.Should().BeTrue(outcome.ErrorDetail);
        _queue.Solicitudes.Should().Equal((i.TenantId, i.Id, TipoConsolidado.Maestro));
    }

    [Fact] // AC1 edge — el encolado va DESPUÉS del commit: un conflicto de concurrencia no deja trabajo.
    public async Task AC1_Radicacion_ConflictoDeConcurrencia_NoEncola()
    {
        var i = Wire(TramiteEstado.Preparado);
        _repo.SaveChangesWithConcurrencyGuardAsync(Arg.Any<CancellationToken>()).Returns(false);

        var outcome = await Transition(Sut(_queue), i, TramiteEstado.Entregado);

        outcome.ErrorCode.Should().Be(TramiteEstadoErrores.ConflictoConcurrencia);
        _queue.Solicitudes.Should().BeEmpty();
    }

    [Fact] // AC1 edge — un gate de entrega que bloquea la radicación no encola nada.
    public async Task AC1_Radicacion_BloqueadaPorGate_NoEncola()
    {
        var i = Wire(TramiteEstado.Preparado);
        _grantGate.IsEnabledForTenantAsync(i.TenantId, OfficeId, Arg.Any<CancellationToken>()).Returns(false);

        var outcome = await Transition(Sut(_queue), i, TramiteEstado.Entregado);

        outcome.ErrorCode.Should().Be(TramiteEstadoErrores.OrganismoNoHabilitado);
        _queue.Solicitudes.Should().BeEmpty();
    }

    [Fact] // AC1 contrato — si la cola descarta (false) el hito NO falla: el perezoso lo cubre.
    public async Task AC1_Radicacion_ColaDescarta_ElHitoSigueExitoso()
    {
        var i = Wire(TramiteEstado.Preparado);
        var llena = new RecordingRegeneracionQueue { Acepta = false };

        var outcome = await Transition(Sut(llena), i, TramiteEstado.Entregado);

        outcome.Success.Should().BeTrue(outcome.ErrorDetail);
        i.Status.Should().Be(TramiteEstado.Entregado);
        i.ConsolidadoMaestroVigente.Should().BeFalse("la invalidación de siempre no depende de la cola");
        llena.Solicitudes.Should().ContainSingle();
    }

    [Fact] // AC1 contrato — sin cola cableada (tests/hosts sin worker) la radicación funciona igual.
    public async Task AC1_Radicacion_SinCola_FuncionaIgual()
    {
        var i = Wire(TramiteEstado.Preparado);

        var outcome = await Transition(Sut(queue: null), i, TramiteEstado.Entregado);

        outcome.Success.Should().BeTrue(outcome.ErrorDetail);
    }

    [Fact] // AC1 edge — borrador → preparado NO es radicación: no anticipa.
    public async Task AC1_Preparar_NoEsRadicacion_NoEncola()
    {
        var i = Wire(TramiteEstado.Borrador);
        await Transition(Sut(_queue), i, TramiteEstado.Preparado);

        _queue.Solicitudes.Should().BeEmpty();
    }

    // ── AC2 — decisión sincronizada (entregado → rechazado por el ciclo de vida) ──────────

    [Fact] // AC2 — un entregado devuelto por el ciclo de vida encola wizard y maestro.
    public async Task AC2_EntregadoRechazado_EncolaWizardYMaestro()
    {
        var i = Wire(TramiteEstado.Entregado);

        var outcome = await Transition(Sut(_queue), i, TramiteEstado.Rechazado, TramiteActor.Ot, "Observado por el OT");

        outcome.Success.Should().BeTrue(outcome.ErrorDetail);
        _queue.Solicitudes.Should().BeEquivalentTo(new[]
        {
            (i.TenantId, i.Id, TipoConsolidado.Wizard),
            (i.TenantId, i.Id, TipoConsolidado.Maestro),
        });
    }

    // ── AC4 — Aprobación no anticipa ────────────────────────────────────────────────

    [Fact] // AC4 — aprobar (estado final) invalida como siempre pero no encola nada.
    public async Task AC4_Aprobar_NoEncola()
    {
        var i = Wire(TramiteEstado.Entregado);

        var outcome = await Transition(Sut(_queue), i, TramiteEstado.Aprobado, TramiteActor.Ot);

        outcome.Success.Should().BeTrue(outcome.ErrorDetail);
        i.ConsolidadoMaestroVigente.Should().BeFalse();
        _queue.Solicitudes.Should().BeEmpty();
    }

    // ── AC5 — Edición no anticipa ───────────────────────────────────────────────────

    [Fact] // AC5 contrato — el PATCH de field-values no depende de la cola: no puede encolar.
    public void AC5_PatchFieldValues_NoDependeDeLaCola()
    {
        var parametros = typeof(PatchFieldValuesHandler)
            .GetConstructors(BindingFlags.Public | BindingFlags.Instance)
            .SelectMany(c => c.GetParameters())
            .Select(p => p.ParameterType);

        parametros.Should().NotContain(typeof(IConsolidadoRegeneracionQueue));
    }

    [Fact] // AC5 — dos PATCH sucesivos en borrador se aplican sin tocar el ciclo de vida (ni la cola).
    public async Task AC5_PatchSucesivos_NoPasanPorElCicloDeVida()
    {
        var ct = TestContext.Current.CancellationToken;
        var i = Wire(TramiteEstado.Borrador);
        _repo.GetByIdWithDetailsAsync(i.Id, i.TenantId, ct).Returns(i);
        _repo.GetFormFieldIdByKeyAsync(Arg.Any<Guid>(), Arg.Any<string>(), ct).Returns((Guid?)null);
        var patch = new PatchFieldValuesHandler(_repo);

        var (_, error1) = await patch.HandleAsync(
            i.Id, i.TenantId, new PatchFieldValuesRequest([new FieldValueInput(null, "vin", "1HGCM82633A004352", null)]), ct);
        var (_, error2) = await patch.HandleAsync(
            i.Id, i.TenantId, new PatchFieldValuesRequest([new FieldValueInput(null, "vin", "1HGCM82633A004353", null)]), ct);

        error1.Should().BeNull();
        error2.Should().BeNull();
        await _repo.DidNotReceive().GetByIdWithWizardGraphAsync(i.Id, i.TenantId, Arg.Any<CancellationToken>());
        _queue.Solicitudes.Should().BeEmpty();
    }
}

/// <summary>
/// Doble de <see cref="IConsolidadoRegeneracionQueue"/> (HU #12796): registra cada solicitud y responde
/// <see cref="Acepta"/> (false simula la cola llena / descarte).
/// </summary>
internal sealed class RecordingRegeneracionQueue : IConsolidadoRegeneracionQueue
{
    public List<(Guid TenantId, Guid ProcedureInstanceId, TipoConsolidado Documento)> Solicitudes { get; } = [];

    public bool Acepta { get; init; } = true;

    public bool Encolar(Guid tenantId, Guid procedureInstanceId, TipoConsolidado documento)
    {
        Solicitudes.Add((tenantId, procedureInstanceId, documento));
        return Acepta;
    }
}
