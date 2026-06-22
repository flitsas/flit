using Flit.Tramites.Domain.Repositories;

namespace Flit.Tramites.Application.UseCases.Catalogs;

public sealed record ExternalDataSourceDto(
    Guid Id,
    string Code,
    string Name,
    string AuthType,
    bool IsActive);

public sealed class ListExternalDataSourcesHandler(ICatalogRepository repository)
{
    public async Task<List<ExternalDataSourceDto>> HandleAsync(CancellationToken ct = default)
    {
        var items = await repository.ListExternalDataSourcesAsync(ct);
        return items.Select(e => new ExternalDataSourceDto(e.Id, e.Code, e.Name, e.AuthType, e.IsActive)).ToList();
    }
}
