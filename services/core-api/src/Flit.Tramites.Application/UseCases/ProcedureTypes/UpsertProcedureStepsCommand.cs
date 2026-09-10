using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Enums;
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
    List<FormFieldInput> FormFields,
    /// <summary>
    /// Renderer de la sección (CFD-09). <c>null</c> = conservar el valor ya almacenado; solo si la
    /// sección es nueva se cae a <c>generic_form</c>. Sin esta preservación, un cliente que no envíe
    /// el campo — todos los actuales — degradaría a genéricas las secciones tipadas del seed.
    /// </summary>
    string? SectionType = null);

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
                    f.ConsultationTemplateId, f.Options, f.ValidationSchema)).ToList(),
                sec.SectionType
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

        // ReplaceStepsAsync borra y recrea: sin este mapa, cada PUT reescribiría section_type al
        // default 'generic_form' y dejaría los tipos parametrizados (PRENDA_INSCRIPCION,
        // CAMBIO_LOCATARIO) sin renderer ni gate. Se indexa por paso+sección porque el código de
        // sección solo es único dentro de su paso.
        var existingSectionTypes = existingSteps
            .SelectMany(st => st.Sections.Select(sec => (st.Code, sec)))
            .ToDictionary(
                x => (x.Code, x.sec.Code),
                x => x.sec.SectionType,
                TupleComparer);

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
                    SectionType = ResolveSectionType(si.Code, sec, existingSectionTypes),
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

    private static readonly IEqualityComparer<(string StepCode, string SectionCode)> TupleComparer =
        new StepSectionComparer();

    /// <summary>
    /// Precedencia del renderer: lo que envía el cliente (si es válido) &gt; lo ya almacenado &gt;
    /// <c>generic_form</c>. Un valor fuera del catálogo se ignora en lugar de propagarse: el CHECK del
    /// DDL lo rechazaría igualmente, y descartarlo preserva la sección en vez de romper el guardado.
    /// </summary>
    private static string ResolveSectionType(
        string stepCode,
        ProcedureSectionInput input,
        Dictionary<(string StepCode, string SectionCode), string> existing)
    {
        if (ProcedureSectionTypes.IsValid(input.SectionType))
            return input.SectionType!;

        return existing.TryGetValue((stepCode, input.Code), out var stored)
            ? stored
            : ProcedureSectionTypes.GenericForm;
    }

    private sealed class StepSectionComparer : IEqualityComparer<(string StepCode, string SectionCode)>
    {
        public bool Equals((string StepCode, string SectionCode) a, (string StepCode, string SectionCode) b) =>
            string.Equals(a.StepCode, b.StepCode, StringComparison.OrdinalIgnoreCase)
            && string.Equals(a.SectionCode, b.SectionCode, StringComparison.OrdinalIgnoreCase);

        public int GetHashCode((string StepCode, string SectionCode) x) =>
            HashCode.Combine(
                x.StepCode.ToUpperInvariant(),
                x.SectionCode.ToUpperInvariant());
    }
}
