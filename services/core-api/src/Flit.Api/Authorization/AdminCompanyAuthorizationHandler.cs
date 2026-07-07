using Microsoft.AspNetCore.Authorization;

namespace Flit.Api.Authorization;

/// <summary>
/// Concede acceso si el JWT tiene role_code == "AdminCompany" o "SuperAdmin".
/// Permite que SuperAdmin acceda a todas las operaciones de empresa.
/// </summary>
public sealed class AdminCompanyAuthorizationHandler
    : AuthorizationHandler<AdminCompanyRequirement>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        AdminCompanyRequirement requirement)
    {
        // Multi-rol (HU #10506): el JWT emite un claim "role" POR CADA rol activo, en orden no
        // determinístico — FindFirst solo evalúa el primero. Se evalúan TODOS los claims del
        // tipo relevante (fix post-review #10504).
        var roleCodes = context.User.Claims
            .Where(c => c.Type == AdminAuthorization.RoleClaimType)
            .Select(c => c.Value);

        if (roleCodes.Contains(AdminAuthorization.AdminCompanyRole) ||
            roleCodes.Contains(AdminAuthorization.SuperAdminRole))
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}
