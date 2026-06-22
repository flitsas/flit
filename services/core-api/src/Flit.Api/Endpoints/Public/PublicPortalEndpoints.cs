using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Flit.Api.Endpoints.Public;

/// <summary>
/// Endpoints PÚBLICOS del portal de participantes (Slice 7 Part B). Sin auth ni tenant header: el
/// token (alta entropía, hasheado en BD) es la credencial. SEGURIDAD: token inválido/expirado/usado
/// → not_found genérico (sin enumeración de PII). El consentimiento Ley 1581 es obligatorio antes de
/// subir documentos. La IP/User-Agent se capturan en el endpoint de consentimiento como prueba de
/// auditoría. Rate-limiting diferido (deuda): el repo no expone aún un limitador reutilizable.
/// </summary>
internal static class PublicPortalEndpoints
{
    internal static IEndpointRouteBuilder MapPublicPortalEndpoints(this IEndpointRouteBuilder app)
    {
        // GET vista del portal por token -> 200 PortalViewDto (consent + pasos pendientes)
        app.MapGet("/api/v1/public/portal/{token}", async (
            string token,
            GetPortalByTokenHandler handler,
            CancellationToken ct) =>
        {
            var (result, error) = await handler.HandleAsync(token, ct);
            return error is "not_found"
                ? Results.Problem(statusCode: 404, title: "Not Found", detail: "Enlace de portal no encontrado.")
                : Results.Ok(result);
        }).WithName("GetPublicPortal").AllowAnonymous();

        // POST aceptar consentimiento Ley 1581 (captura IP + User-Agent como prueba de auditoría)
        app.MapPost("/api/v1/public/portal/{token}/consent", async (
            string token,
            HttpContext http,
            AceptarConsentimientoHandler handler,
            CancellationToken ct) =>
        {
            var ip = http.Connection.RemoteIpAddress?.ToString();
            var ua = http.Request.Headers.UserAgent.ToString();
            var input = new AceptarConsentimientoInput(ip, string.IsNullOrWhiteSpace(ua) ? null : ua);

            var (result, error) = await handler.HandleAsync(token, input, ct);
            return error is "not_found"
                ? Results.Problem(statusCode: 404, title: "Not Found", detail: "Enlace de portal no encontrado.")
                : Results.Ok(result);
        }).WithName("AcceptPublicPortalConsent").AllowAnonymous();

        // POST subir documento (multipart/form-data: file + tipo). Antiforgery deshabilitado (público).
        app.MapPost("/api/v1/public/portal/{token}/documentos", async (
            string token,
            [FromForm] string? tipo,
            IFormFile? file,
            SubirDocumentoPortalHandler handler,
            CancellationToken ct) =>
        {
            if (file is null || file.Length == 0)
                return Results.Problem(statusCode: 400, title: "Bad Request", detail: "Falta el archivo (file).");

            await using var stream = file.OpenReadStream();
            var input = new SubirDocumentoPortalInput(
                tipo ?? string.Empty, file.FileName, file.ContentType, file.Length, stream);

            var (result, error) = await handler.HandleAsync(token, input, ct);
            return error switch
            {
                "not_found" => Results.Problem(statusCode: 404, title: "Not Found", detail: "Enlace de portal no encontrado."),
                "consent_requerido" => Results.Problem(statusCode: 409, title: "Conflict", detail: "Debes aceptar el consentimiento antes de subir documentos."),
                "missing_file" => Results.Problem(statusCode: 400, title: "Bad Request", detail: "Falta el archivo (file)."),
                "invalid_tipo" => Results.Problem(statusCode: 400, title: "Bad Request", detail: "tipo inválido."),
                "invalid_mime" => Results.Problem(statusCode: 400, title: "Bad Request", detail: "Tipo MIME no permitido (use pdf/jpeg/png/webp)."),
                "file_too_large" => Results.Problem(statusCode: 400, title: "Bad Request", detail: "El archivo excede el máximo de 20 MB."),
                "not_draft" => Results.Problem(statusCode: 409, title: "Conflict", detail: "El trámite ya no admite cambios."),
                _ => Results.Ok(result),
            };
        }).WithName("UploadPublicPortalDocument").AllowAnonymous().DisableAntiforgery();

        // GET estado/URL de firma del participante -> 200 PortalFirmaUrlDto
        app.MapGet("/api/v1/public/portal/{token}/firma", async (
            string token,
            GetFirmaUrlPortalHandler handler,
            CancellationToken ct) =>
        {
            var (result, error) = await handler.HandleAsync(token, ct);
            return error is "not_found"
                ? Results.Problem(statusCode: 404, title: "Not Found", detail: "Enlace de portal no encontrado.")
                : Results.Ok(result);
        }).WithName("GetPublicPortalFirma").AllowAnonymous();

        // POST simular firma (mock complete) reusando SimularFirmaHandler -> 200 SimularFirmaResult
        app.MapPost("/api/v1/public/portal/{token}/firma/simulate", async (
            string token,
            SimularFirmaPortalHandler handler,
            CancellationToken ct) =>
        {
            var (result, error) = await handler.HandleAsync(token, ct);
            return error switch
            {
                "not_found" => Results.Problem(statusCode: 404, title: "Not Found", detail: "Enlace de portal no encontrado."),
                "firma_no_solicitada" => Results.Problem(statusCode: 409, title: "Conflict", detail: "La firma aún no ha sido solicitada por el gestor."),
                "signature_not_found" => Results.Problem(statusCode: 404, title: "Not Found", detail: "Firma no encontrada."),
                "estado_invalido" => Results.Problem(statusCode: 409, title: "Conflict", detail: "La firma no está en estado enviada."),
                _ => Results.Ok(result),
            };
        }).WithName("SimulatePublicPortalFirma").AllowAnonymous().DisableAntiforgery();

        // POST finalizar (revoca el token: uso único) -> 200 FinalizarPortalResult
        app.MapPost("/api/v1/public/portal/{token}/finalizar", async (
            string token,
            FinalizarPortalHandler handler,
            CancellationToken ct) =>
        {
            var (result, error) = await handler.HandleAsync(token, ct);
            return error is "not_found"
                ? Results.Problem(statusCode: 404, title: "Not Found", detail: "Enlace de portal no encontrado.")
                : Results.Ok(result);
        }).WithName("FinalizePublicPortal").AllowAnonymous().DisableAntiforgery();

        return app;
    }
}
