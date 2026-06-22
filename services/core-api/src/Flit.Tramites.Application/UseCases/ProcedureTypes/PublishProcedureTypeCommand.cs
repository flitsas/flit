using Flit.Tramites.Domain.Enums;
using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.Services;

namespace Flit.Tramites.Application.UseCases.ProcedureTypes;

public sealed class PublishProcedureTypeHandler(
    IProcedureTypeRepository repository,
    IProcedureTypeValidator validator)
{
    public async Task<(ProcedureTypeSummary? Result, string? Error, object? ValidationErrors)> HandleAsync(
        Guid id,
        CancellationToken ct = default)
    {
        var entity = await repository.GetByIdWithDetailsAsync(id, ct);
        if (entity is null)
            return (null, "not_found", null);

        if (entity.PublicationStatus == PublicationStatus.Published)
            return (null, "already_published", null);

        var validationResult = validator.Validate(entity);
        if (!validationResult.IsValid)
            return (null, "validation_failed", validationResult);

        entity.PublicationStatus = PublicationStatus.Published;
        entity.PublishedAt = DateTimeOffset.UtcNow;
        entity.UpdatedAt = DateTimeOffset.UtcNow;

        await repository.UpdateAsync(entity, ct);
        await repository.SaveChangesAsync(ct);

        return (CreateProcedureTypeHandler.ToSummary(entity), null, null);
    }
}
