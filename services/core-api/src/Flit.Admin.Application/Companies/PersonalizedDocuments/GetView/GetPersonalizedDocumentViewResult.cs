namespace Flit.Admin.Application.Companies.PersonalizedDocuments.GetView;

/// <summary>Desenlace de la vista previa de una versión (HU #11314).</summary>
public enum GetPersonalizedDocumentViewOutcome
{
    Found,

    /// <summary>404 — no existe (incluye el negativo de aislamiento: tenant ajeno) o el binario no
    /// se puede leer/firmar en storage.</summary>
    NotFound,
}

/// <summary>
/// Resultado de la vista previa: <see cref="GetPersonalizedDocumentViewOutcome.Found"/> (200,
/// <c>{ url, expiresAt }</c>) · <c>NotFound</c> (404).
/// </summary>
public sealed class GetPersonalizedDocumentViewResult
{
    private GetPersonalizedDocumentViewResult(
        GetPersonalizedDocumentViewOutcome outcome, string? url, DateTimeOffset? expiresAt)
    {
        Outcome = outcome;
        Url = url;
        ExpiresAt = expiresAt;
    }

    public GetPersonalizedDocumentViewOutcome Outcome { get; }
    public string? Url { get; }
    public DateTimeOffset? ExpiresAt { get; }

    public static GetPersonalizedDocumentViewResult Found(string url, DateTimeOffset expiresAt) =>
        new(GetPersonalizedDocumentViewOutcome.Found, url, expiresAt);

    public static GetPersonalizedDocumentViewResult NotFound() =>
        new(GetPersonalizedDocumentViewOutcome.NotFound, null, null);
}
