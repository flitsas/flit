using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Enums;
using Flit.Tramites.Domain.Repositories;

namespace Flit.Tramites.Application.UseCases.ProcedureInstances;

public sealed class SubmitProcedureInstanceHandler(
    IProcedureInstanceRepository repo,
    IProcedureTypeRepository typeRepo)
{
    public async Task<(ProcedureInstanceSummary? Result, string? Error)> HandleAsync(
        Guid id,
        Guid tenantId,
        CancellationToken ct = default)
    {
        var instance = await repo.GetByIdWithDetailsAsync(id, tenantId, ct);
        if (instance is null)
            return (null, "not_found");

        if (instance.Status != ProcedureInstanceStatus.Draft)
            return (null, "not_draft");

        var procedureType = await typeRepo.GetByIdAsync(instance.ProcedureTypeId, ct);
        if (procedureType is null || procedureType.PublicationStatus != PublicationStatus.Published)
            return (null, "not_published");

        var now = DateTimeOffset.UtcNow;
        instance.Status = ProcedureInstanceStatus.Submitted;
        instance.SubmittedAt = now;
        instance.UpdatedAt = now;

        instance.StatusHistory.Add(new ProcedureInstanceStatusHistory
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ProcedureInstanceId = id,
            FromStatus = ProcedureInstanceStatus.Draft,
            ToStatus = ProcedureInstanceStatus.Submitted,
            ChangedAt = now
        });

        await repo.UpdateAsync(instance, ct);
        await repo.SaveChangesAsync(ct);

        return (CreateProcedureInstanceHandler.ToSummary(instance), null);
    }
}
