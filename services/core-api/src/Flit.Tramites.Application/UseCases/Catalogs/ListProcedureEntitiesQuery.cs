using Flit.Tramites.Domain.Repositories;

namespace Flit.Tramites.Application.UseCases.Catalogs;

public sealed record ProcedureEntityDto(Guid Id, string Code, string Name, short SortOrder, bool IsActive);

public sealed class ListProcedureEntitiesHandler(ICatalogRepository repository)
{
    public async Task<List<ProcedureEntityDto>> HandleAsync(CancellationToken ct = default)
    {
        var items = await repository.ListProcedureEntitiesAsync(ct);
        return items.Select(e => new ProcedureEntityDto(e.Id, e.Code, e.Name, e.SortOrder, e.IsActive)).ToList();
    }
}
