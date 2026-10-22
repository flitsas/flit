using System.Text.Json.Serialization;

namespace Flit.Api.Authorization;

/// <summary>
/// HU #13087 — errores de <c>/api/v1/external/*</c> en RFC 7807 (contrato v3.1 §1) con el código
/// estable del contrato en la extensión <c>code</c> (p. ej. <c>invalid_client</c>).
/// </summary>
public static class ExternalProblem
{
    public static Task WriteAsync(HttpContext context, int status, string code, string detail, CancellationToken cancellationToken = default)
    {
        return WriteAsync(context, status, code, detail, estado: null, terminal: null, cancellationToken);
    }

    /// <summary>
    /// HU #13263 — 409 <c>not_allowed_in_state</c>: suma las extensiones <c>estado</c> (estado actual del trámite)
    /// y <c>terminal</c> (si el trámite ya no volverá a aceptar el envío).
    /// </summary>
    public static Task WriteAsync(
        HttpContext context, int status, string code, string detail, string? estado, bool? terminal,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        context.Response.StatusCode = status;
        context.Response.Headers.CacheControl = "no-store";
        return context.Response.WriteAsJsonAsync(
            new ExternalProblemBody("about:blank", code, status, detail, code, estado, terminal),
            options: null,
            contentType: "application/problem+json",
            cancellationToken: cancellationToken);
    }
}

internal sealed record ExternalProblemBody(
    string Type,
    string Title,
    int Status,
    string Detail,
    string Code,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Estado = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] bool? Terminal = null);
