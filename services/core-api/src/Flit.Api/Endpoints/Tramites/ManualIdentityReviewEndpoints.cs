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

        return app;
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
