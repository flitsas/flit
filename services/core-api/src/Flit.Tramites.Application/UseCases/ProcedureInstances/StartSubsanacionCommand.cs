using Flit.Tramites.Domain.Entities;
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

        // Id pre-asignado + PK store-generated (uuidv7): hay que marcar Added explícito
        // (mismo patrón que TramiteTransitionRecorder / PatchFieldValues). Si solo se agrega a la
        // colección, EF infiere Modified → UPDATE de 0 filas → falso conflicto_concurrencia.
        var history = new ProcedureInstanceStatusHistory
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ProcedureInstanceId = instance.Id,
            FromStatus = TramiteEstado.Rechazado,
            ToStatus = TramiteEstado.Rechazado,
            ChangedAt = now,
            ChangedBy = changedBy,
            Reason = null,
            Metadata = observation.ToJson(),
        };
        instance.StatusHistory.Add(history);
        repo.Add(history);

        var committed = await repo.SaveChangesWithConcurrencyGuardAsync(ct).ConfigureAwait(false);
        if (!committed)
            return (null, TramiteEstadoErrores.ConflictoConcurrencia);

        return (CreateProcedureInstanceHandler.ToSummary(instance), null);
    }
}
