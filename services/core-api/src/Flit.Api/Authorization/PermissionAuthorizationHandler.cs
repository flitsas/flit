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
        // AC4 — SuperAdmin bypass total. Multi-rol (HU #10506): el JWT emite un claim
        // "role_code" POR CADA rol activo, en orden no determinístico — FindFirstValue solo
        // evalúa el primero y puede negar el bypass si SuperAdmin no quedó primero. Se evalúan
        // TODOS los claims "role_code" (fix post-review #10504).
        var isSuperAdmin = context.User.Claims.Any(c =>
            c.Type == "role_code" && string.Equals(c.Value, AdminAuthorization.SuperAdminRole, StringComparison.OrdinalIgnoreCase));
        if (isSuperAdmin)
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
