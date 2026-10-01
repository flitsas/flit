namespace Flit.Api.Authorization;

/// <summary>
/// HU #13087 — errores de <c>/api/v1/external/*</c> en RFC 7807 (contrato v3.1 §1) con el código
/// estable del contrato en la extensión <c>code</c> (p. ej. <c>invalid_client</c>).
/// </summary>
public static class ExternalProblem
{
    public static Task WriteAsync(HttpContext context, int status, string code, string detail, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        context.Response.StatusCode = status;
        context.Response.Headers.CacheControl = "no-store";
        return context.Response.WriteAsJsonAsync(
            new ExternalProblemBody("about:blank", code, status, detail, code),
            options: null,
            contentType: "application/problem+json",
            cancellationToken: cancellationToken);
    }
}

internal sealed record ExternalProblemBody(string Type, string Title, int Status, string Detail, string Code);
