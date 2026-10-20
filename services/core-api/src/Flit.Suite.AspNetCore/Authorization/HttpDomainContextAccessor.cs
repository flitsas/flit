using Flit.Api.Middleware;
using Flit.Modules.Security.Application.Auth;

namespace Flit.Api.Authorization;

/// <summary>
/// Implementación HTTP de <see cref="IDomainContextAccessor"/> (HU #12417) sobre lo que deja
/// <see cref="DomainContextMiddleware"/> en <c>HttpContext.Items</c>. Fuera de una petición HTTP,
/// o si el middleware no corrió antes (tests que no montan el pipeline) ⇒ FLIT (fail-closed, igual
/// que <see cref="DomainContext.Flit(string?)"/>).
/// </summary>
internal sealed class HttpDomainContextAccessor(IHttpContextAccessor httpContextAccessor) : IDomainContextAccessor
{
    private readonly IHttpContextAccessor _httpContextAccessor =
        httpContextAccessor ?? throw new ArgumentNullException(nameof(httpContextAccessor));

    private DomainContext Current =>
        _httpContextAccessor.HttpContext?.Items.TryGetValue(DomainContextMiddleware.ItemKey, out var value) == true
            && value is DomainContext domainContext
            ? domainContext
            : DomainContext.Flit(null);

    public DomainKind Kind => Current.Kind;

    public string? Host => Current.Host;

    public Guid? HeadTenantId => Current.HeadTenantId;

    public string ProductCode => Current.ProductCode;
}
