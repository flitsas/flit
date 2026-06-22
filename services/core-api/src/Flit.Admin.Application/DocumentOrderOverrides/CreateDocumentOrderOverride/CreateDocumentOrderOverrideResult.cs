namespace Flit.Admin.Application.DocumentOrderOverrides.CreateDocumentOrderOverride;

/// <summary>Desenlace del alta de un override (AC1/AC2).</summary>
public enum CreateDocumentOrderOverrideOutcome
{
    /// <summary>Creado correctamente → HTTP 201.</summary>
    Created,

    /// <summary>Validación, documento no asociado, referencia faltante o duplicado → HTTP 422.</summary>
    ValidationFailed,

    /// <summary>El tipo de trámite no existe → HTTP 404.</summary>
    ProcedureTypeNotFound,

    /// <summary>El tipo de documento no existe → HTTP 404.</summary>
    DocumentTypeNotFound,

    /// <summary>La referencia del scope (OT o cliente) no existe → HTTP 404.</summary>
    ScopeRefNotFound,
}

/// <summary>
/// Resultado del alta: el <see cref="Outcome"/> determina el status HTTP;
/// <see cref="Response"/> trae el override creado (201) y <see cref="Error"/> el mensaje
/// en caso de 422.
/// </summary>
public sealed class CreateDocumentOrderOverrideResult
{
    private CreateDocumentOrderOverrideResult(
        CreateDocumentOrderOverrideOutcome outcome,
        DocumentOrderOverrideResponse? response,
        string? error)
    {
        Outcome = outcome;
        Response = response;
        Error = error;
    }

    public CreateDocumentOrderOverrideOutcome Outcome { get; }

    public DocumentOrderOverrideResponse? Response { get; }

    public string? Error { get; }

    public static CreateDocumentOrderOverrideResult Created(DocumentOrderOverrideResponse response) =>
        new(CreateDocumentOrderOverrideOutcome.Created, response, null);

    public static CreateDocumentOrderOverrideResult ValidationFailed(string error) =>
        new(CreateDocumentOrderOverrideOutcome.ValidationFailed, null, error);

    public static CreateDocumentOrderOverrideResult ProcedureTypeNotFound { get; } =
        new(CreateDocumentOrderOverrideOutcome.ProcedureTypeNotFound, null, null);

    public static CreateDocumentOrderOverrideResult DocumentTypeNotFound { get; } =
        new(CreateDocumentOrderOverrideOutcome.DocumentTypeNotFound, null, null);

    public static CreateDocumentOrderOverrideResult ScopeRefNotFound { get; } =
        new(CreateDocumentOrderOverrideOutcome.ScopeRefNotFound, null, null);
}
