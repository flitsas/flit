using Flit.Admin.Domain.Companies.Domains;
using Flit.Modules.Platform.Application.Apps;
using Flit.Modules.Security.Application.Products;

namespace Flit.Api.Endpoints.Platform;

/// <summary>
/// Dominios de una red de Marca Blanca con la suite (HU #12968, A-08): el dominio <c>HUB</c> es el hub y el login de la
/// red; cada producto puede tener el suyo (<c>admin.tenant_domains.purpose</c> = código del producto). Siempre https:
/// es lo único que el servidor OIDC acepta como retorno a un dominio de red (<c>OidcNetworkRedirects</c>).
/// </summary>
internal static class NetworkHosts
{
    /// <summary><c>purpose</c> del dominio de red que sirve <paramref name="productCode"/>.</summary>
    public static string PurposeFor(string productCode) =>
        productCode == ProductCodes.Plataforma ? TenantDomainPurposes.Hub : productCode;

    /// <summary>URL del hub de la red, o <c>null</c> si la red no tiene dominio <c>HUB</c> activo.</summary>
    public static string? HubUrl(IReadOnlyDictionary<string, string> hostsByPurpose) =>
        hostsByPurpose.TryGetValue(TenantDomainPurposes.Hub, out var hub) ? $"https://{hub}" : null;

    /// <summary>
    /// El menú de productos visto desde un dominio de la red: cada producto con dominio en la red se abre en él (la sesión
    /// vale en todos los dominios de la misma red; en uno de FLIT daría <c>SESSION_DOMAIN_MISMATCH</c>), y «Próximamente»
    /// se muestra en el hub de la red. Un producto sin dominio en la red conserva el enlace de FLIT.
    /// </summary>
    public static IReadOnlyList<MyAppView> Rebase(IReadOnlyList<MyAppView> apps, IReadOnlyDictionary<string, string> hostsByPurpose, string? currentHost)
    {
        var hub = HubUrl(hostsByPurpose);
        return apps.Select(app =>
        {
            if (app.ComingSoon)
                return hub is null ? app : app with { Url = $"{hub}/proximamente/{app.Code}" };

            return hostsByPurpose.TryGetValue(PurposeFor(app.Code), out var host)
                ? app with { Url = $"https://{host}", Current = string.Equals(host, currentHost, StringComparison.Ordinal) }
                : app;
        }).ToList();
    }
}
