namespace Flit.Admin.Application.DocumentTypes.CreateDocumentType;

/// <summary>
/// Resultado del alta. <see cref="IsValid"/> falso indica fallo de validación o
/// código duplicado (HTTP 422 <c>{ error }</c>) sin persistir; verdadero indica
/// alta exitosa (HTTP 201) con el documento creado.
/// </summary>
public sealed class CreateDocumentTypeResult
{
    private CreateDocumentTypeResult(bool isValid, DocumentTypeResponse? document, string? error)
    {
        IsValid = isValid;
        Document = document;
        Error = error;
    }

    public bool IsValid { get; }

    public DocumentTypeResponse? Document { get; }

    public string? Error { get; }

    public static CreateDocumentTypeResult Success(DocumentTypeResponse document) =>
        new(true, document, null);

    public static CreateDocumentTypeResult Invalid(string error) =>
        new(false, null, error);
}
