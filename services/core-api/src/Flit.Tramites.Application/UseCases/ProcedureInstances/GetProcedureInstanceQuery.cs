using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Repositories;

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
    string? Reason);

public sealed record ProcedureInstanceActorDto(
    string ActorType,
    string DocumentType,
    string DocumentNumber,
    string FullName);

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
    DateTimeOffset? DraftFinalizedAt = null);

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
            e.StatusHistory
                .Select(h => new ProcedureInstanceStatusHistoryDto(h.FromStatus, h.ToStatus, h.ChangedAt, h.Reason))
                .ToList(),
            e.Actors
                .Select(a => new ProcedureInstanceActorDto(a.ActorType, a.DocumentType, a.DocumentNumber, a.FullName))
                .ToList(),
            e.DraftFinalizedAt);
}
