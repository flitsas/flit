using Flit.Tramites.Domain.Repositories;

namespace Flit.Tramites.Application.UseCases.ProcedureTypes;

public sealed class ListProcedureTypesHandler(IProcedureTypeRepository repository)
{
    public async Task<List<ProcedureTypeSummary>> HandleAsync(
        string? family,
        string? publicationStatus,
        CancellationToken ct = default)
    {
        var items = await repository.ListAsync(family, publicationStatus, ct);
        return items.Select(CreateProcedureTypeHandler.ToSummary).ToList();
    }
}
