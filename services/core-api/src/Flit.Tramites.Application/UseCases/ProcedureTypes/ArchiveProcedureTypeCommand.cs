using Flit.Tramites.Domain.Enums;
using Flit.Tramites.Domain.Repositories;

namespace Flit.Tramites.Application.UseCases.ProcedureTypes;

public sealed class ArchiveProcedureTypeHandler(IProcedureTypeRepository repository)
{
    public async Task<(ProcedureTypeSummary? Result, string? Error)> HandleAsync(
        Guid id,
        CancellationToken ct = default)
    {
        var entity = await repository.GetByIdAsync(id, ct);
        if (entity is null)
            return (null, "not_found");

        if (entity.PublicationStatus == PublicationStatus.Archived)
            return (null, "already_archived");

        entity.PublicationStatus = PublicationStatus.Archived;
        entity.UpdatedAt = DateTimeOffset.UtcNow;

        await repository.UpdateAsync(entity, ct);
        await repository.SaveChangesAsync(ct);

        return (CreateProcedureTypeHandler.ToSummary(entity), null);
    }
}
