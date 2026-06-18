using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Enums;
using Flit.Tramites.Domain.ValueObjects;

namespace Flit.Tramites.Domain.Services;

public sealed class ProcedureTypeValidator : IProcedureTypeValidator
{
    private const string EntityVehicle = "VEHICLE";
    private const string FieldKeyPlateOrVin = "plate_or_vin";
    private const string FieldKeyDocumentType = "document_type";
    private const string PersonTypeJuridical = "juridical";
    private const string EntityScopeActor = "actor";

    public ValidationResult Validate(ProcedureType procedureType)
    {
        var result = new ValidationResult();

        var allFields = GetAllFields(procedureType);
        var activeRules = procedureType.ConformationRules
            .Where(r => r.IsActive)
            .ToList();

        ValidateVinPlateRule(procedureType, activeRules, allFields, result);
        ValidateNitPersonTypeRule(allFields, result);
        ValidateConsultationTemplateFieldCoverage(allFields, result);

        return result;
    }

    private static List<FormField> GetAllFields(ProcedureType procedureType) =>
        procedureType.Steps
            .SelectMany(s => s.Sections)
            .SelectMany(sec => sec.FormFields)
            .ToList();

    private static void ValidateVinPlateRule(
        ProcedureType procedureType,
        List<ConformationRule> activeRules,
        List<FormField> allFields,
        ValidationResult result)
    {
        if (procedureType.Family != ProcedureFamily.Matriculas)
            return;

        bool hasVehicleActive = activeRules.Any(r =>
            r.ProcedureEntity?.Code == EntityVehicle);

        if (!hasVehicleActive)
            return;

        bool hasLockedPlateOrVin = allFields.Any(f =>
            f.FieldKey == FieldKeyPlateOrVin && f.IsLocked);

        if (!hasLockedPlateOrVin)
            result.AddError(
                "VIN_PLATE_RULE",
                "El tipo MATRICULAS con arista VEHICLE activa requiere un campo locked 'plate_or_vin'.",
                "steps.fields.plate_or_vin");
    }

    private static void ValidateNitPersonTypeRule(
        List<FormField> allFields,
        ValidationResult result)
    {
        bool hasNitField = allFields.Any(f =>
            f.FieldKey == FieldKeyDocumentType &&
            f.Options != null &&
            f.Options.Contains("NIT", StringComparison.OrdinalIgnoreCase));

        if (!hasNitField)
            return;

        bool hasRuesJuridicalTemplate = allFields.Any(f =>
            f.ConsultationTemplate?.EntityScope == EntityScopeActor &&
            f.ConsultationTemplate?.PersonType == PersonTypeJuridical &&
            f.IsLocked);

        if (!hasRuesJuridicalTemplate)
            result.AddError(
                "NIT_PERSON_TYPE",
                "El campo 'document_type' acepta NIT pero no hay plantilla RUES persona jurídica aplicada.",
                "steps.fields.document_type");
    }

    private static void ValidateConsultationTemplateFieldCoverage(
        List<FormField> allFields,
        ValidationResult result)
    {
        var templatesInUse = allFields
            .Where(f => f.ConsultationTemplate != null && f.ConsultationTemplate.IsActive)
            .Select(f => f.ConsultationTemplate!)
            .DistinctBy(t => t.Id)
            .ToList();

        foreach (var template in templatesInUse)
        {
            var requiredKeys = ParseRequiredFieldKeys(template.RequiredFieldKeys);
            foreach (var key in requiredKeys)
            {
                bool covered = allFields.Any(f => f.FieldKey == key && f.IsLocked);
                if (!covered)
                    result.AddError(
                        "INCOMPLETE_CONSULTATION_FIELDS",
                        $"La plantilla '{template.Code}' requiere el campo '{key}' como locked.",
                        $"steps.fields.{key}");
            }
        }
    }

    private static List<string> ParseRequiredFieldKeys(string json)
    {
        var keys = new List<string>();
        var trimmed = json.Trim();
        if (trimmed is "[]" or "null" or "")
            return keys;

        trimmed = trimmed.TrimStart('[').TrimEnd(']');
        foreach (var item in trimmed.Split(','))
        {
            var key = item.Trim().Trim('"');
            if (!string.IsNullOrEmpty(key))
                keys.Add(key);
        }
        return keys;
    }
}
