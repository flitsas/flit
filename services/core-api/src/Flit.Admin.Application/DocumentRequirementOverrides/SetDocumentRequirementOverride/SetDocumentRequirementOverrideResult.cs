namespace Flit.Admin.Application.DocumentRequirementOverrides.SetDocumentRequirementOverride;

/// <summary>Desenlace del upsert/limpiar de la obligatoriedad por OT (HU #10198).</summary>
public enum SetDocumentRequirementOverrideOutcome
{
    /// <summary>Override creado o actualizado → HTTP 200.</summary>
    Set,

    /// <summary>Override eliminado (estado=DEFAULT) → HTTP 204.</summary>
    Cleared,

    /// <summary>Estado inválido o documento no asociado al trámite → HTTP 422.</summary>
    ValidationFailed,

    /// <summary>El tipo de trámite no existe → HTTP 404.</summary>
    ProcedureTypeNotFound,

    /// <summary>El tipo de documento no existe → HTTP 404.</summary>
    DocumentTypeNotFound,

    /// <summary>El organismo de tránsito no existe → HTTP 404.</summary>
    TransitOfficeNotFound,
}

/// <summary>
/// Resultado del upsert/limpiar: el <see cref="Outcome"/> determina el status HTTP;
/// <see cref="Response"/> trae el override (200) y <see cref="Error"/> el mensaje en 422.
/// </summary>
public sealed class SetDocumentRequirementOverrideResult
{
    private SetDocumentRequirementOverrideResult(
        SetDocumentRequirementOverrideOutcome outcome,
        DocumentRequirementOverrideResponse? response,
        string? error)
    {
        Outcome = outcome;
        Response = response;
        Error = error;
    }

    public SetDocumentRequirementOverrideOutcome Outcome { get; }

    public DocumentRequirementOverrideResponse? Response { get; }

    public string? Error { get; }

    public static SetDocumentRequirementOverrideResult Set(DocumentRequirementOverrideResponse response) =>
        new(SetDocumentRequirementOverrideOutcome.Set, response, null);

    public static SetDocumentRequirementOverrideResult Cleared { get; } =
        new(SetDocumentRequirementOverrideOutcome.Cleared, null, null);

    public static SetDocumentRequirementOverrideResult ValidationFailed(string error) =>
        new(SetDocumentRequirementOverrideOutcome.ValidationFailed, null, error);

    public static SetDocumentRequirementOverrideResult ProcedureTypeNotFound { get; } =
        new(SetDocumentRequirementOverrideOutcome.ProcedureTypeNotFound, null, null);

    public static SetDocumentRequirementOverrideResult DocumentTypeNotFound { get; } =
        new(SetDocumentRequirementOverrideOutcome.DocumentTypeNotFound, null, null);

    public static SetDocumentRequirementOverrideResult TransitOfficeNotFound { get; } =
        new(SetDocumentRequirementOverrideOutcome.TransitOfficeNotFound, null, null);
}
