using Flit.Modules.Platform.Application.Hosts;
using Flit.Modules.Security.Application.Products;
using Microsoft.Extensions.Options;

namespace Flit.Api.Platform;

/// <summary>
/// <see cref="IProductHosts"/> sobre <see cref="SuiteHostsOptions"/> (contrato v1 §1): la plataforma vive en
/// <c>[ambiente.]raíz</c> y cada producto en <c>[ambiente.]producto.raíz</c>. HU #12966 (B-06).
/// </summary>
internal sealed class ConfiguredProductHosts(IOptionsMonitor<SuiteHostsOptions> options) : IProductHosts
{
    public string UrlFor(string productCode)
    {
        var o = options.CurrentValue;
        if (o.Overrides.TryGetValue(productCode, out var url))
            return url;

        return $"{o.Scheme}://{HostFor(o, productCode)}";
    }

    public string? ProductForHost(string? host)
    {
        if (string.IsNullOrWhiteSpace(host))
            return null;

        var o = options.CurrentValue;
        var bare = host.Split(':')[0].Trim().TrimEnd('.').ToLowerInvariant();
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
}
