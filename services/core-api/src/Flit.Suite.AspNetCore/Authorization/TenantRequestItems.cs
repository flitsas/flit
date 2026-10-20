namespace Flit.Api.Authorization;

/// <summary>
/// Claves de <c>HttpContext.Items</c> donde el enforcement de Trámites deja el tenant resuelto del token (Epic #13217,
/// HU #13232: salieron de <c>TenantEnforcementMiddleware</c> para que <see cref="RequestTenantResolver"/> pueda
/// compartirse con core-identity). Los valores no cambian.
/// </summary>
public static class TenantRequestItems
{
    public const string Tenant = "tramites.tenantId";

    public const string SuperAdmin = "tramites.isSuperAdmin";

    public const string TenantScope = "tramites.tenantScope";
}
