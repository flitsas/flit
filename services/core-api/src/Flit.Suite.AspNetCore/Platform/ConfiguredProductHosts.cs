using Flit.Modules.Platform.Application.Hosts;
using Flit.Modules.Security.Application.Products;
using Microsoft.Extensions.Options;

namespace Flit.Api.Platform;

/// <summary>
/// <see cref="IProductHosts"/> sobre <see cref="SuiteHostsOptions"/> (contrato v1 §1): la plataforma vive en
/// <c>[ambiente.]raíz</c> y cada producto en <c>[ambiente.]producto.raíz</c>. HU #12966 (B-06). El enlace de un
/// producto «próximamente» (<see cref="SuiteHostsOptions.ComingSoon"/>) lleva a su pantalla en el hub; su origen (el
/// de los retornos del login) no cambia. Las raíces alternativas (<see cref="SuiteHostsOptions.AlternateRoots"/>)
/// repiten el mismo reparto en otro dominio (PDN: <c>app.flitsas.com</c> y <c>&lt;producto&gt;.flitsas.com</c>).
/// </summary>
internal sealed class ConfiguredProductHosts(IOptionsMonitor<SuiteHostsOptions> options) : IProductHosts
{
    private const string ProductToken = "{product}";

    public string UrlFor(string productCode)
    {
        var o = options.CurrentValue;
        if (o.Overrides.TryGetValue(productCode, out var url))
            return url;

        return $"{o.Scheme}://{HostFor(o, productCode)}";
    }

    public string UrlFor(string productCode, string? requestHost)
    {
        var o = options.CurrentValue;
        if (o.Overrides.ContainsKey(productCode))
            return UrlFor(productCode);

        return AlternateRootOf(o, requestHost) is { } root
            ? $"{o.Scheme}://{AlternateHostFor(root, productCode)}"
            : UrlFor(productCode);
    }

    public string LinkFor(string productCode) => LinkFor(productCode, requestHost: null);

    public string LinkFor(string productCode, string? requestHost) =>
        IsComingSoon(productCode)
            ? $"{UrlFor(ProductCodes.Plataforma, requestHost).TrimEnd('/')}/proximamente/{productCode}"
            : UrlFor(productCode, requestHost);

    public IReadOnlyList<string> AllUrlsFor(string productCode)
    {
        var o = options.CurrentValue;
        var urls = new List<string> { UrlFor(productCode) };
        if (o.Overrides.ContainsKey(productCode))
            return urls;

        foreach (var root in ValidAlternateRoots(o))
        {
            var url = $"{o.Scheme}://{AlternateHostFor(root, productCode)}";
            if (!urls.Contains(url, StringComparer.OrdinalIgnoreCase))
                urls.Add(url);
        }

        return urls;
    }

    /// <summary>En la lista y sin un reemplazo explícito (en local, un reemplazo apunta a la app que sí corre).</summary>
    public bool IsComingSoon(string productCode)
    {
        var o = options.CurrentValue;
        return o.ComingSoon.Contains(productCode, StringComparer.Ordinal) && !o.Overrides.ContainsKey(productCode);
    }

    public string? ProductForHost(string? host)
    {
        if (string.IsNullOrWhiteSpace(host))
            return null;

        var o = options.CurrentValue;
        var bare = Bare(host);
        foreach (var code in ProductCodes.All)
        {
            if (o.Overrides.TryGetValue(code, out var url)
                && Uri.TryCreate(url, UriKind.Absolute, out var uri)
                && string.Equals(uri.Authority, host.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                return code;
            }

            if (string.Equals(bare, HostFor(o, code), StringComparison.Ordinal))
                return code;

            if (o.Overrides.ContainsKey(code))
                continue;

            foreach (var root in ValidAlternateRoots(o))
            {
                if (string.Equals(bare, AlternateHostFor(root, code), StringComparison.Ordinal))
                    return code;
            }
        }

        return null;
    }

    private static string HostFor(SuiteHostsOptions o, string productCode)
    {
        var labels = new List<string>(3);
        if (!string.IsNullOrWhiteSpace(o.Environment))
            labels.Add(o.Environment.Trim().ToLowerInvariant());
        if (productCode != ProductCodes.Plataforma)
            labels.Add(productCode);
        labels.Add(o.Root.Trim().ToLowerInvariant());
        return string.Join('.', labels);
    }

    private static string AlternateHostFor(SuiteAlternateRoot root, string productCode) =>
        productCode == ProductCodes.Plataforma
            ? root.Hub.Trim().ToLowerInvariant()
            : root.Products.Trim().ToLowerInvariant().Replace(ProductToken, productCode, StringComparison.Ordinal);

    /// <summary>La raíz alternativa a la que pertenece el host (su hub o cualquiera de sus productos), o <c>null</c>.</summary>
    private static SuiteAlternateRoot? AlternateRootOf(SuiteHostsOptions o, string? host)
    {
        if (string.IsNullOrWhiteSpace(host))
            return null;

        var bare = Bare(host);
        foreach (var root in ValidAlternateRoots(o))
        {
            foreach (var code in ProductCodes.All)
            {
                if (string.Equals(bare, AlternateHostFor(root, code), StringComparison.Ordinal))
                    return root;
            }
        }

        return null;
    }

    /// <summary>Raíces bien formadas: hub y patrón con <c>{product}</c>. Una entrada a medias se ignora.</summary>
    private static IEnumerable<SuiteAlternateRoot> ValidAlternateRoots(SuiteHostsOptions o) =>
        o.AlternateRoots.Where(r =>
            !string.IsNullOrWhiteSpace(r.Hub)
            && !string.IsNullOrWhiteSpace(r.Products)
            && r.Products.Contains(ProductToken, StringComparison.Ordinal));

    private static string Bare(string host) => host.Split(':')[0].Trim().TrimEnd('.').ToLowerInvariant();
}
