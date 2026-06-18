using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Repositories;

namespace Flit.Tramites.Application.UseCases.ProcedureTypes;

public sealed record CreateProcedureTypeRequest(
    string Family,
    string Code,
    string Name,
    string? Description);

public sealed record ProcedureTypeSummary(
    Guid Id,
    string Code,
    string Name,
    string Family,
    string PublicationStatus,
    bool IsActive,
    DateTimeOffset? PublishedAt);

public sealed class CreateProcedureTypeHandler(IProcedureTypeRepository repository)
{
    public async Task<ProcedureTypeSummary> HandleAsync(
        CreateProcedureTypeRequest request,
        CancellationToken ct = default)
    {
        var entity = new ProcedureType
        {
            Id = Guid.NewGuid(),
            Code = request.Code,
            Name = request.Name,
            Family = request.Family,
            Description = request.Description,
            IsActive = true,
            PublicationStatus = Domain.Enums.PublicationStatus.Draft,
            CreatedAt = DateTimeOffset.UtcNow
        };

        await repository.AddAsync(entity, ct);
        await repository.SaveChangesAsync(ct);

        return ToSummary(entity);
    }

    internal static ProcedureTypeSummary ToSummary(ProcedureType e) =>
        new(e.Id, e.Code, e.Name, e.Family, e.PublicationStatus, e.IsActive, e.PublishedAt);
}
