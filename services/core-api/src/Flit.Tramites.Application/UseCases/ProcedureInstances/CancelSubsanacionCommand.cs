using System.Text.Json;
using Flit.Tramites.Application.Tracking;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.Tramites.Estados;

namespace Flit.Tramites.Application.UseCases.ProcedureInstances;

/// <summary>
/// Cancela la subsanación sobre un trámite en <c>rechazado</c> con flag activo:
/// apaga <c>subsanacion_activa</c> sin cambiar el status de negocio (sigue rechazado).
/// Usado cuando el operador decide que no hace falta corregir/re-radicar.
///
/// HU #14177 — escribe un evento <c>dato_modificado</c> con etiqueta «Subsanación»,
/// anterior «Activada», nuevo «Desactivada».
/// </summary>
public sealed class CancelSubsanacionHandler(IProcedureInstanceRepository repo)
{
    // camelCase para el payload — mismo options que el fallback de actores (HU #14172).
    private static readonly JsonSerializerOptions s_json = new(JsonSerializerDefaults.Web);

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

        // HU #14177 — bitácora de cambio de dato campo a campo.
        var evtSubsanacion = new ProcedureInstanceEvent
        {
            Id                  = Guid.NewGuid(),
            TenantId            = tenantId,
            ProcedureInstanceId = instance.Id,
            Tipo                = ProcedureFieldChangeRecorder.EventType,
            Payload             = JsonSerializer.Serialize(
                new { etiqueta = "Subsanación", campo = "subsanacion", anterior = "Activada", nuevo = "Desactivada" },
                s_json),
            CreatedAt           = now,
            CreatedBy           = changedBy,
        };
        await repo.AddEventAsync(evtSubsanacion, ct);

        var committed = await repo.SaveChangesWithConcurrencyGuardAsync(ct).ConfigureAwait(false);
        if (!committed)
            return (null, TramiteEstadoErrores.ConflictoConcurrencia);

        return (CreateProcedureInstanceHandler.ToSummary(instance), null);
    }
}
