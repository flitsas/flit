using System.Text.Json;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.Tramites.Estados;

namespace Flit.Tramites.Application.UseCases.ProcedureInstances;

/// <summary>Orden de anulación ADMINISTRATIVA (HU #12160). Ver <see cref="AdminAnularHandler"/>.</summary>
public sealed record AdminAnularCommand(
    Guid InstanceId,
    Guid TenantId,
    string? Reason,
    Guid? ChangedByUserId);

/// <summary>Resumen mínimo de la anulación administrativa aplicada (AC1).</summary>
public sealed record AdminAnularResult(
    Guid Id,
    string PreviousStatus,
    string NewStatus,
    DateTimeOffset ChangedAt);

/// <summary>
/// "Anular" (Feature #12155, HU #12160): mueve un trámite a <see cref="TramiteEstado.Anulado"/> desde
/// CUALQUIER estado de origen, salvo dos excepciones de producto (AC2/AC3) que el admin no puede
/// invadir porque son autoridad del organismo de tránsito.
///
/// <para>
/// <b>Por qué NO reutiliza <see cref="AdminCambiarEstadoHandler"/>:</b> se evaluó (HU #12160) hacer de
/// este endpoint un caso especial de "cambiar estado" con destino fijo = <see cref="TramiteEstado.Anulado"/>,
/// pero las dos reglas de exclusión tienen forma distinta: en "cambiar estado" <c>aprobado</c> se
/// excluye como ORIGEN Y DESTINO indistintamente (cualquiera de los dos basta para rechazar, y el
/// destino se conoce ANTES de tocar el repositorio). Aquí el destino siempre es <c>anulado</c> —nunca
/// puede coincidir con ninguna de las dos exclusiones— y las dos reglas duras (AC2/AC3) aplican
/// EXCLUSIVAMENTE al estado de ORIGEN, que solo se conoce tras cargar la instancia. Forzar el mismo
/// handler habría exigido parametrizar qué exclusiones aplican a origen vs. destino y cuáles no,
/// duplicando la complejidad que se buscaba evitar. Se prefirió un handler propio, corto y explícito,
/// que comparte el mismo puerto de trazabilidad (<see cref="ITramiteTransitionRecorder"/>) y el mismo
/// criterio de "solo el estado, sin efectos colaterales" documentado en
/// <see cref="AdminCambiarEstadoHandler"/>.
/// </para>
///
/// <para>
/// AC2 — <see cref="TramiteEstado.Aprobado"/> como ORIGEN: rechazado siempre (422,
/// <see cref="TramiteEstadoErrores.CannotAnnulApproved"/>). La aprobación es una decisión del
/// organismo de tránsito; el admin no puede descartarla anulando por detrás.
/// </para>
///
/// <para>
/// AC3 — <see cref="TramiteEstado.Revocado"/> como ORIGEN: rechazado siempre (422,
/// <see cref="TramiteEstadoErrores.CannotAnnulRevoked"/>). La revocación es una decisión del
/// organismo de tránsito (deshace su propia aprobación, HU #12166); la anulación administrativa no
/// puede invadirla, igual que con <see cref="TramiteEstado.Aprobado"/> en AC2.
/// </para>
///
/// <para>
/// AC4 (confirmación previa) es responsabilidad del frontend (HU #12163); la contribución de este
/// handler es la misma que en <see cref="AdminCambiarEstadoHandler"/>: nunca aplicar un cambio cuando
/// la validación falla.
/// </para>
///
/// <para>
/// Trazabilidad (AC1): reutiliza <see cref="ITramiteTransitionRecorder"/> para escribir el historial de
/// <c>procedure_instance_status_history</c> (estado anterior, nuevo estado, usuario, fecha y hora) y el
/// evento genérico <c>cambio_estado</c>. Además persiste un evento PROPIO <see cref="EventoTipo"/>
/// (<c>anular_admin</c>) para distinguir esta anulación forzada por un admin de las anulaciones del
/// flujo normal del gestor (borrador/rechazado → anulado vía <see cref="TramiteLifecycleService"/>).
/// </para>
/// </summary>
public sealed class AdminAnularHandler(
    IProcedureInstanceRepository repo,
    ITramiteTransitionRecorder recorder)
{
    /// <summary>Tipo del evento PROPIO de bitácora (distinto del genérico <c>cambio_estado</c> del recorder).</summary>
    public const string EventoTipo = "anular_admin";

    public async Task<(AdminAnularResult? Result, string? Error, string? ErrorDetail)> HandleAsync(
        AdminAnularCommand command,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var instance = await repo.GetByIdAsync(command.InstanceId, command.TenantId, ct).ConfigureAwait(false);
        if (instance is null)
            return (null, TramiteEstadoErrores.NoEncontrado, null);

        var from = instance.Status;

        // AC2 — Aprobado como ORIGEN: rechazado siempre, sin importar que el destino sea fijo (anulado).
        if (string.Equals(from, TramiteEstado.Aprobado, StringComparison.Ordinal))
            return (null, TramiteEstadoErrores.CannotAnnulApproved,
                "El trámite está en estado 'aprobado': la anulación administrativa no puede invadir " +
                "una decisión del organismo de tránsito.");

        // AC3 — Revocado como ORIGEN: rechazado siempre, sin importar que el destino sea fijo (anulado).
        if (string.Equals(from, TramiteEstado.Revocado, StringComparison.Ordinal))
            return (null, TramiteEstadoErrores.CannotAnnulRevoked,
                "El trámite está en estado 'revocado': la anulación administrativa no puede invadir " +
                "una decisión del organismo de tránsito.");

        var now = DateTimeOffset.UtcNow;

        // AC1 — cambio DIRECTO a 'anulado', sin pasar por TramiteStateMachine.IsValidTransition ni por
        // ningún otro gate de TramiteLifecycleService (mismo alcance "solo el estado" de HU #12159, ver
        // XML doc de esta clase).
        instance.Status = TramiteEstado.Anulado;
        instance.UpdatedAt = now;

        var record = new TramiteTransitionRecord(
            command.TenantId,
            instance.Id,
            from,
            TramiteEstado.Anulado,
            command.Reason,
            command.ChangedByUserId,
            now,
            Metadata: JsonSerializer.Serialize(new { origin = "admin" }));

        // Historial (RF05, misma tabla que las transiciones normales) + evento genérico "cambio_estado".
        await recorder.RecordAsync(record, ct).ConfigureAwait(false);

        // Evento PROPIO — diferencia esta anulación de las transiciones normales del flujo (ver XML doc).
        await repo.AddEventAsync(new ProcedureInstanceEvent
        {
            Id = Guid.NewGuid(),
            TenantId = command.TenantId,
            ProcedureInstanceId = instance.Id,
            Tipo = EventoTipo,
            Payload = JsonSerializer.Serialize(new
            {
                from_status = from,
                to_status = TramiteEstado.Anulado,
                reason = command.Reason,
            }),
            CreatedAt = now,
            CreatedBy = command.ChangedByUserId,
        }, ct).ConfigureAwait(false);

        var committed = await repo.SaveChangesWithConcurrencyGuardAsync(ct).ConfigureAwait(false);
        if (!committed)
            return (null, TramiteEstadoErrores.ConflictoConcurrencia,
                "El trámite fue modificado por otro proceso. Recargue el trámite e intente de nuevo.");

        return (new AdminAnularResult(instance.Id, from, TramiteEstado.Anulado, now), null, null);
    }
}
