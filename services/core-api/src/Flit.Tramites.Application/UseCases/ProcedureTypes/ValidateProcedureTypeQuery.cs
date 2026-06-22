using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.Services;
using Flit.Tramites.Domain.ValueObjects;

namespace Flit.Tramites.Application.UseCases.ProcedureTypes;

public sealed class ValidateProcedureTypeHandler(
    IProcedureTypeRepository repository,
    IProcedureTypeValidator validator)
{
    public async Task<(ValidationResult? Result, string? Error)> HandleAsync(
        Guid id,
        CancellationToken ct = default)
    {
        var entity = await repository.GetByIdWithDetailsAsync(id, ct);
        if (entity is null)
            return (null, "not_found");

        return (validator.Validate(entity), null);
    }
}
