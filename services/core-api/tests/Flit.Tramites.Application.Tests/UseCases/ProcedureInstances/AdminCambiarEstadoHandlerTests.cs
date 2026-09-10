using Flit.Tramites.Application.Tests.UseCases.ProcedureInstances.Estados;
using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.Tramites.Estados;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Tramites.Application.Tests.UseCases.ProcedureInstances;

/// <summary>
/// HU #12159 — cambio de estado administrativo: cambio LIBRE entre estados no finales sin pasar por
/// <see cref="TramiteStateMachine"/> (AC1), con la única regla dura de excluir <c>aprobado</c> como
/// destino (AC2) u origen (AC3). AC4 (confirmación previa) es responsabilidad del frontend (HU
/// #12163); la contribución de este handler es no aplicar NINGÚN cambio cuando la validación falla
/// (ni SaveChanges ni historial), cubierta en los casos de error de AC2/AC3.
/// </summary>
public sealed class AdminCambiarEstadoHandlerTests
{
    private readonly IProcedureInstanceRepository _repo = Substitute.For<IProcedureInstanceRepository>();
    private readonly RecordingTransitionRecorder _recorder = new();

    private static ProcedureInstance Instance(Guid id, Guid tenantId, string status) => new()
    {
        Id = id,
        TenantId = tenantId,
        ProcedureTypeId = Guid.NewGuid(),
        ReferenceNumber = "TRM-2026-000999",
        Status = status,
        CreatedAt = DateTimeOffset.UtcNow,
    };

    private AdminCambiarEstadoHandler Handler() => new(_repo, _recorder);

    // ── AC1 — cambio libre entre estados no finales, sin pasar por TramiteStateMachine ───────────

    [Fact]
    public async Task AC1_BorradorAEntregado_TransicionNoPermitidaPorLaMaquinaNormal_SeAplicaIgual()
    {
        // Prueba deliberadamente la transición borrador→entregado, que NO existe en
        // TramiteStateMachine.Transitions (solo permite borrador→[anulado, preparado]): si este
        // handler reutilizara la máquina, fallaría con TransicionNoPermitida.
        TramiteStateMachine.IsValidTransition(TramiteEstado.Borrador, TramiteEstado.Entregado)
            .Should().BeFalse("la máquina normal NO permite este salto; el bypass es el objeto de la HU");

        var id = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var instance = Instance(id, tenantId, TramiteEstado.Borrador);
        _repo.GetByIdAsync(id, tenantId, Arg.Any<CancellationToken>()).Returns(instance);
        _repo.SaveChangesWithConcurrencyGuardAsync(Arg.Any<CancellationToken>()).Returns(true);

        var command = new AdminCambiarEstadoCommand(id, tenantId, TramiteEstado.Entregado, "corrección excepcional", userId);
        var (result, error, _) = await Handler().HandleAsync(command, CancellationToken.None);

        error.Should().BeNull();
        result.Should().NotBeNull();
        result!.PreviousStatus.Should().Be(TramiteEstado.Borrador);
        result.NewStatus.Should().Be(TramiteEstado.Entregado);
        instance.Status.Should().Be(TramiteEstado.Entregado);
    }

