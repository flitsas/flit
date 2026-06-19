using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Enums;
using Flit.Tramites.Domain.Repositories;

namespace Flit.Tramites.Application.UseCases.ProcedureInstances;

public sealed record FieldValueInput(
    Guid FormFieldId,
    string FieldKey,
    string? ValueText,
    string? ValueJson);

public sealed record PatchFieldValuesRequest(IReadOnlyList<FieldValueInput> Items);

public sealed class PatchFieldValuesHandler(IProcedureInstanceRepository repo)
{
    public async Task<(ProcedureInstanceDetailDto? Result, string? Error)> HandleAsync(
        Guid id,
        Guid tenantId,
        PatchFieldValuesRequest request,
        CancellationToken ct = default)
    {
        var instance = await repo.GetByIdWithDetailsAsync(id, tenantId, ct);
        if (instance is null)
            return (null, "not_found");

        if (instance.Status != ProcedureInstanceStatus.Draft)
            return (null, "not_draft");

        var now = DateTimeOffset.UtcNow;

        foreach (var item in request.Items)
        {
            var existing = instance.FieldValues.FirstOrDefault(f => f.FieldKey == item.FieldKey);
            if (existing is not null)
            {
                existing.ValueText = item.ValueText;
                existing.ValueJson = item.ValueJson;
                existing.Source = "user";
                existing.UpdatedAt = now;
            }
            else
            {
                instance.FieldValues.Add(new ProcedureInstanceFieldValue
                {
                    Id = Guid.NewGuid(),
                    TenantId = tenantId,
                    ProcedureInstanceId = id,
                    FormFieldId = item.FormFieldId,
                    FieldKey = item.FieldKey,
                    ValueText = item.ValueText,
                    ValueJson = item.ValueJson,
                    Source = "user",
                    CreatedAt = now
                });
            }
        }

        await repo.UpdateAsync(instance, ct);
        await repo.SaveChangesAsync(ct);

        return (GetProcedureInstanceHandler.ToDetail(instance), null);
    }
}
