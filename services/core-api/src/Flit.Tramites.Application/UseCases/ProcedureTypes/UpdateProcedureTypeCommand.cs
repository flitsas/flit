using Flit.Tramites.Domain.Enums;
using Flit.Tramites.Domain.Repositories;

namespace Flit.Tramites.Application.UseCases.ProcedureTypes;

public sealed record UpdateProcedureTypeRequest(
    string Name,
    string? Description,
    bool IsActive);

public sealed class UpdateProcedureTypeHandler(IProcedureTypeRepository repository)
{
    public async Task<(ProcedureTypeSummary? Result, string? Error)> HandleAsync(
        Guid id,
        UpdateProcedureTypeRequest request,
        CancellationToken ct = default)
    {
        var entity = await repository.GetByIdAsync(id, ct);
        if (entity is null)
            return (null, "not_found");

        if (entity.PublicationStatus == PublicationStatus.Published)
            return (null, "conflict");

        entity.Name = request.Name;
        entity.Description = request.Description;
        entity.IsActive = request.IsActive;
        entity.UpdatedAt = DateTimeOffset.UtcNow;

        await repository.UpdateAsync(entity, ct);
        await repository.SaveChangesAsync(ct);

        return (CreateProcedureTypeHandler.ToSummary(entity), null);
    }
}
