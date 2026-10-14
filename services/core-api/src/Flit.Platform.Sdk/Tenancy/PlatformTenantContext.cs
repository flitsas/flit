using System.Linq.Expressions;
using System.Security.Claims;
using Flit.Api.Authorization;
using Flit.Platform.Sdk.Authentication;

namespace Flit.Platform.Sdk.Tenancy;

/// <summary>
/// La empresa de una petición (Epic #13316, HU #13336; contrato v1.3 §3). Cerrado por defecto: sin empresa resuelta no
/// hay acceso a datos de ninguna, salvo el SuperAdmin, que ve todas.
/// <list type="bullet">
///   <item>Token de usuario: la empresa es su claim <c>tenant_id</c>. La cabecera <see cref="Header"/> se ignora: un
///   usuario nunca elige empresa.</item>
///   <item>Token de servicio: la empresa por la que actúa llega en <see cref="Header"/> (en gRPC, la metadata con el
///   mismo nombre en minúsculas, que ASP.NET Core lee igual).</item>
/// </list>
/// </summary>
public sealed record PlatformTenantContext(Guid? TenantId, bool IsSuperAdmin, bool IsService)
{
    public const string Header = "X-Flit-Tenant-Id";

    public static PlatformTenantContext None { get; } = new(null, false, false);

    public static PlatformTenantContext Resolve(ClaimsPrincipal user, IHeaderDictionary headers)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(headers);
        if (user.Identity?.IsAuthenticated != true)
            return None;

        if (PlatformPrincipal.IsService(user))
            return new PlatformTenantContext(NonEmpty(headers[Header].ToString()), IsSuperAdmin: false, IsService: true);

        return new PlatformTenantContext(
            NonEmpty(user.FindFirstValue(AdminAuthorization.TenantIdClaimType)), PlatformPrincipal.IsSuperAdmin(user), IsService: false);
    }

    private static Guid? NonEmpty(string? value) => Guid.TryParse(value, out var id) && id != Guid.Empty ? id : null;
}

/// <summary>Filtro por empresa que falla cerrado (HU #13336 AC2).</summary>
public static class PlatformTenantQueryableExtensions
{
    /// <summary>
    /// SuperAdmin: sin filtro. Con empresa: solo sus filas. Sin empresa: ninguna fila (nunca «todas»).
    /// </summary>
    public static IQueryable<T> ForTenant<T>(this IQueryable<T> source, PlatformTenantContext tenant, Expression<Func<T, Guid>> tenantOf)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(tenant);
        ArgumentNullException.ThrowIfNull(tenantOf);

        if (tenant.IsSuperAdmin)
            return source;
        if (tenant.TenantId is not { } id)
            return source.Where(_ => false);

        var equals = Expression.Equal(tenantOf.Body, Expression.Constant(id, typeof(Guid)));
        return source.Where(Expression.Lambda<Func<T, bool>>(equals, tenantOf.Parameters));
    }
}

/// <summary>Acceso a la empresa de la petición en curso desde los servicios de aplicación.</summary>
public interface IPlatformTenantAccessor
{
    PlatformTenantContext Current { get; }
}

internal sealed class HttpPlatformTenantAccessor(IHttpContextAccessor http) : IPlatformTenantAccessor
{
    public PlatformTenantContext Current =>
        http.HttpContext is { } context ? PlatformTenantContext.Resolve(context.User, context.Request.Headers) : PlatformTenantContext.None;
}
