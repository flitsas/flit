namespace Flit.Admin.Application.DocumentRequirements.UpdateProcedureDocumentRequirement;

/// <summary>Desenlace de la actualización de una asociación (AC3).</summary>
public enum UpdateProcedureDocumentRequirementOutcome
{
    /// <summary>Actualizada correctamente → HTTP 200.</summary>
    Updated,

    /// <summary>Fallo de validación → HTTP 422.</summary>
    ValidationFailed,

    /// <summary>El id no existe → HTTP 404.</summary>
    NotFound,
}

/// <summary>
/// Resultado de la actualización: el <see cref="Outcome"/> determina el status HTTP;
/// <see cref="Response"/> trae la asociación actualizada (200) y <see cref="Error"/> el
/// mensaje en caso de 422.
/// </summary>
public sealed class UpdateProcedureDocumentRequirementResult
{
    private UpdateProcedureDocumentRequirementResult(
        UpdateProcedureDocumentRequirementOutcome outcome,
        ProcedureDocumentRequirementResponse? response,
        string? error)
    {
        Outcome = outcome;
        Response = response;
        Error = error;
    }

    public UpdateProcedureDocumentRequirementOutcome Outcome { get; }

    public ProcedureDocumentRequirementResponse? Response { get; }

    public string? Error { get; }

    public static UpdateProcedureDocumentRequirementResult Updated(
        ProcedureDocumentRequirementResponse response) =>
        new(UpdateProcedureDocumentRequirementOutcome.Updated, response, null);

    public static UpdateProcedureDocumentRequirementResult ValidationFailed(string error) =>
        new(UpdateProcedureDocumentRequirementOutcome.ValidationFailed, null, error);

    public static UpdateProcedureDocumentRequirementResult NotFound { get; } =
        new(UpdateProcedureDocumentRequirementOutcome.NotFound, null, null);
}
