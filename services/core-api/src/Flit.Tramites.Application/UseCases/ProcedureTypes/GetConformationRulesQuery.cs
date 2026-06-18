using Flit.Tramites.Domain.Repositories;

namespace Flit.Tramites.Application.UseCases.ProcedureTypes;

public sealed record ConformationRuleDto(
    Guid Id,
    string ProcedureEntityCode,
    string ProcedureEntityName,
    bool IsActive,
    short SortOrder,
    string ValidationProfile);

public sealed class GetConformationRulesHandler(IProcedureTypeRepository procedureRepo)
{
    public async Task<(List<ConformationRuleDto>? Result, string? Error)> HandleAsync(
        Guid procedureTypeId,
        CancellationToken ct = default)
    {
        var type = await procedureRepo.GetByIdAsync(procedureTypeId, ct);
        if (type is null)
            return (null, "not_found");

        var rules = await procedureRepo.GetConformationRulesAsync(procedureTypeId, ct);
        var dtos = rules.Select(r => new ConformationRuleDto(
            r.Id,
            r.ProcedureEntity?.Code ?? string.Empty,
            r.ProcedureEntity?.Name ?? string.Empty,
            r.IsActive,
            r.SortOrder,
            r.ValidationProfile)).ToList();

        return (dtos, null);
    }
}
