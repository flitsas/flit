using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Repositories;

namespace Flit.Tramites.Application.UseCases.ProcedureTypes;

public sealed class GetProcedureTypeHandler(IProcedureTypeRepository repository)
{
    public async Task<ProcedureType?> HandleAsync(Guid id, CancellationToken ct = default) =>
        await repository.GetByIdWithDetailsAsync(id, ct);
}
