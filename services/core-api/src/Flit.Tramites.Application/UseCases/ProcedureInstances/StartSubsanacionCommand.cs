using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.Tramites.Estados;
using Flit.Tramites.Domain.Tramites.Services;
using Flit.Tramites.Domain.Tramites.ValueObjects;

namespace Flit.Tramites.Application.UseCases.ProcedureInstances;

/// <summary>
/// Activa la subsanación sobre un trámite en <c>rechazado</c>: enciende
/// <c>subsanacion_activa</c>, incrementa el contador y captura el snapshot de campos como
/// baseline del diff de re-radicación. NO cambia el status de negocio.
/// </summary>
public sealed class StartSubsanacionHandler(IProcedureInstanceRepository repo)
{
    public async Task<(ProcedureInstanceSummary? Result, string? Error)> HandleAsync(
        Guid id,
        Guid tenantId,
        Guid? changedBy,
        string? reason = null,
        CancellationToken ct = default)
    {
        // `reason` se ignora a propósito: el motivo visible es el del OT (entregado→rechazado),
        // no un texto del operador al activar el flag.
        _ = reason;

        var instance = await repo.GetByIdWithDetailsAsync(id, tenantId, ct);
        if (instance is null)
            return (null, "not_found");

        if (!string.Equals(instance.Status, TramiteEstado.Rechazado, StringComparison.OrdinalIgnoreCase))
            return (null, "not_rechazado");

        if (instance.SubsanacionActiva)
            return (CreateProcedureInstanceHandler.ToSummary(instance), null);

        var now = DateTimeOffset.UtcNow;
        instance.SubsanacionActiva = true;
        instance.SubsanacionCount += 1;
        instance.UpdatedAt = now;
        instance.UpdatedBy = changedBy;

        // Solo baseline de campos para el diff de re-radicación. NO escribir Motivo/Reason
        // del operador: el motivo del OT permanece intacto en el historial de rechazo real.
        var observation = new SubsanacionObservation
        {
            FieldSnapshot = FieldValueSnapshot.Capture(instance.FieldValues),
        };

        // El baseline va en la instancia, NO en el historial de estados. Activar la subsanación no
        // es una transición: antes se escribía una fila rechazado → rechazado solo para transportar
        // este snapshot, y el timeline del trámite la mostraba como un segundo rechazo.
        instance.SubsanacionBaseline = observation.ToJson();

        var committed = await repo.SaveChangesWithConcurrencyGuardAsync(ct).ConfigureAwait(false);
        if (!committed)
            return (null, TramiteEstadoErrores.ConflictoConcurrencia);

        return (CreateProcedureInstanceHandler.ToSummary(instance), null);
    }
}
