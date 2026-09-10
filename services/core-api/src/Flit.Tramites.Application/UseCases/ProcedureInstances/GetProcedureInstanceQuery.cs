using System.Text.Json;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.Tramites.ValueObjects;

namespace Flit.Tramites.Application.UseCases.ProcedureInstances;

public sealed record ProcedureInstanceFieldValueDto(
    Guid? FormFieldId,
    string FieldKey,
    string? ValueText,
    string? ValueJson,
    string Source);

public sealed record ProcedureInstanceStatusHistoryDto(
    string? FromStatus,
    string ToStatus,
    DateTimeOffset ChangedAt,
    string? Reason,
    // HU #10871 — checklist HÍBRIDO de la observación de subsanación (motivo + items), RECORTADO del
    // metadata jsonb que persiste OtClientProcedureRepository.BuildStatusHistoryMetadata (HU #10871/
    // #10872): NUNCA se expone fieldSnapshot ni ot_tenant_id/approver_tenant_id al cliente (plomería
    // interna / posible PII del snapshot de field_values). Serializado como JSON string
    // `{"motivo":...,"items":[{"campo":...,"detalle":...}]}` — mismo shape que
    // `frontend/lib/tramites/subsanacion.ts` (`parseSubsanacionObservation`) espera en `metadata`. Null
    // si la entrada no trae observación (transición sin checklist, p. ej. aprobar/rechazar).
    string? Metadata = null);

public sealed record ProcedureInstanceActorDto(
    string ActorType,
    string DocumentType,
    string DocumentNumber,
    string FullName,
    // HU #11014 — correo del actor para el expediente: cuando la identidad está apalancada o cubierta por
    // el baúl no hay validación propia de la que leerlo. PII (Ley 1581): solo en respuestas autenticadas.
    string? Email = null);

public sealed record ProcedureInstanceDetailDto(
    Guid Id,
    string ReferenceNumber,
    string Status,
    Guid ProcedureTypeId,
    Guid TenantId,
    DateTimeOffset CreatedAt,
    DateTimeOffset? SubmittedAt,
    DateTimeOffset? CompletedAt,
    IReadOnlyList<ProcedureInstanceFieldValueDto> FieldValues,
    IReadOnlyList<ProcedureInstanceStatusHistoryDto> StatusHistory,
    IReadOnlyList<ProcedureInstanceActorDto> Actors,
    // HU #10349/#10350 — marca de borrador finalizado (datos completos a la espera de la
    // validación de identidad async). Null mientras el borrador no se ha finalizado. El
    // frontend lo usa para el modo "readOnly parcial" del wizard (datos bloqueados, identidad
    // operable). Opcional (default null) para compat con consumidores que no lo lean.
    DateTimeOffset? DraftFinalizedAt = null,
    // Feature #10587 / HU #10785 — sub-estado interno de la ruta de placa, ortogonal al Status global
    // (que permanece en 'entregado'): null (sin ruta de placa) | 'preasignado' | 'asignado'. El frontend
    // lo usa para el badge secundario, el panel de SOAT y las acciones del OT. Opcional (default null).
    string? PlateFlowStatus = null,
    // HU #10879 — paso actual persistido del wizard (Key del paso). Prima como punto de retoma al
    // reabrir el borrador (AC2); null = el frontend cae al paso derivado de los gates. Opcional (default null).
    string? CurrentStep = null,
    /// <summary>Subsanación activa sobre rechazado (edición sin cambiar status).</summary>
    bool SubsanacionActiva = false,
    /// <summary>Veces que se activó la subsanación en este expediente.</summary>
    int SubsanacionCount = 0,
    // HU #10536 — marca de prioridad. Vive en una columna del expediente, no en FieldValues, así que
    // el detalle era la única lectura del wizard que NO la traía: el paso 1 pinta el interruptor de
    // "Trámite prioritario" y, al volver sobre un trámite ya creado, no tenía de dónde rehidratarlo.
    // Opcional (default false) para no romper a los consumidores que no la lean.
    bool Prioritario = false);

public sealed class GetProcedureInstanceHandler(IProcedureInstanceRepository repo)
{
    public async Task<(ProcedureInstanceDetailDto? Result, string? Error)> HandleAsync(
        Guid id,
        Guid tenantId,
        CancellationToken ct = default)
    {
        var instance = await repo.GetByIdWithDetailsAsync(id, tenantId, ct);
        if (instance is null)
            return (null, "not_found");

        return (ToDetail(instance), null);
    }

    internal static ProcedureInstanceDetailDto ToDetail(ProcedureInstance e) =>
        new(
            e.Id,
            e.ReferenceNumber,
            e.Status,
            e.ProcedureTypeId,
            e.TenantId,
            e.CreatedAt,
            e.SubmittedAt,
            e.CompletedAt,
            e.FieldValues
                .Select(f => new ProcedureInstanceFieldValueDto(f.FormFieldId, f.FieldKey, f.ValueText, f.ValueJson, f.Source))
                .ToList(),
            // Trazabilidad cronológica: EF materializa la colección sin orden garantizado — se
            // ordena por fecha/hora con desempate estable por Id (mismo criterio que el endpoint
            // de status-history) para que el Expediente pinte las transiciones en orden real.
            e.StatusHistory
                .OrderBy(h => h.ChangedAt)
                .ThenBy(h => h.Id)
                .Select(h => new ProcedureInstanceStatusHistoryDto(
                    h.FromStatus, h.ToStatus, h.ChangedAt, h.Reason, BuildObservationMetadata(h.Metadata)))
                .ToList(),
            e.Actors
                .Select(a => new ProcedureInstanceActorDto(a.ActorType, a.DocumentType, a.DocumentNumber, a.FullName, a.Email))
                .ToList(),
            e.DraftFinalizedAt,
            e.PlateFlowStatus,
            e.CurrentStep,
            e.SubsanacionActiva,
            e.SubsanacionCount,
            e.Prioritario);

    /// <summary>
    /// HU #10871 — recorta el metadata jsonb persistido en <c>procedure_instance_status_history</c> al
    /// checklist HÍBRIDO (<c>motivo</c> + <c>items</c>) que el frontend necesita para pintar el panel de
    /// subsanación (<c>SubsanacionObservation</c>, HU #10871/#10872). Deliberadamente NO reenvía
    /// <c>FieldSnapshot</c> (baseline interno de re-radicación) ni <c>ot_tenant_id</c>/
    /// <c>approver_tenant_id</c> (plomería de auditoría cross-tenant) — esas claves quedan solo en la
    /// persistencia. Devuelve <c>null</c> cuando la entrada no trae observación (transiciones sin
    /// checklist, p. ej. aprobar/rechazar sin subsanación) para no ensuciar el contrato con JSON vacío.
    /// </summary>
    private static string? BuildObservationMetadata(string? metadataJson)
    {
        var observation = SubsanacionObservation.FromJson(metadataJson);
        if (observation is null || (observation.Motivo is null && observation.Items.Count == 0))
            return null;

        return JsonSerializer.Serialize(new
        {
            motivo = observation.Motivo,
            items = observation.Items.Select(i => new { campo = i.Campo, detalle = i.Detalle }),
        });
    }
}
