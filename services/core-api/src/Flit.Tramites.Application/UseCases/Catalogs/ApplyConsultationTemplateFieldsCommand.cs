using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Repositories;

namespace Flit.Tramites.Application.UseCases.Catalogs;

public sealed record ApplyTemplateFieldsRequest(Guid ProcedureTypeId, Guid SectionId);

public sealed class ApplyConsultationTemplateFieldsHandler(
    IProcedureTypeRepository procedureRepo,
    ICatalogRepository catalogRepo)
{
    public async Task<string?> HandleAsync(
        Guid templateId,
        ApplyTemplateFieldsRequest request,
        CancellationToken ct = default)
    {
        var template = await catalogRepo.GetConsultationTemplateByIdAsync(templateId, ct);
        if (template is null)
            return "template_not_found";

        var steps = await procedureRepo.GetStepsWithDetailsAsync(request.ProcedureTypeId, ct);
        var targetSection = steps
            .SelectMany(s => s.Sections)
            .FirstOrDefault(sec => sec.Id == request.SectionId);

        if (targetSection is null)
            return "section_not_found";

        var requiredKeys = ParseKeys(template.RequiredFieldKeys);
        short sortOrder = (short)(targetSection.FormFields.Count > 0
            ? targetSection.FormFields.Max(f => f.SortOrder) + 1
            : 0);

        var newFields = new List<FormField>();

        foreach (var key in requiredKeys)
        {
            var existing = targetSection.FormFields.FirstOrDefault(f =>
                string.Equals(f.FieldKey, key, StringComparison.OrdinalIgnoreCase));

            if (existing is not null)
            {
                existing.IsLocked = true;
                existing.LockReason ??= $"Requerido por plantilla {template.Code}";
                existing.ConsultationTemplateId ??= template.Id;
                continue;
            }

            newFields.Add(new FormField
            {
                Id = Guid.NewGuid(),
                ProcedureSectionId = targetSection.Id,
                FieldKey = key,
                Label = key,
                FieldType = "text",
                IsRequired = true,
                SortOrder = sortOrder++,
                IsLocked = true,
                LockReason = $"Requerido por plantilla {template.Code}",
                ConsultationTemplateId = template.Id,
                ValidationSchema = "{}",
                CreatedAt = DateTimeOffset.UtcNow
            });
        }

        if (newFields.Count > 0)
            await procedureRepo.AddFormFieldsAsync(newFields, ct);

        await procedureRepo.SaveChangesAsync(ct);
        return null;
    }

    private static List<string> ParseKeys(string json)
    {
        var keys = new List<string>();
        var trimmed = json.Trim().TrimStart('[').TrimEnd(']');
        if (string.IsNullOrWhiteSpace(trimmed))
            return keys;

        foreach (var item in trimmed.Split(','))
        {
            var key = item.Trim().Trim('"');
            if (!string.IsNullOrEmpty(key))
                keys.Add(key);
        }
        return keys;
    }
}
