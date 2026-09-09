using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.ReadModels;
using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.Tramites.Estados;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Tramites.Application.Tests.UseCases.ProcedureInstances;

/// <summary>
/// HU #12162 — reasignación administrativa del gestor de un trámite: AC1 cambia
/// <see cref="ProcedureInstance.AssignedToUserId"/> conservando <see cref="ProcedureInstance.CreatedByUserId"/>
/// intacto; AC2 rechaza destinos no disponibles (no existe, no pertenece al tenant, inactivo o
/// suspendido) sin aplicar ningún cambio; AC3 queda en el historial (evento propio) con gestor
/// anterior, gestor nuevo, usuario que ejecutó, fecha y hora.
/// </summary>
public sealed class AdminReasignarGestorHandlerTests
{
    private readonly IProcedureInstanceRepository _repo = Substitute.For<IProcedureInstanceRepository>();

    private static ProcedureInstance Instance(
        Guid id, Guid tenantId, Guid createdByUserId, Guid? assignedToUserId = null) => new()
    {
        Id = id,
        TenantId = tenantId,
        ProcedureTypeId = Guid.NewGuid(),
        ReferenceNumber = "TRM-2026-001000",
        Status = TramiteEstado.Entregado,
        CreatedAt = DateTimeOffset.UtcNow,
        CreatedByUserId = createdByUserId,
        AssignedToUserId = assignedToUserId,
    };

    private static GestorCandidate Disponible(Guid id, string nombre = "Nuevo Gestor") =>
        new(id, nombre, BelongsToTenant: true, IsActive: true, IsSuspended: false);

    private AdminReasignarGestorHandler Handler() => new(_repo);

    // ── AC1 — reasigna AssignedToUserId sin tocar CreatedByUserId ─────────────────────────────────