    [Fact]
    public async Task AC1_CambioAplicado_QuedaRegistradoConEstadoAnteriorNuevoUsuarioFechaYHora()
    {
        var id = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var instance = Instance(id, tenantId, TramiteEstado.Borrador);
        _repo.GetByIdAsync(id, tenantId, Arg.Any<CancellationToken>()).Returns(instance);
        _repo.SaveChangesWithConcurrencyGuardAsync(Arg.Any<CancellationToken>()).Returns(true);
        var antes = DateTimeOffset.UtcNow;

        var command = new AdminCambiarEstadoCommand(id, tenantId, TramiteEstado.Entregado, "corrección excepcional", userId);
        var (_, error, _) = await Handler().HandleAsync(command, CancellationToken.None);

        error.Should().BeNull();

        // Historial (misma tabla que las transiciones normales, vía el puerto compartido).
        _recorder.Records.Should().ContainSingle();
        var record = _recorder.Records[0];
        record.TenantId.Should().Be(tenantId);
        record.ProcedureInstanceId.Should().Be(id);
        record.FromStatus.Should().Be(TramiteEstado.Borrador);
        record.ToStatus.Should().Be(TramiteEstado.Entregado);
        record.ChangedByUserId.Should().Be(userId);
        record.ChangedAt.Should().BeOnOrAfter(antes);
        record.Reason.Should().Be("corrección excepcional");

        // Evento PROPIO que diferencia este cambio de una transición normal del flujo.
        await _repo.Received(1).AddEventAsync(
            Arg.Is<ProcedureInstanceEvent>(e =>
                e.Tipo == AdminCambiarEstadoHandler.EventoTipo
                && e.TenantId == tenantId
                && e.ProcedureInstanceId == id
                && e.CreatedBy == userId
                && e.CreatedAt >= antes),
            Arg.Any<CancellationToken>());

        await _repo.Received(1).SaveChangesWithConcurrencyGuardAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AC1_EstadoDestinoDesconocido_Rechaza_SinAplicarNingunCambio()
    {
        var id = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var instance = Instance(id, tenantId, TramiteEstado.Borrador);
        _repo.GetByIdAsync(id, tenantId, Arg.Any<CancellationToken>()).Returns(instance);

        var command = new AdminCambiarEstadoCommand(id, tenantId, "estado_inventado", null, null);
        var (result, error, _) = await Handler().HandleAsync(command, CancellationToken.None);

        error.Should().Be(TramiteEstadoErrores.EstadoDesconocido);
        result.Should().BeNull();
        instance.Status.Should().Be(TramiteEstado.Borrador, "un estado desconocido no debe aplicar ningún cambio");
        _recorder.Records.Should().BeEmpty();
        await _repo.DidNotReceive().SaveChangesWithConcurrencyGuardAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AC1_ConflictoDeConcurrencia_DevuelveErrorSinResultado()
    {
        var id = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var instance = Instance(id, tenantId, TramiteEstado.Borrador);
        _repo.GetByIdAsync(id, tenantId, Arg.Any<CancellationToken>()).Returns(instance);
        _repo.SaveChangesWithConcurrencyGuardAsync(Arg.Any<CancellationToken>()).Returns(false);

        var command = new AdminCambiarEstadoCommand(id, tenantId, TramiteEstado.Entregado, null, null);
        var (result, error, _) = await Handler().HandleAsync(command, CancellationToken.None);

        error.Should().Be(TramiteEstadoErrores.ConflictoConcurrencia);
        result.Should().BeNull();
    }

    [Fact]
    public async Task AC1_InstanciaNoExiste_DevuelveNotFound()
    {
        var id = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        _repo.GetByIdAsync(id, tenantId, Arg.Any<CancellationToken>()).Returns((ProcedureInstance?)null);

        var command = new AdminCambiarEstadoCommand(id, tenantId, TramiteEstado.Entregado, null, null);
        var (result, error, _) = await Handler().HandleAsync(command, CancellationToken.None);

        error.Should().Be(TramiteEstadoErrores.NoEncontrado);
        result.Should().BeNull();
    }

    // ── AC2 — Aprobado como DESTINO, rechazado (422) ──────────────────────────────────────────────

    [Theory]
    [InlineData(TramiteEstado.Borrador)]
    [InlineData(TramiteEstado.Preparado)]
    [InlineData(TramiteEstado.Entregado)]
    [InlineData(TramiteEstado.Rechazado)]
    [InlineData(TramiteEstado.Anulado)]
    public async Task AC2_AprobadoComoDestino_SeRechazaSinImportarElOrigen(string origen)
    {
        var id = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var instance = Instance(id, tenantId, origen);
        _repo.GetByIdAsync(id, tenantId, Arg.Any<CancellationToken>()).Returns(instance);

        var command = new AdminCambiarEstadoCommand(id, tenantId, TramiteEstado.Aprobado, null, null);
        var (result, error, errorDetail) = await Handler().HandleAsync(command, CancellationToken.None);

        error.Should().Be(TramiteEstadoErrores.AdminAprobadoExcluido);
        errorDetail.Should().NotBeNullOrWhiteSpace();
        result.Should().BeNull();
        instance.Status.Should().Be(origen, "el rechazo no debe aplicar ningún cambio");
        _recorder.Records.Should().BeEmpty();
        await _repo.DidNotReceive().SaveChangesWithConcurrencyGuardAsync(Arg.Any<CancellationToken>());
        await _repo.DidNotReceive().AddEventAsync(Arg.Any<ProcedureInstanceEvent>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AC2_AprobadoComoDestino_NoConsultaLaInstancia()
    {
        // La validación de destino corta ANTES de tocar el repositorio: rechaza sin más IO.
        var id = Guid.NewGuid();
        var tenantId = Guid.NewGuid();

        var command = new AdminCambiarEstadoCommand(id, tenantId, TramiteEstado.Aprobado, null, null);
        var (_, error, _) = await Handler().HandleAsync(command, CancellationToken.None);

        error.Should().Be(TramiteEstadoErrores.AdminAprobadoExcluido);
        await _repo.DidNotReceive().GetByIdAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    // ── AC3 — Aprobado como ORIGEN, rechazado (422) ───────────────────────────────────────────────

    [Theory]
    [InlineData(TramiteEstado.Borrador)]
    [InlineData(TramiteEstado.Preparado)]
    [InlineData(TramiteEstado.Entregado)]
    [InlineData(TramiteEstado.Rechazado)]
    [InlineData(TramiteEstado.Anulado)]
    public async Task AC3_AprobadoComoOrigen_SeRechazaSinImportarElDestino(string destino)
    {
        var id = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var instance = Instance(id, tenantId, TramiteEstado.Aprobado);
        _repo.GetByIdAsync(id, tenantId, Arg.Any<CancellationToken>()).Returns(instance);

        var command = new AdminCambiarEstadoCommand(id, tenantId, destino, null, null);
        var (result, error, errorDetail) = await Handler().HandleAsync(command, CancellationToken.None);

        error.Should().Be(TramiteEstadoErrores.AdminAprobadoExcluido);
        errorDetail.Should().NotBeNullOrWhiteSpace();
        result.Should().BeNull();
        instance.Status.Should().Be(TramiteEstado.Aprobado, "el rechazo no debe aplicar ningún cambio");
        _recorder.Records.Should().BeEmpty();
        await _repo.DidNotReceive().SaveChangesWithConcurrencyGuardAsync(Arg.Any<CancellationToken>());
    }

    // ── AC4 — el backend nunca aplica un cambio con datos incompletos/ambiguos ───────────────────

    [Fact]
    public async Task AC4_ToStatusVacio_SeRechazaComoEstadoDesconocido_SinAplicarCambios()
    {
        // AC4 es responsabilidad del frontend (confirmación explícita, HU #12163); la contribución de
        // este handler es no aplicar NUNCA un cambio "accidental" cuando el destino no es un estado
        // de negocio válido y explícito.
        var id = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var instance = Instance(id, tenantId, TramiteEstado.Borrador);
        _repo.GetByIdAsync(id, tenantId, Arg.Any<CancellationToken>()).Returns(instance);

        var command = new AdminCambiarEstadoCommand(id, tenantId, string.Empty, null, null);
        var (result, error, _) = await Handler().HandleAsync(command, CancellationToken.None);

        error.Should().Be(TramiteEstadoErrores.EstadoDesconocido);
        result.Should().BeNull();
        instance.Status.Should().Be(TramiteEstado.Borrador);
        await _repo.DidNotReceive().SaveChangesWithConcurrencyGuardAsync(Arg.Any<CancellationToken>());
    }
}
