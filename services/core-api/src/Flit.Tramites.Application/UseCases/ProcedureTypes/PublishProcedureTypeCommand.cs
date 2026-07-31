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

        // CFD-01 / AC#5 (BE-01-AC-07): re-publicar un tipo YA publicado antes (PublishedAt != null)
        // incrementa la versión semántica; la primera publicación conserva version=1. Los trámites en
        // curso no se ven afectados: leen su snapshot inmutable (procedure_type_snapshots), no la
        // versión live del tipo.
        if (entity.PublishedAt is not null)
            entity.Version += 1;

        entity.PublicationStatus = PublicationStatus.Published;
        entity.PublishedAt = DateTimeOffset.UtcNow;
        entity.UpdatedAt = DateTimeOffset.UtcNow;

        await repository.UpdateAsync(entity, ct);
        await repository.SaveChangesAsync(ct);

        return (CreateProcedureTypeHandler.ToSummary(entity), null, null);
    }
}