    [Fact]
    public async Task AC1_ReasignaAUnGestorDisponible_CambiaAssignedToUserId_ConservaCreatedByUserId()
    {
        var id = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var creador = Guid.NewGuid();
        var gestorAnterior = Guid.NewGuid();
        var gestorNuevo = Guid.NewGuid();
        var ejecutor = Guid.NewGuid();
        var instance = Instance(id, tenantId, creador, gestorAnterior);
        _repo.GetByIdAsync(id, tenantId, Arg.Any<CancellationToken>()).Returns(instance);
        _repo.FindGestorCandidateAsync(gestorNuevo, tenantId, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(Disponible(gestorNuevo));
        _repo.SaveChangesWithConcurrencyGuardAsync(Arg.Any<CancellationToken>()).Returns(true);

        var command = new AdminReasignarGestorCommand(id, tenantId, gestorNuevo, ejecutor);
        var (result, error, _) = await Handler().HandleAsync(command, CancellationToken.None);

        error.Should().BeNull();
        result.Should().NotBeNull();
        result!.PreviousAssignedToUserId.Should().Be(gestorAnterior);
        result.NewAssignedToUserId.Should().Be(gestorNuevo);
        instance.AssignedToUserId.Should().Be(gestorNuevo);
        instance.CreatedByUserId.Should().Be(creador, "CreatedByUserId es auditoría inmutable: esta HU no la toca");
    }

    [Fact]
    public async Task AC1_TramiteSinGestorAsignado_ReasignaDesdeNull()
    {
        var id = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var gestorNuevo = Guid.NewGuid();
        var instance = Instance(id, tenantId, Guid.NewGuid(), assignedToUserId: null);
        _repo.GetByIdAsync(id, tenantId, Arg.Any<CancellationToken>()).Returns(instance);
        _repo.FindGestorCandidateAsync(gestorNuevo, tenantId, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(Disponible(gestorNuevo));
        _repo.SaveChangesWithConcurrencyGuardAsync(Arg.Any<CancellationToken>()).Returns(true);

        var command = new AdminReasignarGestorCommand(id, tenantId, gestorNuevo, Guid.NewGuid());
        var (result, error, _) = await Handler().HandleAsync(command, CancellationToken.None);

        error.Should().BeNull();
        result!.PreviousAssignedToUserId.Should().BeNull();
        instance.AssignedToUserId.Should().Be(gestorNuevo);
    }

    [Fact]
    public async Task AC1_InstanciaNoExiste_DevuelveNotFound()
    {
        var id = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        _repo.GetByIdAsync(id, tenantId, Arg.Any<CancellationToken>()).Returns((ProcedureInstance?)null);

        var command = new AdminReasignarGestorCommand(id, tenantId, Guid.NewGuid(), Guid.NewGuid());
        var (result, error, _) = await Handler().HandleAsync(command, CancellationToken.None);

        error.Should().Be(TramiteEstadoErrores.NoEncontrado);
        result.Should().BeNull();
    }

    [Fact]
    public async Task AC1_ConflictoDeConcurrencia_DevuelveErrorSinResultado()
    {
        var id = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var gestorNuevo = Guid.NewGuid();
        var instance = Instance(id, tenantId, Guid.NewGuid(), Guid.NewGuid());
        _repo.GetByIdAsync(id, tenantId, Arg.Any<CancellationToken>()).Returns(instance);
        _repo.FindGestorCandidateAsync(gestorNuevo, tenantId, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(Disponible(gestorNuevo));
        _repo.SaveChangesWithConcurrencyGuardAsync(Arg.Any<CancellationToken>()).Returns(false);

        var command = new AdminReasignarGestorCommand(id, tenantId, gestorNuevo, Guid.NewGuid());
        var (result, error, _) = await Handler().HandleAsync(command, CancellationToken.None);

        error.Should().Be(TramiteEstadoErrores.ConflictoConcurrencia);
        result.Should().BeNull();
    }

    // ── AC2 — destino no disponible: rechazado, sin aplicar ningún cambio ─────────────────────────

    [Fact]
    public async Task AC2_UsuarioDestinoNoExiste_SeRechazaSinAplicarCambio()
    {
        var id = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var gestorAnterior = Guid.NewGuid();
        var gestorInexistente = Guid.NewGuid();
        var instance = Instance(id, tenantId, Guid.NewGuid(), gestorAnterior);
        _repo.GetByIdAsync(id, tenantId, Arg.Any<CancellationToken>()).Returns(instance);
        _repo.FindGestorCandidateAsync(
                gestorInexistente, tenantId, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns((GestorCandidate?)null);

        var command = new AdminReasignarGestorCommand(id, tenantId, gestorInexistente, Guid.NewGuid());
        var (result, error, errorDetail) = await Handler().HandleAsync(command, CancellationToken.None);

        error.Should().Be(AdminReasignarGestorHandler.GestorNoDisponible);
        errorDetail.Should().NotBeNullOrWhiteSpace();
        result.Should().BeNull();
        instance.AssignedToUserId.Should().Be(gestorAnterior, "el rechazo no debe aplicar ningún cambio");
        await _repo.DidNotReceive().SaveChangesWithConcurrencyGuardAsync(Arg.Any<CancellationToken>());
        await _repo.DidNotReceive().AddEventAsync(Arg.Any<ProcedureInstanceEvent>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AC2_UsuarioDestinoDeOtroTenant_SeRechaza()
    {
        var id = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var gestorDeOtroTenant = Guid.NewGuid();
        var instance = Instance(id, tenantId, Guid.NewGuid(), Guid.NewGuid());
        _repo.GetByIdAsync(id, tenantId, Arg.Any<CancellationToken>()).Returns(instance);
        _repo.FindGestorCandidateAsync(
                gestorDeOtroTenant, tenantId, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(new GestorCandidate(
                gestorDeOtroTenant, "Ajeno", BelongsToTenant: false, IsActive: true, IsSuspended: false));

        var command = new AdminReasignarGestorCommand(id, tenantId, gestorDeOtroTenant, Guid.NewGuid());
        var (result, error, _) = await Handler().HandleAsync(command, CancellationToken.None);

        error.Should().Be(AdminReasignarGestorHandler.GestorNoDisponible);
        result.Should().BeNull();
    }

    [Fact]
    public async Task AC2_UsuarioDestinoInactivo_SeRechaza()
    {
        var id = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var gestorInactivo = Guid.NewGuid();
        var instance = Instance(id, tenantId, Guid.NewGuid(), Guid.NewGuid());
        _repo.GetByIdAsync(id, tenantId, Arg.Any<CancellationToken>()).Returns(instance);
        _repo.FindGestorCandidateAsync(
                gestorInactivo, tenantId, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(new GestorCandidate(
                gestorInactivo, "Inactivo", BelongsToTenant: true, IsActive: false, IsSuspended: false));

        var command = new AdminReasignarGestorCommand(id, tenantId, gestorInactivo, Guid.NewGuid());
        var (result, error, _) = await Handler().HandleAsync(command, CancellationToken.None);

        error.Should().Be(AdminReasignarGestorHandler.GestorNoDisponible);
        result.Should().BeNull();
    }

    [Fact]
    public async Task AC2_UsuarioDestinoSuspendido_SeRechaza()
    {
        var id = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var gestorSuspendido = Guid.NewGuid();
        var instance = Instance(id, tenantId, Guid.NewGuid(), Guid.NewGuid());
        _repo.GetByIdAsync(id, tenantId, Arg.Any<CancellationToken>()).Returns(instance);
        _repo.FindGestorCandidateAsync(
                gestorSuspendido, tenantId, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(new GestorCandidate(
                gestorSuspendido, "Suspendido", BelongsToTenant: true, IsActive: true, IsSuspended: true));

        var command = new AdminReasignarGestorCommand(id, tenantId, gestorSuspendido, Guid.NewGuid());
        var (result, error, _) = await Handler().HandleAsync(command, CancellationToken.None);

        error.Should().Be(AdminReasignarGestorHandler.GestorNoDisponible);
        result.Should().BeNull();
    }

    // ── AC3 — historial: gestor anterior, gestor nuevo, quién ejecutó, fecha y hora ───────────────

    [Fact]
    public async Task AC3_CambioAplicado_QuedaRegistradoConGestorAnteriorNuevoUsuarioFechaYHora()
    {
        var id = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var gestorAnterior = Guid.NewGuid();
        var gestorNuevo = Guid.NewGuid();
        var ejecutor = Guid.NewGuid();
        var instance = Instance(id, tenantId, Guid.NewGuid(), gestorAnterior);
        _repo.GetByIdAsync(id, tenantId, Arg.Any<CancellationToken>()).Returns(instance);
        _repo.FindGestorCandidateAsync(gestorNuevo, tenantId, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(Disponible(gestorNuevo));
        _repo.SaveChangesWithConcurrencyGuardAsync(Arg.Any<CancellationToken>()).Returns(true);
        var antes = DateTimeOffset.UtcNow;

        var command = new AdminReasignarGestorCommand(id, tenantId, gestorNuevo, ejecutor);
        var (_, error, _) = await Handler().HandleAsync(command, CancellationToken.None);

        error.Should().BeNull();

        await _repo.Received(1).AddEventAsync(
            Arg.Is<ProcedureInstanceEvent>(e =>
                e.Tipo == AdminReasignarGestorHandler.EventoTipo
                && e.TenantId == tenantId
                && e.ProcedureInstanceId == id
                && e.CreatedBy == ejecutor
                && e.CreatedAt >= antes
                && e.Payload!.Contains(gestorAnterior.ToString())
                && e.Payload!.Contains(gestorNuevo.ToString())),
            Arg.Any<CancellationToken>());

        await _repo.Received(1).SaveChangesWithConcurrencyGuardAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AC4_TramiteNoEncontrado_NoAplicaNingunCambioNiRegistraHistorial()
    {
        var id = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        _repo.GetByIdAsync(id, tenantId, Arg.Any<CancellationToken>()).Returns((ProcedureInstance?)null);

        var command = new AdminReasignarGestorCommand(id, tenantId, Guid.NewGuid(), Guid.NewGuid());
        var (result, error, _) = await Handler().HandleAsync(command, CancellationToken.None);

        error.Should().Be(TramiteEstadoErrores.NoEncontrado);
        result.Should().BeNull();
        await _repo.DidNotReceive().SaveChangesWithConcurrencyGuardAsync(Arg.Any<CancellationToken>());
        await _repo.DidNotReceive().AddEventAsync(Arg.Any<ProcedureInstanceEvent>(), Arg.Any<CancellationToken>());
    }
}
