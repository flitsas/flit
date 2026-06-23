using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;

namespace Flit.Api.Authorization;

/// <summary>
/// Evalúa <see cref="PermissionRequirement"/> comparando los claims "permissions"
/// del JWT con el slug requerido (HU #10165).
///
/// Bypass SuperAdmin (AC4): si el claim "role_code" es "SuperAdmin" se concede
/// acceso sin importar los permisos del token.
///
/// Sin bypass: si el claim "permissions" contiene el slug → Succeed (AC2).
/// Si no lo contiene → no Succeed; el middleware devuelve 403 (AC3).
/// </summary>
public sealed class PermissionAuthorizationHandler
    : AuthorizationHandler<PermissionRequirement>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        PermissionRequirement requirement)
    {
        // AC4 — SuperAdmin bypass total
        var roleCode = context.User.FindFirstValue("role_code");
        if (string.Equals(roleCode, "SuperAdmin", StringComparison.OrdinalIgnoreCase))
        {
            context.Succeed(requirement);
            return Task.CompletedTask;
        }

        // AC2 — permiso presente en el JWT
        var hasPermission = context.User.Claims
            .Where(c => c.Type == "permissions")
            .Any(c => string.Equals(c.Value, requirement.Slug, StringComparison.OrdinalIgnoreCase));

        if (hasPermission)
        {
            context.Succeed(requirement);
        }
        // AC3 — sin el permiso: no se llama Succeed; ASP.NET Core emite 403 automáticamente.

        return Task.CompletedTask;
    }
}
