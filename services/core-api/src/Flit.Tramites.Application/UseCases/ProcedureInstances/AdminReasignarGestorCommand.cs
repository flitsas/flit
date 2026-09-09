using System.Text.Json;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.ReadModels;
using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.Tramites.Estados;

namespace Flit.Tramites.Application.UseCases.ProcedureInstances;

/// <summary>Orden de reasignación ADMINISTRATIVA del gestor de un trámite (HU #12162). Ver
/// <see cref="AdminReasignarGestorHandler"/>.</summary>
public sealed record AdminReasignarGestorCommand(
    Guid InstanceId,
    Guid TenantId,
    Guid NewAssignedToUserId,
    Guid? ChangedByUserId);

/// <summary>Resumen mínimo de la reasignación aplicada (AC1).</summary>
public sealed record AdminReasignarGestorResult(
    Guid Id,
    Guid? PreviousAssignedToUserId,
    Guid NewAssignedToUserId,
    DateTimeOffset ChangedAt);

/// <summary>
/// "Reasignar gestor" (Feature #12155, HU #12162): cambia
/// <see cref="ProcedureInstance.AssignedToUserId"/> — el gestor HOY responsable del trámite — a otro
/// usuario disponible del mismo tenant, SIN tocar <see cref="ProcedureInstance.CreatedByUserId"/> (quién
/// radicó, auditoría inmutable — ver XML doc de esa propiedad y de la migración de esquema de la HU).
///
/// <para>
/// <b>Colisión terminológica "gestor" (nota del database-agent) — decisión de esta HU:</b> antes de esta
/// HU, <c>ListProcedureInstancesHandler</c> (listado del dashboard) llamaba "gestor" al usuario resuelto
/// desde <c>CreatedByUserId</c> (quien radicó). Con <see cref="ProcedureInstance.AssignedToUserId"/> el
/// término pasa a tener dos acepciones posibles. Se resuelve así, en <c>ListProcedureInstancesHandler
/// .ToSummary</c>: si <c>AssignedToUserId</c> tiene valor, ESE es el "gestor" que expone la columna
/// <c>GestorNombre</c>; si es <c>null</c> (trámite nunca reasignado), la columna sigue cayendo a
/// <c>CreatedByUserId</c> como fallback de PRESENTACIÓN — el mismo criterio que ya fijó la migración de
/// esquema para no backfillear la columna nueva. La ACCIÓN de reasignar, en cambio, SIEMPRE opera sobre
/// <c>AssignedToUserId</c>: nunca sobre <c>CreatedByUserId</c>, que ningún endpoint de esta HU modifica.
/// </para>
///
/// <para>
/// AC2 — el usuario destino debe ser un gestor DISPONIBLE del mismo tenant: existe, no está eliminado,
/// pertenece al tenant (mismo criterio de pertenencia que
/// <c>IUserRoleAssignmentRepository.UserBelongsToTenantAsync</c> del módulo Security — ver XML doc de
/// <see cref="GestorCandidate"/>), está <c>active</c> y no tiene una suspensión
/// temporal vigente ahora mismo. Cualquier otra combinación rechaza la operación (422,
/// <see cref="GestorNoDisponible"/>) SIN aplicar ningún cambio.
/// </para>
///
/// <para>
/// AC3 — trazabilidad: persiste un evento PROPIO <see cref="EventoTipo"/> en
/// <see cref="ProcedureInstanceEvent"/> (mismo patrón <c>AddEventAsync</c> que el resto de este lote de
/// HUs administrativas — #12159/#12160/#12161) con el gestor ANTERIOR, el gestor NUEVO, quién ejecutó
/// (<c>CreatedBy</c>) y cuándo (<c>CreatedAt</c>). No se usa <see cref="ITramiteTransitionRecorder"/>
/// (esa tabla es para transiciones de <c>Status</c>, RF05) porque reasignar el gestor NO cambia el
/// estado del trámite — es un campo de asignación operativa ortogonal al ciclo de vida.
/// </para>
///
/// <para>
/// AC-selector: los candidatos disponibles para el selector del frontend (HU #12163) los resuelve
/// <see cref="IProcedureInstanceRepository.ListAvailableGestoresAsync"/> — endpoint de solo lectura
/// separado, ver <c>AdminReasignarGestorEndpoints</c>.
/// </para>
/// </summary>
public sealed class AdminReasignarGestorHandler(IProcedureInstanceRepository repo)
{
    /// <summary>Tipo del evento PROPIO de bitácora (historial visible en el dashboard admin).</summary>
    public const string EventoTipo = "reasignar_gestor_admin";

