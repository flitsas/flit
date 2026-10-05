using Flit.Api.Authorization;
using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Flit.Api.Endpoints.Tramites;

/// <summary>
/// HU #13296 (Feature #13282, Épica #13202) — revisión de la identidad manual, EXCLUSIVA del Super Admin.
/// <c>GET /api/v1/tramites/biometric-validations/manual</c> lista, de todas las compañías, las validaciones con
/// proveedor <c>manual</c> (contrato <c>EPICA-13202-contrato-api.md</c> §3). Vive en un archivo propio: las demás
/// rutas de <c>/biometric-validations</c> están en <see cref="BiometricaEndpoints"/>. El prefijo está bajo el
/// middleware de tenant, que ya responde 401 al anónimo; el Super Admin llega con alcance «todas las compañías».
/// </summary>
internal static class ManualIdentityReviewEndpoints
{
    internal const string SuperAdminRequiredCode = "super_admin_required";

    internal static IEndpointRouteBuilder MapManualIdentityReviewEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/tramites");

        group.MapGet("/biometric-validations/manual", async (
            [FromQuery] int? page,
            [FromQuery] int? pageSize,
            [FromQuery] string? status,
            [FromQuery] string? origin,
            [FromQuery] string? q,
            ListManualIdentityValidationsHandler handler,
            CancellationToken ct) =>
        {
            var query = new ListManualIdentityValidationsQuery(
                status, origin, q, page ?? 1, pageSize ?? ListManualIdentityValidationsQuery.DefaultPageSize);

            var (result, error) = await handler.HandleAsync(query, ct);
            return error is not null
                ? Results.Json(new { code = "parametro_invalido", message = error }, statusCode: StatusCodes.Status400BadRequest)
                : Results.Ok(result);
        })
            .AddEndpointFilter(new SuperAdminOnlyFilter())
            .WithName("ListManualIdentityValidations")
            .WithSummary("Validaciones de identidad manuales de todas las compañías (solo Super Admin)")
            .Produces<ManualListResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);

        // HU #13297 (Feature #13282 C2) — detalle de una validación manual de cualquier compañía. Cada consulta se audita.
        group.MapGet("/biometric-validations/{id:guid}/manual-detail", async (
            Guid id,
            HttpContext http,
            GetManualDetailHandler handler,
            CancellationToken ct) =>
        {
            if (!TryGetUserId(http, out var userId))
                return Results.Unauthorized();

            var (result, error) = await handler.HandleAsync(new GetManualDetailQuery(id, userId), ct);
            return error is null
                ? Results.Ok(result)
                : Results.Json(
                    new { code = GetManualDetailHandler.NoEncontrada, message = "Validación manual no encontrada." },
                    statusCode: StatusCodes.Status404NotFound);
        })
            .AddEndpointFilter(new SuperAdminOnlyFilter())
            .WithName("GetManualIdentityValidationDetail")
            .WithSummary("Detalle de una validación manual con el estado de sus 4 imágenes (solo Super Admin; auditado)")
            .Produces<ManualDetail>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound);

        // HU #13297 — imagen del ciclo actual (rostro|anverso|reverso|firma) entregada por este endpoint autenticado; nunca una
        // URL ni la ruta del storage. no-store: el navegador y los intermediarios no la conservan.
        group.MapGet("/biometric-validations/{id:guid}/manual-images/{kind}", async (
            Guid id,
            string kind,
            HttpContext http,
            GetManualImageHandler handler,
            CancellationToken ct) =>
        {
            if (!TryGetUserId(http, out var userId))
                return Results.Unauthorized();

            var (result, error) = await handler.HandleAsync(new GetManualImageQuery(id, kind, userId), ct);
            if (error is not null || result is null)
            {
                return Results.Json(
                    new { code = GetManualImageHandler.NoEncontrada, message = "Imagen no encontrada." },
                    statusCode: StatusCodes.Status404NotFound);
            }

            http.Response.Headers.CacheControl = "no-store";
            http.Response.Headers.Pragma = "no-cache";
            http.Response.Headers.XContentTypeOptions = "nosniff";
            return Results.File(result.Content, result.ContentType);
        })
            .AddEndpointFilter(new SuperAdminOnlyFilter())
            .WithName("GetManualIdentityValidationImage")
            .WithSummary("Imagen de la captura manual (solo Super Admin; auditado; Cache-Control: no-store)")
            .Produces(StatusCodes.Status200OK, contentType: "image/jpeg")
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound);

        return app;
    }

    /// <summary>Id del usuario autenticado (claim <c>sub</c> o nameidentifier).</summary>
    private static bool TryGetUserId(HttpContext http, out Guid userId)
    {
        var raw = http.User.FindFirst("sub")?.Value
            ?? http.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        return Guid.TryParse(raw, out userId) && userId != Guid.Empty;
    }

    /// <summary>401 al anónimo y 403 a cualquier rol que no sea Super Admin, antes de ejecutar la consulta.</summary>
    private sealed class SuperAdminOnlyFilter : IEndpointFilter
    {
        public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
        {
            ArgumentNullException.ThrowIfNull(context);
            ArgumentNullException.ThrowIfNull(next);

            var user = context.HttpContext.User;
            if (user.Identity?.IsAuthenticated != true)
                return Results.Unauthorized();

            if (!RequestTenantResolver.IsSuperAdmin(user))
            {
                return Results.Json(
                    new { code = SuperAdminRequiredCode, message = "Solo el Super Admin puede revisar validaciones manuales." },
                    statusCode: StatusCodes.Status403Forbidden);
            }

            return await next(context);
        }
    }
}
