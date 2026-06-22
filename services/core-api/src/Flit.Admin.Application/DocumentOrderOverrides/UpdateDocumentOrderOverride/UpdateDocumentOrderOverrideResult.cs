namespace Flit.Admin.Application.DocumentOrderOverrides.UpdateDocumentOrderOverride;

/// <summary>Desenlace del reordenamiento de un override (HU #10198).</summary>
public enum UpdateDocumentOrderOverrideOutcome
{
    /// <summary>Orden actualizado → HTTP 200.</summary>
    Updated,

    /// <summary>Orden inválido (negativo o fuera de rango) → HTTP 422.</summary>
    ValidationFailed,

    /// <summary>El override no existe → HTTP 404.</summary>
    NotFound,
}

/// <summary>
/// Resultado del reordenamiento: el <see cref="Outcome"/> determina el status HTTP;
/// <see cref="Response"/> trae el override actualizado (200) y <see cref="Error"/> el
/// mensaje en caso de 422.
/// </summary>
public sealed class UpdateDocumentOrderOverrideResult
{
    private UpdateDocumentOrderOverrideResult(
        UpdateDocumentOrderOverrideOutcome outcome,
        DocumentOrderOverrideResponse? response,
        string? error)
    {
        Outcome = outcome;
        Response = response;
        Error = error;
    }

    public UpdateDocumentOrderOverrideOutcome Outcome { get; }

    public DocumentOrderOverrideResponse? Response { get; }

    public string? Error { get; }

    public static UpdateDocumentOrderOverrideResult Updated(DocumentOrderOverrideResponse response) =>
        new(UpdateDocumentOrderOverrideOutcome.Updated, response, null);

    public static UpdateDocumentOrderOverrideResult ValidationFailed(string error) =>
        new(UpdateDocumentOrderOverrideOutcome.ValidationFailed, null, error);

    public static UpdateDocumentOrderOverrideResult NotFound { get; } =
        new(UpdateDocumentOrderOverrideOutcome.NotFound, null, null);
}
