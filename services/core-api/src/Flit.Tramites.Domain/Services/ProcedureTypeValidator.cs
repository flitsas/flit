using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Enums;
using Flit.Tramites.Domain.Tramites.Services;
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
        ValidateFamily(procedureType, result);
        ValidateGateProfile(procedureType, result);
        ValidateSectionTypes(procedureType, result);

        return result;
    }

    /// <summary>
    /// ADR-0050 — la familia gobierna clasificación, filtros, causales y gates por compañía. Fuera del
    /// dominio, <c>ProcedureFamilyCodes.FromCodeOrOtros</c> degradaría el tipo a OTROS en silencio, y
    /// el CHECK del DDL rechazaría el guardado más abajo con un error mucho menos legible.
    /// </summary>
    private static void ValidateFamily(ProcedureType procedureType, ValidationResult result)
    {
        if (!ProcedureFamilyCodes.IsValid(procedureType.Family))
            result.AddError(
                "FAMILY_INVALID",
                $"La familia '{procedureType.Family}' no pertenece al dominio "
                + $"({string.Join(" | ", ProcedureFamilyCodes.All)}).",
                "family");
    }

    /// <summary>
    /// ADR-0050 — el <c>gate_profile</c> gobierna el recorrido del wizard, así que un
    /// <c>entryMode</c> inválido deja el tipo sin forma de entrar. Se valida solo cuando viene
    /// informado: el perfil es configuración opcional y ausente equivale a "sin exigencias".
    /// </summary>
    private static void ValidateGateProfile(ProcedureType procedureType, ValidationResult result)
    {
        var profile = ProcedureTypeGateProfile.FromJson(procedureType.GateProfile);

        if (profile.EntryMode is not null && !ProcedureTypeGateProfile.IsValidEntryMode(profile.EntryMode))
            result.AddError(
                "GATE_PROFILE_ENTRY_MODE_INVALID",
                $"entryMode '{profile.EntryMode}' no es válido (PLATE | VIN | BOTH).",
                "gateProfile.entryMode");

        if (profile.RequiresBiometrics && profile.BiometricActors.Count == 0)
            result.AddError(
                "GATE_PROFILE_BIOMETRIC_ACTORS_MISSING",
                "requiresBiometrics exige al menos un actor en biometricActors; sin actores el gate "
                + "biométrico se satisface siempre y la validación de identidad nunca bloquea.",
                "gateProfile.biometricActors");
    }

    /// <summary>
    /// CFD-09 — cada sección debe declarar un <c>section_type</c> del catálogo cerrado: es lo que
    /// elige el renderer del frontend y la rama del <c>DynamicGateEvaluator</c>. Un valor fuera del
    /// catálogo cae en el <c>default</c> del evaluador, que nunca bloquea.
    /// </summary>
    private static void ValidateSectionTypes(ProcedureType procedureType, ValidationResult result)
    {
        foreach (var step in procedureType.Steps)
        {
            foreach (var section in step.Sections)
            {
                if (!ProcedureSectionTypes.IsValid(section.SectionType))
                    result.AddError(
                        "SECTION_TYPE_INVALID",
                        $"section_type '{section.SectionType}' no pertenece al catálogo CFD-09.",
                        $"steps.{step.Code}.sections.{section.Code}.sectionType");
            }
        }
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
        if (ProcedureFamilyCodes.FromCode(procedureType.Family) != ProcedureFamily.Matriculas)
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
