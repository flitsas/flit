using System.Security.Claims;
using Flit.Api.Middleware;

namespace Flit.Api.Authorization;

/// <summary>
/// Punto único de resolución del cliente (tenant) de la petición (HU #12320, Feature #12254).
/// Reemplaza las copias locales de <c>TryResolveTenantId</c> que vivían en cada archivo de
/// endpoints: toda lectura del claim <see cref="AdminAuthorization.TenantIdClaimType"/> y de los
/// <see cref="HttpContext.Items"/> que puebla <see cref="TenantEnforcementMiddleware"/> pasa por aquí.
/// Un test de arquitectura (<c>TenantResolutionArchitectureTests</c>) falla si aparece otra copia.
/// </summary>
public static class RequestTenantResolver
{
    /// <summary>Claim alterno de rol (código) que también emite el JWT FLIT.</summary>
    private const string RoleCodeClaimType = "role_code";

    /// <summary>
    /// Tenant del JWT (<c>tenant_id</c>). Misma semántica que las copias locales que sustituye:
    /// devuelve <c>true</c> si el claim existe y parsea como <see cref="Guid"/>.
    /// <para>
    /// NOTA: <see cref="Guid.Empty"/> cuenta como resuelto (ninguna de las copias lo validaba;
    /// se conserva para que cada sitio de llamada devuelva exactamente el mismo resultado). Los
    /// consumidores que necesitan rechazar el vacío usan <see cref="TryResolveNonEmptyTenantId"/>.
    /// </para>
    /// </summary>
    public static bool TryResolveTenantId(ClaimsPrincipal user, out Guid tenantId)
    {
        ArgumentNullException.ThrowIfNull(user);
        return Guid.TryParse(user.FindFirstValue(AdminAuthorization.TenantIdClaimType), out tenantId);
    }

    /// <summary>
    /// Igual que <see cref="TryResolveTenantId"/> pero rechaza <see cref="Guid.Empty"/>
    /// (regla del middleware y de la telemetría: "sin compañía asignada").
    /// </summary>
    public static bool TryResolveNonEmptyTenantId(ClaimsPrincipal user, out Guid tenantId) =>
        TryResolveTenantId(user, out tenantId) && tenantId != Guid.Empty;

    /// <summary>Tenant del JWT como <see cref="Nullable{T}"/> (<c>null</c> si no resuelve).</summary>
    public static Guid? ResolveTenantIdOrNull(ClaimsPrincipal user) =>
        TryResolveTenantId(user, out var tenantId) ? tenantId : null;

    /// <summary>
    /// Regla de SuperAdmin del <see cref="TenantEnforcementMiddleware"/>: multi-rol (HU #10506), el JWT
    /// emite un claim por cada rol activo en orden no determinístico, así que se evalúan TODOS los claims
    /// de tipo <see cref="AdminAuthorization.RoleClaimType"/> o <c>role_code</c> (no solo el primero).
    /// </summary>
    public static bool IsSuperAdmin(ClaimsPrincipal user)
    {
        ArgumentNullException.ThrowIfNull(user);
        return user.Claims.Any(c =>
            (c.Type == AdminAuthorization.RoleClaimType || c.Type == RoleCodeClaimType)
            && string.Equals(c.Value, AdminAuthorization.SuperAdminRole, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Contexto de tenant que dejó el <see cref="TenantEnforcementMiddleware"/> en
    /// <see cref="HttpContext.Items"/> (solo rutas <see cref="TenantEnforcementMiddleware.RuntimeScopedRoutes"/>).
    /// <c>TenantId == null</c> significa "todos" (solo SuperAdmin sin acotar) o ruta no scopeada.
    /// </summary>
    public static (Guid? TenantId, bool IsSuperAdmin) FromItems(HttpContext http)
    {
        ArgumentNullException.ThrowIfNull(http);
        var isSuperAdmin = http.Items.TryGetValue(TenantEnforcementMiddleware.SuperAdminItemKey, out var sa)
            && sa is true;
        Guid? tenantId = http.Items.TryGetValue(TenantEnforcementMiddleware.TenantItemKey, out var t) && t is Guid g
            ? g
            : null;
        return (tenantId, isSuperAdmin);
    }
}
