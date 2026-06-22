using Flit.Tramites.Domain.Enums;
using Flit.Tramites.Domain.Repositories;

namespace Flit.Tramites.Application.UseCases.ProcedureTypes;

public sealed class DeleteProcedureTypeHandler(IProcedureTypeRepository repository)
{
    public async Task<string?> HandleAsync(Guid id, CancellationToken ct = default)
    {
        var entity = await repository.GetByIdAsync(id, ct);
        if (entity is null)
            return "not_found";

        bool hasInstances = await repository.HasInstancesAsync(id, ct);
        if (hasInstances)
            return "conflict";

        if (entity.PublicationStatus == PublicationStatus.Published)
        {
            entity.PublicationStatus = PublicationStatus.Archived;
        }
        else
        {
            entity.IsActive = false;
            entity.PublicationStatus = PublicationStatus.Archived;
        }

        entity.UpdatedAt = DateTimeOffset.UtcNow;
        await repository.UpdateAsync(entity, ct);
        await repository.SaveChangesAsync(ct);
        return null;
    }
}
