using System.Security.Claims;
using Flit.Api.Authorization;
using Microsoft.AspNetCore.Http;

namespace Flit.Api.Endpoints.Analytics;

/// <summary>Helpers compartidos entre endpoints analíticos / reporting (tenant + errores 400).</summary>
internal static class AnalyticsEndpointsHelpers
{
    public static bool TryResolveTenant(
        ClaimsPrincipal user, Guid? tenantIdQuery, out Guid tenant, out IResult? error)
    {
        tenant = Guid.Empty;
        error = null;
        var isSuperAdmin = user.IsInRole(AdminAuthorization.SuperAdminRole);
        Guid? claimTenant = TryResolveTenantId(user, out var parsed) ? parsed : null;

        var (resolved, err) = Flit.Analytics.Application.Reporting.ReportingTenantAccess.Resolve(
            isSuperAdmin, claimTenant, tenantIdQuery);
        if (err is null)
        {
            tenant = resolved;
            return true;
        }

        error = err switch
        {
            "forbidden" => Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: "Forbidden",
                detail: "No está autorizado para consultar métricas de otro tenant."),
            "tenant_required" => Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Bad Request",
                detail: "Este endpoint requiere especificar un tenantId."),
            _ => Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Bad Request",
                detail: "Falta el tenant: el token no incluye tenant_id y no se indicó tenantId."),
        };
        return false;
    }

    private static bool TryResolveTenantId(ClaimsPrincipal user, out Guid tenantId)
    {
        var claim = user.FindFirstValue(AdminAuthorization.TenantIdClaimType);
        return Guid.TryParse(claim, out tenantId);
    }

    public static IResult InvalidRange() =>
        Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Bad Request",
            detail: "El rango de fechas es inválido: 'from' no puede ser posterior a 'to'.");
}
