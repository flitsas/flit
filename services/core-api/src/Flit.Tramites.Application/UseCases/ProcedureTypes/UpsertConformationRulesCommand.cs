using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Repositories;

namespace Flit.Tramites.Application.UseCases.ProcedureTypes;

public sealed record ConformationRuleInput(
    string ProcedureEntityCode,
    bool IsActive,
    short SortOrder,
    string? ValidationProfile);

public sealed class UpsertConformationRulesHandler(
    IProcedureTypeRepository procedureRepo,
    ICatalogRepository catalogRepo)
{
    public async Task<(List<ConformationRuleDto>? Result, string? Error)> HandleAsync(
        Guid procedureTypeId,
        List<ConformationRuleInput> inputs,
        CancellationToken ct = default)
    {
        var type = await procedureRepo.GetByIdAsync(procedureTypeId, ct);
        if (type is null)
            return (null, "not_found");

        var newRules = new List<ConformationRule>();
        foreach (var input in inputs)
        {
            var entity = await catalogRepo.GetProcedureEntityByCodeAsync(input.ProcedureEntityCode, ct);
            if (entity is null)
                return (null, $"entity_not_found:{input.ProcedureEntityCode}");

            newRules.Add(new ConformationRule
            {
                Id = Guid.NewGuid(),
                ProcedureTypeId = procedureTypeId,
                ProcedureEntityId = entity.Id,
                ProcedureEntity = entity,
                IsActive = input.IsActive,
                SortOrder = input.SortOrder,
                ValidationProfile = input.ValidationProfile ?? "{}",
                CreatedAt = DateTimeOffset.UtcNow
            });
        }

        await procedureRepo.ReplaceConformationRulesAsync(procedureTypeId, newRules, ct);
        await procedureRepo.SaveChangesAsync(ct);

        var dtos = newRules.Select(r => new ConformationRuleDto(
            r.Id,
            r.ProcedureEntity?.Code ?? string.Empty,
            r.ProcedureEntity?.Name ?? string.Empty,
            r.IsActive,
            r.SortOrder,
            r.ValidationProfile)).ToList();

        return (dtos, null);
    }
}
