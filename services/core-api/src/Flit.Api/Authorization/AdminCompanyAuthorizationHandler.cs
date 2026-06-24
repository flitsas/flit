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
        var roleCode = context.User.FindFirst(AdminAuthorization.RoleClaimType)?.Value;

        if (roleCode == AdminAuthorization.AdminCompanyRole ||
            roleCode == AdminAuthorization.SuperAdminRole)
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}
