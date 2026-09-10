namespace Flit.Tramites.Domain.Enums;

/// <summary>
/// Catálogo CERRADO de <c>tramites.procedure_sections.section_type</c> (CFD-09). Cada valor mapea a
/// un renderer del <c>SectionRendererRegistry</c> del cliente y a una rama de
/// <c>DynamicGateEvaluator.EvaluateSection</c>.
/// <para>Espeja el <c>CHECK ck_procedure_sections_section_type</c> del DDL. Cambiar este catálogo
/// exige PR coordinado backend + frontend + migración: son tres definiciones del mismo contrato.</para>
/// </summary>
public static class ProcedureSectionTypes
{
    public const string VehicleQuery = "vehicle_query";
    public const string DocumentChecklist = "document_checklist";
    public const string ActorForm = "actor_form";
    public const string Commercial = "commercial";
    public const string Biometric = "biometric";
    public const string SignatureFur = "signature_fur";
    public const string PlateRequest = "plate_request";
    public const string PrendaDecision = "prenda_decision";

    /// <summary>Default del DDL: no bloquea ningún gate; los form_fields se validan aparte.</summary>
    public const string GenericForm = "generic_form";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
    {
        VehicleQuery, DocumentChecklist, ActorForm, Commercial,
        Biometric, SignatureFur, PlateRequest, PrendaDecision, GenericForm,
    };

    /// <summary>
    /// <c>true</c> si el valor pertenece al catálogo. Comparación ORDINAL y sensible a mayúsculas: el
    /// CHECK del DDL también lo es, así que aceptar variantes aquí produciría un fallo de inserción
    /// más abajo y más difícil de leer.
    /// </summary>
    public static bool IsValid(string? value) =>
        value is not null && All.Contains(value);
}
