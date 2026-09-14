using System.Security.Claims;
using Flit.Api.Authorization;
using Flit.Tramites.Application.BulkTramites;
using Flit.Tramites.Application.BulkTramites.Query;
using Flit.Tramites.Application.BulkTramites.SubmitBatch;
using Microsoft.AspNetCore.Mvc;

namespace Flit.Api.Endpoints.Tramites;

/// <summary>
/// Endpoints de carga masiva de trámites (HU #12520, Feature #12519). Exigen usuario autenticado
/// (cualquier JWT válido), <b>no</b> SuperAdmin ni un rol específico: el PO confirmó que si el
/// usuario puede radicar trámites, ve también la carga masiva — sin distinción por tenant.
/// </summary>
public static class BulkTramitesEndpoints
{
    public static IEndpointRouteBuilder MapBulkTramitesEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var group = app
            .MapGroup("/api/v1/tramites/carga-masiva")
            .RequireAuthorization();

        // GET /plantilla?tipo=matricula|traspaso|otros — AC1/AC3 → 200 (xlsx) / 400.
        group.MapGet("/plantilla", DescargarPlantillaAsync).WithName("BulkTramitesDescargarPlantilla");

        // POST /lotes?tipo=... — HU #12522 AC1/AC2/AC3 → 201 / 400 (template_invalid, too_many_rows, invalid_file).
        group.MapPost("/lotes", SubirLoteAsync)
            .WithName("BulkTramitesSubirLote")
            .DisableAntiforgery();

        // GET /lotes — HU #12524 AC1/AC2: resumen de los últimos lotes del cliente.
        group.MapGet("/lotes", ListarLotesAsync).WithName("BulkTramitesListarLotes");

        // GET /lotes/{id} — HU #12524: detalle fila a fila para saber qué retomar.
        group.MapGet("/lotes/{batchId:guid}", DetalleLoteAsync).WithName("BulkTramitesDetalleLote");

        return app;
    }

    private static async Task<IResult> ListarLotesAsync(
        HttpContext httpContext,
        [FromServices] GetBulkTramitesBatchesHandler handler,
        CancellationToken cancellationToken)
    {
        var tenantId = TenantDeLaPeticion(httpContext);
        if (tenantId is null)
        {
            return Results.Json(
                new ErrorResponse("Indique la compañía (header X-Tenant-Id)."),
                statusCode: StatusCodes.Status400BadRequest);
        }

        var lotes = await handler.ListAsync(tenantId.Value, cancellationToken).ConfigureAwait(false);
        return Results.Ok(new { items = lotes });
    }

    private static async Task<IResult> DetalleLoteAsync(
        Guid batchId,
        HttpContext httpContext,
        [FromServices] GetBulkTramitesBatchesHandler handler,
        CancellationToken cancellationToken)
    {
        var tenantId = TenantDeLaPeticion(httpContext);
        if (tenantId is null)
        {
            return Results.Json(
                new ErrorResponse("Indique la compañía (header X-Tenant-Id)."),
                statusCode: StatusCodes.Status400BadRequest);
        }

        var detalle = await handler.GetAsync(tenantId.Value, batchId, cancellationToken).ConfigureAwait(false);
        return detalle is null
            ? Results.NotFound(new ErrorResponse("No existe ese lote de carga masiva."))
            : Results.Ok(detalle);
    }

    private static async Task<IResult> SubirLoteAsync(
        [FromQuery] string? tipo,
        IFormFile? archivo,
        HttpContext httpContext,
        [FromServices] SubmitBulkTramitesBatchHandler handler,
        CancellationToken cancellationToken)
    {
        var tipoResuelto = BulkTramitesTemplateTypeParser.Parse(tipo);
        if (tipoResuelto is null)
        {
            return Results.Json(
                new ErrorResponse(
                    $"El tipo de plantilla '{tipo}' no está disponible para carga masiva. "
                    + "Usa 'matricula', 'traspaso' u 'otros'."),
                statusCode: StatusCodes.Status400BadRequest);
        }

        if (archivo is null || archivo.Length == 0)
        {
            return Results.Json(
                new ErrorResponse("Debes adjuntar el archivo Excel diligenciado."),
                statusCode: StatusCodes.Status400BadRequest);
        }

        var tenantId = TenantDeLaPeticion(httpContext);
        var userId = ResolveUserId(httpContext.User);
        if (tenantId is null || userId is null)
        {
            return Results.Json(
                new ErrorResponse("Indique la compañía (header X-Tenant-Id) o no fue posible resolver el usuario del token."),
                statusCode: StatusCodes.Status400BadRequest);
        }

        await using var contenido = archivo.OpenReadStream();
        var command = new SubmitBulkTramitesBatchCommand(
            tenantId.Value, userId.Value, tipoResuelto.Value, archivo.FileName, contenido);

        var resultado = await handler.HandleAsync(command, cancellationToken).ConfigureAwait(false);

        return resultado.Outcome switch
        {
            SubmitBulkTramitesBatchOutcome.Accepted => Results.Created(
                $"/api/v1/tramites/carga-masiva/lotes/{resultado.BatchId}",
                new BulkTramitesBatchAcceptedResponse(
                    resultado.BatchId!.Value, resultado.TotalRows, resultado.RowsWithStructuralErrors)),
            _ => Results.Json(new ErrorResponse(resultado.Error!), statusCode: StatusCodes.Status400BadRequest),
        };
    }

    /// <summary>
    /// Tenant que dejó el <c>TenantEnforcementMiddleware</c> en <c>HttpContext.Items</c> (HU #12320): la
    /// ruta está en <c>RuntimeScopedRoutes</c>, así que a un usuario de compañía se le impone su
    /// tenant desde el JWT y el SuperAdmin acota con X-Tenant-Id. <c>null</c> = SuperAdmin sin acotar.
    /// </summary>
    private static Guid? TenantDeLaPeticion(HttpContext http) => RequestTenantResolver.FromItems(http).TenantId;

    private static Guid? ResolveUserId(ClaimsPrincipal user)
    {
        var raw = user.FindFirstValue("sub") ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return Guid.TryParse(raw, out var userId) ? userId : null;
    }

    private sealed record BulkTramitesBatchAcceptedResponse(Guid BatchId, int TotalRows, int RowsWithStructuralErrors);

    private static async Task<IResult> DescargarPlantillaAsync(
        [FromQuery] string? tipo,
        HttpContext httpContext,
        [FromServices] IBulkTramitesXlsxTemplate template,
        CancellationToken cancellationToken)
    {
        var tipoResuelto = BulkTramitesTemplateTypeParser.Parse(tipo);
        if (tipoResuelto is null)
        {
            return Results.Json(
                new ErrorResponse(
                    $"El tipo de plantilla '{tipo}' no está disponible para carga masiva. "
                    + "Usa 'matricula', 'traspaso' u 'otros'."),
                statusCode: StatusCodes.Status400BadRequest);
        }

        // El tenant decide qué organismos de tránsito ofrece el desplegable de Matrícula.
        var tenantId = TenantDeLaPeticion(httpContext);
        if (tenantId is null)
        {
            return Results.Json(
                new ErrorResponse("Indique la compañía (header X-Tenant-Id)."),
                statusCode: StatusCodes.Status400BadRequest);
        }

        var archivo = await template
            .BuildAsync(tipoResuelto.Value, tenantId.Value, cancellationToken)
            .ConfigureAwait(false);
        return Results.File(archivo.Content, archivo.Mimetype, archivo.Filename);
    }

    private sealed record ErrorResponse(string Error);
}
