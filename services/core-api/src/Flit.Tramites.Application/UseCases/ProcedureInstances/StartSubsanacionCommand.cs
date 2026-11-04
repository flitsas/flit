using System.Text.Json;
using Flit.Tramites.Application.Tracking;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Integration;
using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.Tramites.Estados;
using Flit.Tramites.Domain.Tramites.Services;
using Flit.Tramites.Domain.Tramites.ValueObjects;

namespace Flit.Tramites.Application.UseCases.ProcedureInstances;

/// <summary>
/// Activa la subsanación sobre un trámite en <c>rechazado</c>: enciende
/// <c>subsanacion_activa</c>, incrementa el contador y captura el snapshot de campos como
/// baseline del diff de re-radicación. NO cambia el status de negocio.
///
/// HU #14177 — escribe un evento <c>dato_modificado</c> con etiqueta «Subsanación»,
/// anterior «Desactivada», nuevo «Activada» (con motivo si se proporcionó).
/// </summary>
/// <remarks>
/// HU #14051: <paramref name="publisher"/> es opcional (sin él, el comportamiento es el de siempre). Con él, justo antes de
/// guardar se avisa que se abrió la subsanación, en la misma unidad de trabajo (outbox).
/// </remarks>
public sealed class StartSubsanacionHandler(IProcedureInstanceRepository repo, ITramiteSubsanacionPublisher? publisher = null)
{
    // camelCase para el payload — mismo options que el fallback de actores (HU #14172).
    private static readonly JsonSerializerOptions s_json = new(JsonSerializerDefaults.Web);

    public async Task<(ProcedureInstanceSummary? Result, string? Error)> HandleAsync(
        Guid id,
        Guid tenantId,
        Guid? changedBy,
        string? reason = null,
        CancellationToken ct = default)
    {
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
        // ADR-0059 (HU #12597 AC6) — el distintivo «Rechazado preasignación» sirve para priorizar; una vez
        // el gestor toma el trámite en subsanación, ya no hay nada que priorizar.
        instance.RejectedFrom = null;
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

        // HU #14177 — el motivo entra en la bitácora cuando existe: el que manda el llamador,
        // o el del rechazo del organismo si el gestor no escribe otro.
        var motivo = string.IsNullOrWhiteSpace(reason) ? MotivoDelRechazo(instance) : reason.Trim();
        var nuevoValor = motivo is null ? "Activada" : $"Activada · {motivo}";
        var evtSubsanacion = new ProcedureInstanceEvent
        {
            Id                  = Guid.NewGuid(),
            TenantId            = tenantId,
            ProcedureInstanceId = instance.Id,
            Tipo                = ProcedureFieldChangeRecorder.EventType,
            Payload             = JsonSerializer.Serialize(
                new { etiqueta = "Subsanación", campo = "subsanacion", anterior = "Desactivada", nuevo = nuevoValor },
                s_json),
            CreatedAt           = now,
            CreatedBy           = changedBy,
        };
        await repo.AddEventAsync(evtSubsanacion, ct);

        if (publisher is not null)
            await publisher.EnqueueAsync(instance, ct).ConfigureAwait(false);

        var committed = await repo.SaveChangesWithConcurrencyGuardAsync(ct).ConfigureAwait(false);
        if (!committed)
            return (null, TramiteEstadoErrores.ConflictoConcurrencia);

        return (CreateProcedureInstanceHandler.ToSummary(instance), null);
    }

    /// <summary>Motivo del rechazo vigente, si el historial lo trae. No inventa uno.</summary>
    private static string? MotivoDelRechazo(ProcedureInstance instance)
    {
        var reason = instance.StatusHistory
            .Where(h => string.Equals(h.ToStatus, TramiteEstado.Rechazado, StringComparison.OrdinalIgnoreCase)
                        && !string.IsNullOrWhiteSpace(h.Reason))
            .OrderByDescending(h => h.ChangedAt)
            .Select(h => h.Reason!.Trim())
            .FirstOrDefault();

        if (string.IsNullOrWhiteSpace(reason))
            return null;

        return reason.Length <= 300 ? reason : reason[..300];
    }
}
