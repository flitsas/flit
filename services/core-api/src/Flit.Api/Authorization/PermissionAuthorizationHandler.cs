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
        // AC2/AC4 — permiso presente en el JWT o SuperAdmin; si no, no se llama Succeed y ASP.NET Core emite 403 (AC3).
        if (Grants(context.User, requirement.Slug))
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// SuperAdmin (bypass total, AC4) o slug presente en el claim "permissions" (AC2). Multi-rol (HU #10506): el JWT emite
    /// un claim "role_code" POR CADA rol activo, en orden no determinístico; se evalúan TODOS (fix post-review #10504).
    /// </summary>
    internal static bool Grants(ClaimsPrincipal user, string slug) =>
        user.Claims.Any(c => c.Type == "role_code" && string.Equals(c.Value, AdminAuthorization.SuperAdminRole, StringComparison.OrdinalIgnoreCase))
        || user.Claims.Any(c => c.Type == "permissions" && string.Equals(c.Value, slug, StringComparison.OrdinalIgnoreCase));
}
