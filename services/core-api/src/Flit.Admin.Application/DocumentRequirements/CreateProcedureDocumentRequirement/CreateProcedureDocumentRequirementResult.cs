namespace Flit.Admin.Application.DocumentRequirements.CreateProcedureDocumentRequirement;

/// <summary>Desenlace del alta de una asociación (AC1/AC5).</summary>
public enum CreateProcedureDocumentRequirementOutcome
{
    /// <summary>Creada correctamente → HTTP 201.</summary>
    Created,

    /// <summary>Validación, documento inactivo o par duplicado → HTTP 422.</summary>
    ValidationFailed,

    /// <summary>El tipo de trámite no existe → HTTP 404.</summary>
    ProcedureTypeNotFound,

    /// <summary>El tipo de documento no existe → HTTP 404.</summary>
    DocumentTypeNotFound,
}

/// <summary>
/// Resultado del alta: el <see cref="Outcome"/> determina el status HTTP;
/// <see cref="Response"/> trae la asociación creada (201) y <see cref="Error"/> el
/// mensaje en caso de 422.
/// </summary>
public sealed class CreateProcedureDocumentRequirementResult
{
    /// <summary>Mensaje 422 exacto del AC5 (documento inactivo).</summary>
    public const string DocumentInactiveMessage =
        "El tipo de documento está inactivo y no puede asociarse a un trámite";

    private CreateProcedureDocumentRequirementResult(
        CreateProcedureDocumentRequirementOutcome outcome,
        ProcedureDocumentRequirementResponse? response,
        string? error)
    {
        Outcome = outcome;
        Response = response;
        Error = error;
    }

    public CreateProcedureDocumentRequirementOutcome Outcome { get; }

    public ProcedureDocumentRequirementResponse? Response { get; }

    public string? Error { get; }

    public static CreateProcedureDocumentRequirementResult Created(
        ProcedureDocumentRequirementResponse response) =>
        new(CreateProcedureDocumentRequirementOutcome.Created, response, null);

    public static CreateProcedureDocumentRequirementResult ValidationFailed(string error) =>
        new(CreateProcedureDocumentRequirementOutcome.ValidationFailed, null, error);

    public static CreateProcedureDocumentRequirementResult ProcedureTypeNotFound { get; } =
        new(CreateProcedureDocumentRequirementOutcome.ProcedureTypeNotFound, null, null);

    public static CreateProcedureDocumentRequirementResult DocumentTypeNotFound { get; } =
        new(CreateProcedureDocumentRequirementOutcome.DocumentTypeNotFound, null, null);
}
