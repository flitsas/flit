using System.Globalization;
using System.Security.Claims;
using Flit.Admin.Application.GeneracionDocumental.Download;
using Flit.Admin.Application.GeneracionDocumental.GenerateRues;
using Flit.Admin.Application.GeneracionDocumental.List;
using Flit.Admin.Domain.GeneracionDocumental;
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

        // Historial: la RAIZ del grupo, no una sub-ruta /documentos. Es lo que declara el diseno
        // 7.1 y lo que consume el frontend (GENERACION_DOCUMENTAL_API_BASE).
        group.MapGet("", ListDocumentosAsync)
            .RequirePermission("generacion-documental.read")
            .WithName("AdminGeneracionDocumentalList")
            .WithSummary("Historial paginado de documentos generados por la compania")
            .WithDescription("Devuelve la metadata minima del historial (tipo, escenario, empresa, "
                + "usuario, fecha y resultado) filtrable por tipo, rango de fechas, usuario y "
                + "estado (CF-17/CF-18). NUNCA expone document_snapshot ni rues_snapshot (PII alta) "
                + "ni rutas de storage ni URLs firmadas. El parametro opcional tenantId solo lo "
                + "honra un SuperAdmin y aun asi devuelve metadata, nunca contenido (CF-20). "
                + "Requiere el permiso generacion-documental.read.")
            .Produces<StandaloneDocumentsPageResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);

        group.MapGet("/{id:guid}/download", DownloadDocumentoAsync)
            .RequirePermission("generacion-documental.read")
            .WithName("AdminGeneracionDocumentalDownload")
            .WithSummary("Presigned URL de descarga del PDF ya generado y auditoria (CF-19)")
            .WithDescription("Devuelve una presigned URL de vida corta (ADR-0029) del PDF ya "
                + "generado y, en la misma operacion, incrementa download_count y actualiza "
                + "downloaded_at con un unico UPDATE atomico. No regenera nada: no se invoca ningun "
                + "generador ni proveedor externo y storage_sha256 no cambia. Un id de otra "
                + "compania responde 404 sin revelar su existencia (tambien para SuperAdmin), y un "
                + "documento que no este en estado generated responde 409 sin URL. "
                + "Requiere el permiso generacion-documental.read.")
            .Produces<StandaloneDocumentDownloadResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict);

        return app;
    }

    /// <summary>Cuerpo de ambas rutas del Certificado RUES.</summary>
    public sealed record RuesRequest(string? Nit);

    /// <summary>
    /// Contrato de respuesta de la generación: solo id y estado. Sin PDF, sin snapshot y sin PII.
    /// </summary>
    public sealed record StandaloneDocumentGenerateResponse(Guid Id, string Status);

    /// <summary>
    /// Fila del historial (CF-17). Deliberadamente SIN snapshot, sin ruta de storage y sin hash:
    /// lo que no esta en el contrato no se puede filtrar por descuido desde el frontend.
    /// </summary>
    public sealed record StandaloneDocumentListResponse(
        Guid Id,
        string DocumentType,
        string? Scenario,
        string Status,
        string? ErrorCode,
        string? Filename,
        string? CompanyName,
        Guid CreatedByUserId,
        string? CreatedByUserName,
        DateTimeOffset CreatedAt);

    public sealed record StandaloneDocumentsPageResponse(
        IReadOnlyList<StandaloneDocumentListResponse> Items,
        int Page,
        int PageSize,
        int Total);

    /// <summary>Respuesta de la descarga: URL firmada y su vencimiento. Nada mas.</summary>
    public sealed record StandaloneDocumentDownloadResponse(string Url, DateTimeOffset ExpiresAt);

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

    internal static async Task<IResult> ListDocumentosAsync(
        HttpContext httpContext,
        [FromServices] ListStandaloneDocumentsHandler handler,
        CancellationToken cancellationToken)
    {
        if (!TryResolveTenantId(httpContext.User, out var tenantId))
        {
            return Unauthorized("Token invalido: falta claim tenant_id");
        }

        var q = httpContext.Request.Query;

        if (!TryParseDate(q["dateFrom"], out var dateFrom) || !TryParseDate(q["dateTo"], out var dateTo))
        {
            return Results.Json(
                new { error = "invalid_request", field = "date" },
                statusCode: StatusCodes.Status400BadRequest);
        }

        // "hasta el 9" debe incluir el 9 completo: una fecha sin hora se convierte en el limite
        // superior EXCLUSIVO del dia siguiente.
        if (dateTo is { } hasta && !q["dateTo"].ToString().Contains('T', StringComparison.Ordinal))
        {
            dateTo = hasta.AddDays(1);
        }

        Guid? requestedTenantId = Guid.TryParse(q["tenantId"], out var otroTenant) ? otroTenant : null;
        Guid? userId = Guid.TryParse(q["userId"], out var autor) ? autor : null;
        var documentType = q["documentType"].ToString();

        var page = await handler
            .HandleAsync(
                new ListStandaloneDocumentsQuery
                {
                    TenantId = tenantId,
                    IsSuperAdmin = IsSuperAdmin(httpContext.User),
                    RequestedTenantId = requestedTenantId,
                    DocumentType = string.IsNullOrWhiteSpace(documentType) ? null : documentType,
                    Statuses = [.. q["status"].Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s!)],
                    DateFrom = dateFrom,
                    DateTo = dateTo,
                    CreatedByUserId = userId,
                    Page = int.TryParse(q["page"], NumberStyles.Integer, CultureInfo.InvariantCulture, out var p) ? p : 1,
                    PageSize = int.TryParse(q["pageSize"], NumberStyles.Integer, CultureInfo.InvariantCulture, out var ps) ? ps : 20,
                },
                cancellationToken)
            .ConfigureAwait(false);

        return Results.Ok(new StandaloneDocumentsPageResponse(
            [.. page.Items.Select(ToResponse)],
            page.Page,
            page.PageSize,
            page.Total));
    }

    internal static async Task<IResult> DownloadDocumentoAsync(
        HttpContext httpContext,
        Guid id,
        [FromServices] GetStandaloneDocumentDownloadHandler handler,
        CancellationToken cancellationToken)
    {
        if (!TryResolveTenantId(httpContext.User, out var tenantId))
        {
            return Unauthorized("Token invalido: falta claim tenant_id");
        }

        // SIEMPRE el tenant del JWT: no hay parametro que permita descargar de otra compania.
        var result = await handler
            .HandleAsync(tenantId, id, cancellationToken)
            .ConfigureAwait(false);

        return result.Outcome switch
        {
            StandaloneDocumentDownloadOutcome.Ok =>
                Results.Ok(new StandaloneDocumentDownloadResponse(result.Link!.Url, result.Link.ExpiresAt)),

            // 409: existe en MI tenant pero no esta generado. Se puede decir el estado porque el
            // documento es mio; no hay URL, ni snapshot, ni PII.
            StandaloneDocumentDownloadOutcome.NotGenerated =>
                Results.Json(
                    new { error = "conflict", status = result.Status },
                    statusCode: StatusCodes.Status409Conflict),

            // 404 escueto: no existe, es de otro tenant o perdio su binario. Un cuerpo distinto por
            // caso permitiria distinguirlos, y con eso enumerar documentos ajenos.
            _ => Results.Json(new { error = "not_found" }, statusCode: StatusCodes.Status404NotFound),
        };
    }

    private static StandaloneDocumentListResponse ToResponse(StandaloneDocumentListItem item) => new(
        item.Id,
        item.DocumentType,
        item.Scenario,
        item.Status,
        item.ErrorCode,
        item.Filename,
        item.CompanyName,
        item.CreatedByUserId,
        item.CreatedByUserName,
        item.CreatedAt);

    private static bool TryParseDate(string? value, out DateTimeOffset? parsed)
    {
        parsed = null;
        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        if (DateTimeOffset.TryParse(
                value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var date))
        {
            parsed = date;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Rol del JWT. Solo habilita el filtro OPCIONAL tenantId del listado (metadata global, CF-20);
    /// no concede descarga cruzada, no da acceso a snapshots y no protege ninguna ruta: la
    /// autorizacion de este modulo es SIEMPRE por permiso.
    /// </summary>
    private static bool IsSuperAdmin(ClaimsPrincipal user) =>
        user.IsInRole(AdminAuthorization.SuperAdminRole);

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
