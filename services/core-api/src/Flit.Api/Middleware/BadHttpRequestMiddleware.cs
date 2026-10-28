using Microsoft.AspNetCore.Http;

namespace Flit.Api.Middleware;

/// <summary>
/// Bug #14010 H-02: un parámetro obligatorio ausente lanza <see cref="BadHttpRequestException"/> con el
/// nombre de la clase y la pila. Esta respuesta es 400 en español y sin ese detalle.
/// Los mensajes que el producto ya redactó (por ejemplo «No hay registros para exportar…») se conservan.
/// </summary>
public sealed class BadHttpRequestMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context).ConfigureAwait(false);
        }
        catch (BadHttpRequestException ex) when (!context.Response.HasStarted)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            await context.Response.WriteAsJsonAsync(
                new { error = BadHttpRequestMessages.For(ex) },
                context.RequestAborted).ConfigureAwait(false);
        }
    }
}

/// <summary>Texto público de un <see cref="BadHttpRequestException"/>.</summary>
public static class BadHttpRequestMessages
{
    public static string For(BadHttpRequestException ex)
    {
        var msg = ex.Message ?? "";
        if (msg.Contains("Required parameter", StringComparison.OrdinalIgnoreCase)
            && msg.Contains("DateOnly", StringComparison.Ordinal)
            && (msg.Contains(" from", StringComparison.OrdinalIgnoreCase) || msg.Contains(" to", StringComparison.OrdinalIgnoreCase)))
        {
            return "Debes indicar la fecha inicial y la fecha final.";
        }

        if (msg.Contains("Required parameter", StringComparison.OrdinalIgnoreCase)
            || msg.Contains("Microsoft.", StringComparison.Ordinal)
            || msg.Contains("lambda_method", StringComparison.Ordinal))
        {
            return "Faltan datos obligatorios en la consulta.";
        }

        return string.IsNullOrWhiteSpace(msg) ? "No se pudo procesar la consulta." : msg;
    }
}
