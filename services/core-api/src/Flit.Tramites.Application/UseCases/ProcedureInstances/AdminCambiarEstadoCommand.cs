using System.Text.Json;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.Tramites.Estados;

namespace Flit.Tramites.Application.UseCases.ProcedureInstances;

/// <summary>Orden de cambio de estado ADMINISTRATIVO (HU #12159). Ver <see cref="AdminCambiarEstadoHandler"/>.</summary>
public sealed record AdminCambiarEstadoCommand(
    Guid InstanceId,
    Guid TenantId,
    string ToStatus,
    string? Reason,
    Guid? ChangedByUserId);

/// <summary>Resumen mínimo de la transición administrativa aplicada (AC1).</summary>
public sealed record AdminCambiarEstadoResult(
    Guid Id,
    string PreviousStatus,
    string NewStatus,
    DateTimeOffset ChangedAt);

/// <summary>
/// "Cambiar estado" (Feature #12155, HU #12159): permite al admin mover un trámite a CUALQUIER estado
/// de negocio conocido SIN pasar por <see cref="TramiteStateMachine.IsValidTransition"/> —a diferencia
/// de <see cref="Estados.TramiteLifecycleService"/>, que es el ÚNICO punto de entrada para los flujos
/// normales del gestor/OT y no debe alterarse ni reutilizarse aquí (rompería sus reglas de negocio).
///
/// <para>
/// ÚNICA regla dura de este endpoint (AC2/AC3): <c>aprobado</c> nunca participa de la transición, ni
/// como origen ni como destino. La aprobación exige el flujo formal (gates de entrega, resolución de
/// mandatario ADR-0036 §D9, regeneración de FUR/mandato) que esta corrección administrativa
/// deliberadamente no reproduce.
/// </para>
///
/// <para>
/// <b>Alcance deliberadamente limitado a "solo el estado" (decisión de producto, HU #12159):</b> a
/// diferencia de <see cref="Estados.TramiteLifecycleService.TransitionAsync"/>, este handler NO
/// replica los efectos colaterales de una transición normal — no libera/asigna placa
/// (<see cref="TramiteEstado.EstadosQueLiberanPlaca"/>), no invalida ni regenera consolidados/FUR, no
/// apaga el flag de subsanación, no resuelve mandatario y no encola notificaciones
/// (<c>ITramiteTransitionPublisher</c>). Es una corrección puntual del campo <c>status</c> para
/// situaciones excepcionales; si el estado quedó inconsistente con esos efectos colaterales, se debe
/// corregir aparte con la acción administrativa correspondiente.
/// </para>
///
/// <para>
/// Trazabilidad (AC1): reutiliza <see cref="ITramiteTransitionRecorder"/> — el MISMO puerto que usan
/// las transiciones normales— para escribir la fila de <c>procedure_instance_status_history</c>
/// (estado anterior, nuevo estado, usuario, fecha y hora) y el evento genérico <c>cambio_estado</c> de
/// la bitácora. Además persiste un evento PROPIO <c>cambio_estado_admin</c> para que quede explícito en
/// el timeline que el cambio lo forzó un admin fuera del flujo normal (y no una transición del
/// gestor/OT).
/// </para>
/// </summary>
public sealed class AdminCambiarEstadoHandler(
    IProcedureInstanceRepository repo,
    ITramiteTransitionRecorder recorder)
{
    /// <summary>Tipo del evento PROPIO de bitácora (distinto del genérico <c>cambio_estado</c> del recorder).</summary>
    public const string EventoTipo = "cambio_estado_admin";

    public async Task<(AdminCambiarEstadoResult? Result, string? Error, string? ErrorDetail)> HandleAsync(
        AdminCambiarEstadoCommand command,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var toStatus = command.ToStatus.Trim().ToLowerInvariant();

        if (!TramiteEstado.EsValido(toStatus))
            return (null, TramiteEstadoErrores.EstadoDesconocido,
                $"'{command.ToStatus}' no es un estado de trámite conocido.");

        // AC2 — Aprobado como DESTINO: rechazado siempre, sin importar el estado origen.
        if (string.Equals(toStatus, TramiteEstado.Aprobado, StringComparison.Ordinal))
            return (null, TramiteEstadoErrores.AdminAprobadoExcluido,
                "El cambio de estado administrativo no puede fijar 'aprobado' como destino: la " +
                "aprobación exige el flujo formal del organismo de tránsito.");

        var instance = await repo.GetByIdAsync(command.InstanceId, command.TenantId, ct).ConfigureAwait(false);
        if (instance is null)
            return (null, TramiteEstadoErrores.NoEncontrado, null);

        var from = instance.Status;

        // AC3 — Aprobado como ORIGEN: rechazado siempre, sin importar el estado destino.
        if (string.Equals(from, TramiteEstado.Aprobado, StringComparison.Ordinal))
            return (null, TramiteEstadoErrores.AdminAprobadoExcluido,
                "El trámite está en estado 'aprobado': el cambio de estado administrativo no puede " +
                "moverlo a otro estado.");

        var now = DateTimeOffset.UtcNow;

        // AC1 — cambio DIRECTO de status, sin pasar por TramiteStateMachine.IsValidTransition ni por
        // ningún otro gate de TramiteLifecycleService (ver XML doc de esta clase: alcance "solo el
        // estado", sin efectos colaterales).
        instance.Status = toStatus;
        instance.UpdatedAt = now;

        var record = new TramiteTransitionRecord(
            command.TenantId,
            instance.Id,
            from,
            toStatus,
            command.Reason,
            command.ChangedByUserId,
            now,
            Metadata: JsonSerializer.Serialize(new { origin = "admin" }));

        // Historial (RF05, misma tabla que las transiciones normales) + evento genérico "cambio_estado".
        await recorder.RecordAsync(record, ct).ConfigureAwait(false);

        // Evento PROPIO — diferencia este cambio de las transiciones normales del flujo (ver XML doc).
        await repo.AddEventAsync(new ProcedureInstanceEvent
        {
            Id = Guid.NewGuid(),
            TenantId = command.TenantId,
            ProcedureInstanceId = instance.Id,
            Tipo = EventoTipo,
            Payload = JsonSerializer.Serialize(new
            {
                from_status = from,
                to_status = toStatus,
                reason = command.Reason,
            }),
            CreatedAt = now,
            CreatedBy = command.ChangedByUserId,
        }, ct).ConfigureAwait(false);

        var committed = await repo.SaveChangesWithConcurrencyGuardAsync(ct).ConfigureAwait(false);
        if (!committed)
            return (null, TramiteEstadoErrores.ConflictoConcurrencia,
                "El trámite fue modificado por otro proceso. Recargue el trámite e intente de nuevo.");

        return (new AdminCambiarEstadoResult(instance.Id, from, toStatus, now), null, null);
    }
}
