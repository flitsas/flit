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

        var statusHistory = new ProcedureInstanceStatusHistory
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ProcedureInstanceId = id,
            FromStatus = ProcedureInstanceStatus.Draft,
            ToStatus = ProcedureInstanceStatus.Submitted,
            ChangedAt = now
        };
        instance.StatusHistory.Add(statusHistory);
        // PK store-generated (uuidv7) con Id ya seteado: marcar Added explícito para forzar
        // INSERT. Sin esto, EF infiere Modified por la PK no-default → UPDATE de 0 filas.
        repo.Add(statusHistory);

        // Instancia trackeada (GetByIdWithDetailsAsync sin AsNoTracking): el change tracker
        // detecta el cambio de estado de la instancia y el status_history nuevo (INSERT). NO se
        // llama Update(): marcaría el status_history nuevo como Modified → UPDATE de 0 filas.
        await repo.SaveChangesAsync(ct);

        return (CreateProcedureInstanceHandler.ToSummary(instance), null);
    }
}
