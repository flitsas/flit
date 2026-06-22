namespace Flit.Admin.Application.DocumentTypes.UpdateDocumentType;

/// <summary>Desenlace de la actualización de un tipo de documento (AC3).</summary>
public enum UpdateDocumentTypeOutcome
{
    /// <summary>Actualizado correctamente → HTTP 200.</summary>
    Updated,

    /// <summary>Fallo de validación o código duplicado → HTTP 422.</summary>
    ValidationFailed,

    /// <summary>El id no existe → HTTP 404.</summary>
    NotFound,
}

/// <summary>
/// Resultado de la actualización: el <see cref="Outcome"/> determina el status HTTP;
/// <see cref="Document"/> trae el documento actualizado en caso de éxito y
/// <see cref="Error"/> el mensaje en caso de 422.
/// </summary>
public sealed class UpdateDocumentTypeResult
{
    private UpdateDocumentTypeResult(
        UpdateDocumentTypeOutcome outcome,
        DocumentTypeResponse? document,
        string? error)
    {
        Outcome = outcome;
        Document = document;
        Error = error;
    }

    public UpdateDocumentTypeOutcome Outcome { get; }

    public DocumentTypeResponse? Document { get; }

    public string? Error { get; }

    public static UpdateDocumentTypeResult Updated(DocumentTypeResponse document) =>
        new(UpdateDocumentTypeOutcome.Updated, document, null);

    public static UpdateDocumentTypeResult ValidationFailed(string error) =>
        new(UpdateDocumentTypeOutcome.ValidationFailed, null, error);

    public static UpdateDocumentTypeResult NotFound() =>
        new(UpdateDocumentTypeOutcome.NotFound, null, null);
}