    /// <summary>
    /// AC2 — el usuario destino no existe, no pertenece al tenant, está inactivo o está suspendido
    /// (422). No es un error de ESTADO DE TRÁMITE (por eso no vive en <see cref="TramiteEstadoErrores"/>),
    /// sino del USUARIO destino — mismo criterio que <c>AdminReenviarValidacionIdentidadHandler
    /// .IdentidadAprobada</c>, que tampoco es un error de estado del trámite.
    /// </summary>
    public const string GestorNoDisponible = "gestor_no_disponible";

    public async Task<(AdminReasignarGestorResult? Result, string? Error, string? ErrorDetail)> HandleAsync(
        AdminReasignarGestorCommand command,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var instance = await repo.GetByIdAsync(command.InstanceId, command.TenantId, ct).ConfigureAwait(false);
        if (instance is null)
            return (null, TramiteEstadoErrores.NoEncontrado, null);

        var now = DateTimeOffset.UtcNow;

        // AC2 — disponibilidad del gestor destino: existe, pertenece al tenant, activo, sin suspensión.
        var candidate = await repo
            .FindGestorCandidateAsync(command.NewAssignedToUserId, command.TenantId, now, ct)
            .ConfigureAwait(false);
        if (candidate is null || !candidate.IsAvailable)
            return (null, GestorNoDisponible,
                "El usuario destino no existe, no pertenece a este tenant, está inactivo o tiene una " +
                "suspensión vigente: no se puede reasignar el gestor a un usuario no disponible.");

        var previous = instance.AssignedToUserId;

        // AC1 — cambia ÚNICAMENTE AssignedToUserId; CreatedByUserId (quién radicó) no se toca jamás
        // por este handler.
        instance.AssignedToUserId = candidate.Id;
        instance.UpdatedAt = now;

        // AC3 — historial: evento PROPIO con gestor anterior, gestor nuevo, quién ejecutó y cuándo.
        await repo.AddEventAsync(new ProcedureInstanceEvent
        {
            Id = Guid.NewGuid(),
            TenantId = command.TenantId,
            ProcedureInstanceId = instance.Id,
            Tipo = EventoTipo,
            Payload = JsonSerializer.Serialize(new
            {
                previous_assigned_to_user_id = previous,
                new_assigned_to_user_id = candidate.Id,
            }),
            CreatedAt = now,
            CreatedBy = command.ChangedByUserId,
        }, ct).ConfigureAwait(false);

        var committed = await repo.SaveChangesWithConcurrencyGuardAsync(ct).ConfigureAwait(false);
        if (!committed)
            return (null, TramiteEstadoErrores.ConflictoConcurrencia,
                "El trámite fue modificado por otro proceso. Recargue el trámite e intente de nuevo.");

        return (new AdminReasignarGestorResult(instance.Id, previous, candidate.Id, now), null, null);
    }
}

/// <summary>
/// HU #12162 (AC-selector) — lista los gestores DISPONIBLES del tenant para el selector de
/// reasignación (consumido por el frontend de HU #12163). Delgado a propósito: solo envuelve
/// <see cref="IProcedureInstanceRepository.ListAvailableGestoresAsync"/>, que ya filtra
/// disponibilidad en la consulta (ver XML doc del método en el repositorio para por qué NO se
/// reutiliza <c>GET /api/v1/security/users</c>).
/// </summary>
public sealed class ListGestoresDisponiblesHandler(IProcedureInstanceRepository repo)
{
    public Task<IReadOnlyList<GestorOption>> HandleAsync(Guid tenantId, CancellationToken ct = default) =>
        repo.ListAvailableGestoresAsync(tenantId, DateTimeOffset.UtcNow, ct);
}
