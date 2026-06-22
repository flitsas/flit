using Flit.Tramites.Domain.Repositories;

namespace Flit.Tramites.Application.UseCases.ProcedureTypes;

public sealed record ProcedureTypeConfigurationDto(
    Guid Id,
    string Code,
    string Name,
    string Family,
    DateTimeOffset? PublishedAt,
    List<ConformationRuleDto> ConformationRules,
    List<ProcedureStepDto> Steps);

public sealed class GetProcedureTypeConfigurationHandler(IProcedureTypeRepository repository)
{
    public async Task<(ProcedureTypeConfigurationDto? Result, string? Error)> HandleAsync(
        string code,
        CancellationToken ct = default)
    {
        var entity = await repository.GetByCodePublishedAsync(code, ct);
        if (entity is null)
            return (null, "not_found");

        var conformationDtos = entity.ConformationRules
            .Select(r => new ConformationRuleDto(
                r.Id,
                r.ProcedureEntity?.Code ?? string.Empty,
                r.ProcedureEntity?.Name ?? string.Empty,
                r.IsActive,
                r.SortOrder,
                r.ValidationProfile))
            .ToList();

        var stepDtos = entity.Steps
            .OrderBy(s => s.SortOrder)
            .Select(s => new ProcedureStepDto(
                s.Id, s.Code, s.Title, s.SortOrder, s.IsActive,
                s.Sections.OrderBy(sec => sec.SortOrder).Select(sec => new ProcedureSectionDto(
                    sec.Id, sec.Code, sec.Title, sec.SortOrder, sec.Layout,
                    sec.FormFields.OrderBy(f => f.SortOrder).Select(f => new FormFieldDto(
                        f.Id, f.FieldKey, f.Label, f.FieldType,
                        f.IsRequired, f.SortOrder, f.IsLocked, f.LockReason,
                        f.ConsultationTemplateId, f.Options, f.ValidationSchema)).ToList()
                )).ToList()))
            .ToList();

        return (new ProcedureTypeConfigurationDto(
            entity.Id,
            entity.Code,
            entity.Name,
            entity.Family,
            entity.PublishedAt,
            conformationDtos,
            stepDtos), null);
    }
}
