using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.Tramites.Estados;

namespace Flit.Tramites.Application.UseCases.ProcedureInstances;

/// <summary>
/// Gestor/radicador en sub-estado <c>asignado</c>: marca checks opcionales (SOAT / impuesto) y
/// avanza a <c>terminado</c> para que el OT pueda aprobar/rechazar.
/// </summary>
public sealed record CompletePlateFlowRequest(
    bool? SoatPagado = null,
    bool? ImpuestoDepartamentalPagado = null);

public sealed class CompletePlateFlowHandler(IProcedureInstanceRepository repo)
{
    public async Task<(ProcedureInstanceSummary? Result, string? Error)> HandleAsync(
        Guid id,
        Guid tenantId,
        Guid? changedBy,
        CompletePlateFlowRequest request,
        CancellationToken ct = default)
    {
        var instance = await repo.GetByIdWithDetailsAsync(id, tenantId, ct);
        if (instance is null)
            return (null, "not_found");

        if (instance.Status != TramiteEstado.Entregado)
            return (null, TramiteEstadoErrores.TransicionNoPermitida);

        if (instance.PlateFlowStatus != PlateFlowStatus.Asignado)
            return (null, "plate_flow_not_asignado");

        if (!PlateFlowStateMachine.IsValidTransition(PlateFlowStatus.Asignado, PlateFlowStatus.Terminado))
            return (null, TramiteEstadoErrores.TransicionNoPermitida);

        var now = DateTimeOffset.UtcNow;

        // Fase 1 — escribir checks ESTANDO en asignado (el trigger de inmutabilidad solo lo
        // permite en ese sub-estado) y persistir antes de cambiar plate_flow_status.
        UpsertBoolField(instance, tenantId, id, PlateFlowCheckFields.SoatPagado, request.SoatPagado, now);
        UpsertBoolField(instance, tenantId, id, PlateFlowCheckFields.ImpuestoDepartamentalPagado, request.ImpuestoDepartamentalPagado, now);

        if (!await repo.SaveChangesWithConcurrencyGuardAsync(ct))
            return (null, TramiteEstadoErrores.ConflictoConcurrencia);

        // Fase 2 — avanzar a terminado (mismo patrón que AssignPlate: preasignado→asignado).
        instance.PlateFlowStatus = PlateFlowStatus.Terminado;
        instance.UpdatedAt = now;
        instance.UpdatedBy = changedBy;

        if (!await repo.SaveChangesWithConcurrencyGuardAsync(ct))
            return (null, TramiteEstadoErrores.ConflictoConcurrencia);

        return (CreateProcedureInstanceHandler.ToSummary(instance), null);
    }

    private void UpsertBoolField(
        ProcedureInstance instance,
        Guid tenantId,
        Guid instanceId,
        string key,
        bool? value,
        DateTimeOffset now)
    {
        if (value is null)
            return;

        var text = value.Value ? "true" : "false";
        var existing = instance.FieldValues.FirstOrDefault(f =>
            string.Equals(f.FieldKey, key, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            existing.ValueText = text;
            existing.Source = "user";
            existing.UpdatedAt = now;
            return;
        }

        var fieldValue = new ProcedureInstanceFieldValue
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ProcedureInstanceId = instanceId,
            FieldKey = key,
            ValueText = text,
            Source = "user",
            CreatedAt = now,
        };
        instance.FieldValues.Add(fieldValue);
        // PK store-generated (uuidv7) con Id ya seteado: Added explícito para forzar INSERT.
        // Sin esto EF infiere Modified → UPDATE de 0 filas → DbUpdateConcurrencyException.
        repo.Add(fieldValue);
    }
}
