using System.Security.Claims;
using Microsoft.AspNetCore.Http;

namespace Flit.Api.Authorization;

/// <summary>
/// Acceso scoped a recursos de compañía por tenant (HU #11226 / #11228).
/// SuperAdmin: multi-tenant. AdminCompany: solo su <c>tenant_id</c> del JWT.
/// </summary>
public static class CompanyTenantAccess
{
    /// <summary>
    /// Resuelve <c>excludeTransitOffices</c>: solo SuperAdmin puede pedir inclusión de OT.
    /// Cualquier otro rol fuerza exclusión (<c>true</c>).
    /// </summary>
    public static bool ResolveExcludeTransitOffices(ClaimsPrincipal user, bool? requested)
    {
        ArgumentNullException.ThrowIfNull(user);

        if (IsSuperAdmin(user))
        {
            return requested ?? true;
        }

        return true;
    }

    /// <summary>
    /// <c>null</c> si el caller puede operar el <paramref name="tenantId"/>;
    /// si no, un <see cref="IResult"/> 403.
    /// </summary>
    public static IResult? ForbidIfForeignTenant(ClaimsPrincipal user, Guid tenantId)
    {
        ArgumentNullException.ThrowIfNull(user);

        if (IsSuperAdmin(user))
        {
            return null;
        }

        var raw = user.FindFirstValue(AdminAuthorization.TenantIdClaimType);
        if (!Guid.TryParse(raw, out var callerTenantId) || callerTenantId != tenantId)
        {
            return Results.Json(
                new
                {
                    error = "FORBIDDEN_TENANT",
                    message = "Solo puedes operar el tenant de tu compañía.",
                },
                statusCode: StatusCodes.Status403Forbidden);
        }

        return null;
    }

    public static bool IsSuperAdmin(ClaimsPrincipal user) =>
        user.Claims.Any(c =>
            c.Type == AdminAuthorization.RoleClaimType
            && string.Equals(c.Value, AdminAuthorization.SuperAdminRole, StringComparison.OrdinalIgnoreCase));
}
