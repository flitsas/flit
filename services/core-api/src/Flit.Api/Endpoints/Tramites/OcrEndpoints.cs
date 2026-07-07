using Flit.Tramites.Application.Ocr;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Flit.Api.Endpoints.Tramites;

/// <summary>
/// Endpoint de OCR semántico de documentos de trámites: analiza un documento con el modelo de visión
/// ANTES de subirlo al expediente (S3). No asocia nada a la instancia ni persiste el resultado — es
/// stateless. Seguridad e inquilino vía TenantEnforcementMiddleware + header X-Tenant-Id (mismo patrón
/// que AttachmentEndpoints; sin RequireAuthorization propio).
/// </summary>
internal static class OcrEndpoints
{
    internal static IEndpointRouteBuilder MapTramitesOcrEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/tramites");

        // POST /ocr/{tipo} (multipart/form-data: file) -> 200 { ok, tipo, data, extractedPdfBase64 }
        // tipo ∈ { factura, aduana, impronta, soat }. Máx 10 MB. Formato validado por magic bytes.
        // extractedPdfBase64: PDF recortado (base64) cuando el documento ocupaba sólo un subconjunto de
        // páginas de un PDF multi-documento; null si no hubo recorte (imagen, PDF de una página, o el
        // tipo abarca todo el PDF). El frontend sube ese recorte al expediente en vez del PDF completo.
        group.MapPost("/ocr/{tipo}", async (
            string tipo,
            [FromHeader(Name = "X-Tenant-Id")] Guid? tenantId,
            IFormFile? file,
            AnalyzeDocumentHandler handler,
            CancellationToken ct) =>
        {
            if (tenantId is null || tenantId == Guid.Empty)
                return Results.Problem(statusCode: 400, title: "Bad Request", detail: "Falta header X-Tenant-Id");
            if (file is null || file.Length == 0)
                return Results.Problem(statusCode: 400, title: "Bad Request", detail: "Falta el archivo (file).");
            if (file.Length > AnalyzeDocumentHandler.MaxFileBytes)
                return Results.Problem(statusCode: 400, title: "Bad Request", detail: "Archivo máximo 10MB");

            using var ms = new MemoryStream();
            await file.CopyToAsync(ms, ct);

            var (result, failure) = await handler.HandleAsync(tipo, ms.ToArray(), ct);
            return failure is not null
                ? Results.Problem(statusCode: failure.Status, title: "OCR", detail: failure.Message)
                : Results.Ok(result);
        })
        .WithName("AnalyzeTramiteDocumentOcr")
        .DisableAntiforgery();

        return app;
    }
}
