using Microsoft.AspNetCore.Builder;

namespace Flit.Api.Authorization;

/// <summary>
/// Extension method para proteger un endpoint minimal API con un slug de permiso
/// del JWT de Flit (HU #10165).
///
/// Uso:
/// <code>
/// app.MapGet("/api/v1/tramites", handler)
///    .RequirePermission("tramites.read");
/// </code>
///
/// Si el token contiene el slug en el claim "permissions" → 200.
/// Si el token es válido pero no contiene el slug → 403 (AC3).
/// Si no hay token → 401 (AC5, manejado por el middleware JWT).
/// SuperAdmin (role_code = "SuperAdmin") → siempre pasa (AC4).
/// </summary>
public static class RequirePermissionExtensions
{
    public static RouteHandlerBuilder RequirePermission(
        this RouteHandlerBuilder builder,
        string slug)
        => builder.RequireAuthorization(policy =>
            policy.AddRequirements(new PermissionRequirement(slug)));
}
