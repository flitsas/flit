using System.Security.Claims;
using Flit.Admin.Domain.OtMetrics;
using Flit.Api.Authorization;
using Microsoft.AspNetCore.Http;

namespace Flit.Api.Endpoints.Analytics;

/// <summary>
/// Resolución de tenant para "Programación y alertas" con alcance Organismo de Tránsito (Reportes
/// 2.0, HU-D, tercera ola). Mismo eje invertido que <c>AdminOtMetricsEndpoints</c>: el tenant sale
/// SIEMPRE del claim JWT <c>tenant_id</c> — el organismo que ese tenant tiene asociado es "el suyo".
///
/// <para>SuperAdmin puede fijar <c>?transitOfficeId=</c> para administrar informes/alertas de un
/// organismo AJENO al suyo, igual que ya audita cualquier OT en el resto del módulo. A diferencia
/// de los reportes (que resuelven el organismo en tiempo de lectura), aquí hace falta resolver el
/// tenant DUEÑO de ese organismo de una vez: <c>report_schedules</c>/<c>alert_rules</c> se guardan
/// por <c>tenant_id</c>, no por id de organismo (no hay columna nueva — ver DDL §76), así que sin
/// este paso el CRUD reutilizado de compañía no sabría en qué tenant escribir.</para>
/// </summary>
internal static class AdminOtSchedulingTenantResolver
{
    public static async Task<(Guid Tenant, IResult? Error)> ResolveAsync(
        ClaimsPrincipal user,
        Guid? transitOfficeIdQuery,
        IOtMetricsReadRepository otMetrics,
        CancellationToken ct)
    {
        if (!TryResolveTenantId(user, out var jwtTenant))
        {
            return (Guid.Empty, Results.Json(
                new { error = "Token inválido: falta claim tenant_id" },
                statusCode: StatusCodes.Status401Unauthorized));
        }

        if (!IsSuperAdmin(user) || transitOfficeIdQuery is not Guid officeId || officeId == Guid.Empty)
        {
            return (jwtTenant, null);
        }

        var ownerTenant = await otMetrics
            .ResolveTenantIdForTransitOfficeAsync(officeId, ct)
            .ConfigureAwait(false);

        if (ownerTenant is null)
        {
            return (Guid.Empty, Results.Problem(statusCode: StatusCodes.Status400BadRequest,
                title: "Bad Request", detail: "transitOfficeId no existe en el catálogo OT."));
        }

        return (ownerTenant.Value, null);
    }

    public static Guid? TryResolveUserId(ClaimsPrincipal user)
    {
        var raw = user.FindFirst("sub")?.Value ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return Guid.TryParse(raw, out var id) ? id : null;
    }

    public static IResult ValidationProblem(string detail) =>
        Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Bad Request", detail: detail);

    public static IResult ScheduleNotFound() =>
        Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "Not Found",
            detail: "El informe programado no existe.");

    public static IResult AlertRuleNotFound() =>
        Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "Not Found",
            detail: "La regla de alerta no existe.");

    public static IResult AlertEventNotFound() =>
        Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "Not Found",
            detail: "El disparo de alerta no existe.");

    private static bool TryResolveTenantId(ClaimsPrincipal user, out Guid tenantId) =>
        Guid.TryParse(user.FindFirstValue(AdminAuthorization.TenantIdClaimType), out tenantId);

    private static bool IsSuperAdmin(ClaimsPrincipal user) =>
        user.IsInRole(AdminAuthorization.SuperAdminRole)
        || string.Equals(
            user.FindFirstValue(AdminAuthorization.RoleClaimType),
            AdminAuthorization.SuperAdminRole,
            StringComparison.OrdinalIgnoreCase);
}
