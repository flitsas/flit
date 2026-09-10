using Flit.Tramites.Domain.Repositories;

namespace Flit.Tramites.Application.UseCases.ProcedureTypes;

public sealed record FormFieldDto(
    Guid Id,
    string FieldKey,
    string Label,
    string FieldType,
    bool IsRequired,
    short SortOrder,
    bool IsLocked,
    string? LockReason,
    Guid? ConsultationTemplateId,
    string? Options,
    string ValidationSchema);

public sealed record ProcedureSectionDto(
    Guid Id,
    string Code,
    string Title,
    short SortOrder,
    string Layout,
    List<FormFieldDto> FormFields,
    /// <summary>Renderer de la sección (CFD-09). Sin este campo el configurador no podía leer ni
    /// devolver el <c>section_type</c>, y cada guardado lo degradaba a <c>generic_form</c>.</summary>
    string SectionType = Flit.Tramites.Domain.Enums.ProcedureSectionTypes.GenericForm);

public sealed record ProcedureStepDto(
    Guid Id,
    string Code,
    string Title,
    short SortOrder,
    bool IsActive,
    List<ProcedureSectionDto> Sections);

public sealed class GetProcedureStepsHandler(IProcedureTypeRepository repository)
{
    public async Task<(List<ProcedureStepDto>? Result, string? Error)> HandleAsync(
        Guid procedureTypeId,
        CancellationToken ct = default)
    {
        var type = await repository.GetByIdAsync(procedureTypeId, ct);
        if (type is null)
            return (null, "not_found");

        var steps = await repository.GetStepsWithDetailsAsync(procedureTypeId, ct);
        var dtos = steps.Select(s => new ProcedureStepDto(
            s.Id,
            s.Code,
            s.Title,
            s.SortOrder,
            s.IsActive,
            s.Sections.Select(sec => new ProcedureSectionDto(
                sec.Id,
                sec.Code,
                sec.Title,
                sec.SortOrder,
                sec.Layout,
                sec.FormFields.Select(f => new FormFieldDto(
                    f.Id,
                    f.FieldKey,
                    f.Label,
                    f.FieldType,
                    f.IsRequired,
                    f.SortOrder,
                    f.IsLocked,
                    f.LockReason,
                    f.ConsultationTemplateId,
                    f.Options,
                    f.ValidationSchema)).ToList(),
                sec.SectionType
            )).ToList()
        )).ToList();

        return (dtos, null);
    }
}
