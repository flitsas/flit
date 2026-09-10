using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.Tramites.Estados;

namespace Flit.Tramites.Application.UseCases.ProcedureInstances;

/// <summary>
/// Cancela la subsanación sobre un trámite en <c>rechazado</c> con flag activo:
/// apaga <c>subsanacion_activa</c> sin cambiar el status de negocio (sigue rechazado).
/// Usado cuando el operador decide que no hace falta corregir/re-radicar.
/// </summary>
public sealed class CancelSubsanacionHandler(IProcedureInstanceRepository repo)
{
    public async Task<(ProcedureInstanceSummary? Result, string? Error)> HandleAsync(
        Guid id,
        Guid tenantId,
        Guid? changedBy,
        CancellationToken ct = default)
    {
        var instance = await repo.GetByIdWithDetailsAsync(id, tenantId, ct);
        if (instance is null)
            return (null, "not_found");

        if (!string.Equals(instance.Status, TramiteEstado.Rechazado, StringComparison.OrdinalIgnoreCase))
            return (null, "not_rechazado");

        if (!instance.SubsanacionActiva)
            return (CreateProcedureInstanceHandler.ToSummary(instance), null);

        var now = DateTimeOffset.UtcNow;
        instance.SubsanacionActiva = false;
        // El baseline solo tiene sentido con la ventana abierta; al cerrarla se suelta para que una
        // subsanación posterior no compare contra un snapshot viejo.
        instance.SubsanacionBaseline = null;
        instance.UpdatedAt = now;
        instance.UpdatedBy = changedBy;

        // Cerrar la ventana tampoco es una transición: no se escribe historial. Antes se insertaba
        // una fila rechazado → rechazado que el timeline mostraba como un rechazo repetido. El quién
        // y el cuándo quedan en updated_by / updated_at y en el log de auditoría de la tabla.

        var committed = await repo.SaveChangesWithConcurrencyGuardAsync(ct).ConfigureAwait(false);
        if (!committed)
            return (null, TramiteEstadoErrores.ConflictoConcurrencia);

        return (CreateProcedureInstanceHandler.ToSummary(instance), null);
    }
}
