using System.Net;
using Flit.Api.RateLimiting;
using Flit.Tramites.Application.UseCases.ProcedureInstances.ManualCapture;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Flit.Api.Endpoints.Public;

/// <summary>
/// Endpoints PÚBLICOS de la captura manual de identidad (Feature #13281 B, Épica #13202): el cliente abre su enlace
/// <c>/captura-manual/[token]</c>, ve sus datos, acepta el consentimiento y (HU #13290) envía rostro, documento y firma.
/// Sin auth ni tenant header: el token (alta entropía, solo su hash en BD) es la credencial. Un token desconocido,
/// regenerado o de otro proveedor responde 404 genérico (sin revelar si existió). Errores: <c>{ code, message }</c>
/// (contrato §2). Limitado por IP con la policy <c>manual-capture</c>. Rutas independientes de <c>/public/biometric/{token}</c>
/// (proveedor mock), que no se toca.
/// </summary>
internal static class PublicManualCaptureEndpoints
{
    internal sealed record ManualCaptureError(string Code, string Message);

    internal sealed record ManualConsentRequest(bool Accepted, string? TextVersion);

    internal static IEndpointRouteBuilder MapPublicManualCaptureEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v1/public/manual-capture/{token}", async (
            string token,
            GetManualCaptureHandler handler,
            CancellationToken ct) =>
        {
            var (result, error) = await handler.HandleAsync(token, ct);
            return error is null ? Results.Ok(result) : Error(error);
        })
        .WithName("GetPublicManualCapture")
        .WithSummary("Datos de la sesión de captura manual (por token)")
        .AllowAnonymous()
        .RequireRateLimiting(ManualCaptureRateLimit.PolicyName)
        .Produces<ManualCaptureViewDto>(StatusCodes.Status200OK)
        .Produces<ManualCaptureError>(StatusCodes.Status404NotFound)
        .Produces<ManualCaptureError>(StatusCodes.Status409Conflict)
        .Produces<ManualCaptureError>(StatusCodes.Status410Gone);

        // Fecha y hora las toma el servidor y la IP se resuelve aquí: el cuerpo NUNCA trae ninguna de las dos.
        app.MapPost("/api/v1/public/manual-capture/{token}/consent", async (
            string token,
            ManualConsentRequest? body,
            HttpContext http,
            RegistrarConsentimientoManualHandler handler,
            CancellationToken ct) =>
        {
            var command = new RegistrarConsentimientoManualCommand(
                token, body?.Accepted ?? false, body?.TextVersion, ResolveClientIp(http));
            var error = await handler.HandleAsync(command, ct);
            return error is null ? Results.NoContent() : Error(error);
        })
        .WithName("AcceptPublicManualCaptureConsent")
        .WithSummary("Registra el consentimiento biométrico de la captura manual")
        .AllowAnonymous()
        .DisableAntiforgery()
        .RequireRateLimiting(ManualCaptureRateLimit.PolicyName)
        .Produces(StatusCodes.Status204NoContent)
        .Produces<ManualCaptureError>(StatusCodes.Status400BadRequest)
        .Produces<ManualCaptureError>(StatusCodes.Status404NotFound)
        .Produces<ManualCaptureError>(StatusCodes.Status409Conflict)
        .Produces<ManualCaptureError>(StatusCodes.Status410Gone);

        return app;
    }

    /// <summary>
    /// IP del cliente con el mismo criterio del proyecto (<c>HttpAuditContextAccessor</c> y los límites de tasa): primer hop de
    /// <c>X-Forwarded-For</c> que sella el borde, o la conexión. Solo se guarda si es una IP válida (nada de texto libre).
    /// </summary>
    internal static string? ResolveClientIp(HttpContext http)
    {
        var candidate = PublicBrandingRateLimit.ResolvePartitionKey(http);
        return IPAddress.TryParse(candidate, out var ip) ? ip.ToString() : http.Connection.RemoteIpAddress?.ToString();
    }

    internal static IResult Error(string code) => code switch
    {
        ManualCaptureErrors.NotFound => Json(code, "Enlace de captura no encontrado.", StatusCodes.Status404NotFound),
        ManualCaptureErrors.Expirada => Json(code, "El enlace de captura expiró.", StatusCodes.Status410Gone),
        ManualCaptureErrors.EstadoInvalido => Json(code, "El enlace ya no admite capturas.", StatusCodes.Status409Conflict),
        ManualCaptureErrors.ConsentimientoRequerido => Json(code, "Falta aceptar el consentimiento.", StatusCodes.Status409Conflict),
        ManualCaptureErrors.ConsentimientoNoAceptado => Json(code, "Debe aceptar el consentimiento para continuar.", StatusCodes.Status400BadRequest),
        ManualCaptureErrors.VersionTextoInvalida => Json(code, "La versión del texto de consentimiento no es la vigente.", StatusCodes.Status400BadRequest),
        _ => Json(code, "No se pudo procesar la solicitud.", StatusCodes.Status422UnprocessableEntity),
    };

    private static IResult Json(string code, string message, int status) =>
        Results.Json(new ManualCaptureError(code, message), statusCode: status);
}
