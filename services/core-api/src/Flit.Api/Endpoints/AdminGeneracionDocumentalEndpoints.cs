using System.Security.Claims;
using Flit.Admin.Application.GeneracionDocumental.GenerateRues;
using Flit.Api.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Flit.Api.Endpoints;

/// <summary>
/// Generación documental autónoma (Feature #12201, ADR-0056-generacion-documental-standalone):
/// emisión de documentos SIN abrir un trámite. Este archivo cubre el Certificado RUES (I1); el
/// historial, la descarga presignada y la transferencia llegan en HUs posteriores.
///
/// <para><b>Autorización POR PERMISO</b> (<c>generacion-documental.generate</c>), nunca por una
/// policy de grupo de SuperAdmin: eso dejaría fuera a AdminCompany, que es justamente el usuario del
/// módulo. Hay un test de contrato que escanea este archivo y falla si aparece esa policy —de ahí
/// que ni siquiera se la nombre aquí.</para>
///
/// <para><b>La generación NUNCA devuelve el binario</b> (decisión del PO): responde
/// <c>application/json</c> con <c>{ id, status }</c> con cualquier <c>Accept</c>, y el PDF se baja
/// después por <c>GET /{id}/download</c>. <c>status</c> solo puede valer <c>generated</c> o
/// <c>error</c> (CF-21).</para>
/// </summary>
public static class AdminGeneracionDocumentalEndpoints
{
    public static IEndpointRouteBuilder MapAdminGeneracionDocumentalEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var group = app
            .MapGroup("/api/v1/admin/generacion-documental")
            .WithTags("Admin · Generación documental");

        group.MapPost("/rues/preview", PreviewRuesAsync)
            .RequirePermission("generacion-documental.generate")
            .WithName("AdminGeneracionDocumentalPreviewRues")
            .WithSummary("Consulta el RUES por NIT para revisión previa, sin persistir nada")
            .WithDescription("Consulta EN VIVO el RUES por NIT y devuelve los campos mercantiles "
                + "para que el usuario los revise antes de emitir (CF-04). No crea ninguna fila en "
                + "admin.standalone_documents ni escribe archivo alguno. Requiere el permiso "
                + "generacion-documental.generate.")
            .Produces<PreviewRuesCompanyResult>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status502BadGateway);

        group.MapPost("/rues/generate", GenerateRuesAsync)
            .RequirePermission("generacion-documental.generate")
            .WithName("AdminGeneracionDocumentalGenerateRues")
            .WithSummary("Genera el Certificado RUES standalone (sin trámite)")
            .WithDescription("Consulta el RUES EN VIVO por NIT, congela el snapshot inmutable, "
                + "renderiza el PDF con el mismo generador del expediente y lo persiste en storage. "
                + "NO devuelve el PDF: responde { id, status } en application/json y la descarga va "
                + "siempre por GET /{id}/download. La cabecera Idempotency-Key, repetida dentro del "
                + "mismo tenant, devuelve el documento existente sin consultar al proveedor ni "
                + "escribir un archivo nuevo (CF-16). El documento queda en el tenant del JWT, "
                + "también para SuperAdmin. Requiere el permiso generacion-documental.generate.")
            .Produces<StandaloneDocumentGenerateResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status422UnprocessableEntity)
            .Produces(StatusCodes.Status502BadGateway)
            .Produces(StatusCodes.Status503ServiceUnavailable);

        return app;
    }

    /// <summary>Cuerpo de ambas rutas del Certificado RUES.</summary>
    public sealed record RuesRequest(string? Nit);

    /// <summary>
    /// Contrato de respuesta de la generación: solo id y estado. Sin PDF, sin snapshot y sin PII.
    /// </summary>
    public sealed record StandaloneDocumentGenerateResponse(Guid Id, string Status);

    // internal (no private): Flit.Admin.Tests verifica el contrato de la respuesta invocando el
    // delegate directamente (que sea application/json y nunca application/pdf).
    internal static async Task<IResult> PreviewRuesAsync(
        HttpContext httpContext,
        RuesRequest request,
        [FromServices] PreviewRuesCompanyHandler handler,
        CancellationToken cancellationToken)
    {
        if (!TryResolveTenantId(httpContext.User, out var tenantId))
        {
            return Unauthorized("Token inválido: falta claim tenant_id");
        }

        var result = await handler
            .HandleAsync(tenantId, request?.Nit, cancellationToken)
            .ConfigureAwait(false);

        return result.Error switch
        {
            null => Results.Ok(result),
            "invalid_request" => Results.Json(new { error = "invalid_request" }, statusCode: StatusCodes.Status400BadRequest),
            "provider_not_found" => Results.Json(new { error = "provider_not_found" }, statusCode: StatusCodes.Status503ServiceUnavailable),
            _ => Results.Json(new { error = "provider_unavailable" }, statusCode: StatusCodes.Status502BadGateway),
        };
    }

    internal static async Task<IResult> GenerateRuesAsync(
        HttpContext httpContext,
        RuesRequest request,
        [FromServices] GenerateRuesDocumentHandler handler,
        CancellationToken cancellationToken)
    {
        if (!TryResolveTenantId(httpContext.User, out var tenantId))
        {
            return Unauthorized("Token inválido: falta claim tenant_id");
        }

        var userId = ResolveUserId(httpContext.User);
        if (userId is null)
        {
            return Unauthorized("Token inválido: falta claim sub");
        }

        var idempotencyKey = httpContext.Request.Headers["Idempotency-Key"].ToString();

        var result = await handler
            .HandleAsync(
                new GenerateRuesDocumentCommand(
                    tenantId,
                    userId.Value,
                    request?.Nit,
                    string.IsNullOrWhiteSpace(idempotencyKey) ? null : idempotencyKey),
                cancellationToken)
            .ConfigureAwait(false);

        return result.Outcome switch
        {
            // 200 { id, status } en application/json, con cualquier Accept. Nunca application/pdf.
            GenerateRuesDocumentOutcome.Generated =>
                Results.Ok(new StandaloneDocumentGenerateResponse(result.Id!.Value, result.Status!)),

            GenerateRuesDocumentOutcome.InvalidRequest =>
                Results.Json(
                    new { error = result.ErrorCode, field = "nit" },
                    statusCode: StatusCodes.Status400BadRequest),

            GenerateRuesDocumentOutcome.RuesNotFound =>
                Results.Json(
                    new { error = result.ErrorCode, field = "nit", id = result.Id },
                    statusCode: StatusCodes.Status422UnprocessableEntity),

            GenerateRuesDocumentOutcome.ProviderNotFound =>
                Results.Json(
                    new { error = result.ErrorCode, id = result.Id },
                    statusCode: StatusCodes.Status503ServiceUnavailable),

            _ => Results.Json(
                new { error = result.ErrorCode, id = result.Id },
                statusCode: StatusCodes.Status502BadGateway),
        };
    }

    private static IResult Unauthorized(string message) =>
        Results.Json(new { error = message }, statusCode: StatusCodes.Status401Unauthorized);

    private static bool TryResolveTenantId(ClaimsPrincipal user, out Guid tenantId)
    {
        var claim = user.FindFirstValue(AdminAuthorization.TenantIdClaimType);
        return Guid.TryParse(claim, out tenantId);
    }

    private static Guid? ResolveUserId(ClaimsPrincipal user)
    {
        var sub = user.FindFirstValue("sub");
        return Guid.TryParse(sub, out var userId) ? userId : null;
    }
}
