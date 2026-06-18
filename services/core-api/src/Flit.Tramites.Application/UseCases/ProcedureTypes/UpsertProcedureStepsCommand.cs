using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Repositories;

namespace Flit.Tramites.Application.UseCases.ProcedureTypes;

public sealed record FormFieldInput(
    string FieldKey,
    string Label,
    string FieldType,
    bool IsRequired,
    short SortOrder,
    string? Options,
    string? ValidationSchema,
    string? DefaultValue);

public sealed record ProcedureSectionInput(
    string Code,
    string Title,
    short SortOrder,
    string? Layout,
    List<FormFieldInput> FormFields);

public sealed record ProcedureStepInput(
    string Code,
    string Title,
    short SortOrder,
    bool IsActive,
    List<ProcedureSectionInput> Sections);

public sealed class UpsertProcedureStepsHandler(IProcedureTypeRepository repository)
{
    public async Task<(List<ProcedureStepDto>? Result, string? Error, List<string>? LockedFieldsViolated)> HandleAsync(
        Guid procedureTypeId,
        List<ProcedureStepInput> inputs,
        CancellationToken ct = default)
    {
        var type = await repository.GetByIdAsync(procedureTypeId, ct);
        if (type is null)
            return (null, "not_found", null);

        var existingSteps = await repository.GetStepsWithDetailsAsync(procedureTypeId, ct);
        var lockedFields = existingSteps
            .SelectMany(s => s.Sections)
            .SelectMany(sec => sec.FormFields)
            .Where(f => f.IsLocked)
            .Select(f => f.FieldKey)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var incomingKeys = inputs
            .SelectMany(s => s.Sections)
            .SelectMany(sec => sec.FormFields)
            .Select(f => f.FieldKey)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var violated = lockedFields.Where(k => !incomingKeys.Contains(k)).ToList();
        if (violated.Count > 0)
            return (null, "locked_field_violation", violated);

        var newSteps = BuildSteps(procedureTypeId, inputs, existingSteps);

        await repository.ReplaceStepsAsync(procedureTypeId, newSteps, ct);
        await repository.SaveChangesAsync(ct);

        var dtos = newSteps.Select(s => new ProcedureStepDto(
            s.Id, s.Code, s.Title, s.SortOrder, s.IsActive,
            s.Sections.Select(sec => new ProcedureSectionDto(
                sec.Id, sec.Code, sec.Title, sec.SortOrder, sec.Layout,
                sec.FormFields.Select(f => new FormFieldDto(
                    f.Id, f.FieldKey, f.Label, f.FieldType,
                    f.IsRequired, f.SortOrder, f.IsLocked, f.LockReason,
                    f.ConsultationTemplateId, f.Options, f.ValidationSchema)).ToList()
            )).ToList()
        )).ToList();

        return (dtos, null, null);
    }

    private static List<ProcedureStep> BuildSteps(
        Guid procedureTypeId,
        List<ProcedureStepInput> inputs,
        List<ProcedureStep> existingSteps)
    {
        var existingFieldsByKey = existingSteps
            .SelectMany(s => s.Sections)
            .SelectMany(sec => sec.FormFields)
            .ToDictionary(f => f.FieldKey, f => f, StringComparer.OrdinalIgnoreCase);

        return inputs.Select(si =>
        {
            var step = new ProcedureStep
            {
                Id = Guid.NewGuid(),
                ProcedureTypeId = procedureTypeId,
                Code = si.Code,
                Title = si.Title,
                SortOrder = si.SortOrder,
                IsActive = si.IsActive,
                CreatedAt = DateTimeOffset.UtcNow
            };

            step.Sections = si.Sections.Select(sec =>
            {
                var section = new ProcedureSection
                {
                    Id = Guid.NewGuid(),
                    ProcedureStepId = step.Id,
                    Code = sec.Code,
                    Title = sec.Title,
                    SortOrder = sec.SortOrder,
                    Layout = sec.Layout ?? "single",
                    CreatedAt = DateTimeOffset.UtcNow
                };

                section.FormFields = sec.FormFields.Select(fi =>
                {
                    if (existingFieldsByKey.TryGetValue(fi.FieldKey, out var existing) && existing.IsLocked)
                    {
                        existing.ProcedureSectionId = section.Id;
                        return existing;
                    }

                    return new FormField
                    {
                        Id = Guid.NewGuid(),
                        ProcedureSectionId = section.Id,
                        FieldKey = fi.FieldKey,
                        Label = fi.Label,
                        FieldType = fi.FieldType,
                        IsRequired = fi.IsRequired,
                        SortOrder = fi.SortOrder,
                        Options = fi.Options,
                        ValidationSchema = fi.ValidationSchema ?? "{}",
                        DefaultValue = fi.DefaultValue,
                        CreatedAt = DateTimeOffset.UtcNow
                    };
                }).ToList();

                return section;
            }).ToList();

            return step;
        }).ToList();
    }
}
