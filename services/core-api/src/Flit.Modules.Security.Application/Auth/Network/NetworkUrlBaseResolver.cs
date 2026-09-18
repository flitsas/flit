namespace Flit.Modules.Security.Application.Auth.Network;

/// <summary>
/// Implementación de <see cref="INetworkUrlBaseResolver"/> (HU #12423) sobre
/// <see cref="ITenantNetworkMembership"/> (pertenencia a red + dominio activo de la cabeza, HU
/// #12422) — sin E/S propia, sin caché propia (se invoca solo al componer un correo, no en cada
/// petición runtime).
/// </summary>
public sealed class NetworkUrlBaseResolver(ITenantNetworkMembership networkMembership) : INetworkUrlBaseResolver
{
    private readonly ITenantNetworkMembership _networkMembership =
        networkMembership ?? throw new ArgumentNullException(nameof(networkMembership));

    public async Task<string> ForTenantAsync(
        Guid tenantId, string configuredBase, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(configuredBase);

        var membership = await _networkMembership.ResolveAsync(tenantId, cancellationToken).ConfigureAwait(false);

        return membership is { IsMarcaBlancaNetwork: true, ActiveHost: { } host }
            ? ComposeWithHost(host, configuredBase)
            : configuredBase;
    }

    public string ForRequestDomain(IDomainContextAccessor domainContext, string configuredBase)
    {
        ArgumentNullException.ThrowIfNull(domainContext);
        ArgumentNullException.ThrowIfNull(configuredBase);

        return domainContext is { Kind: DomainKind.Network, Host: { } host }
            ? ComposeWithHost(host, configuredBase)
            : configuredBase;
    }

    /// <summary>
    /// <c>https://{host}</c> + el path/query de <paramref name="configuredBase"/> (conserva la
    /// ruta relativa configurada, p. ej. <c>/invite/activate</c>). Host normalizado a minúsculas
    /// sin espacios, mismo criterio que <c>DomainContextMiddleware</c>.
    /// </summary>
    private static string ComposeWithHost(string host, string configuredBase)
    {
        var normalizedHost = host.Trim().ToLowerInvariant();
        var pathAndQuery = ExtractPathAndQuery(configuredBase);
        return $"https://{normalizedHost}{pathAndQuery}";
    }

    private static string ExtractPathAndQuery(string configuredBase)
    {
        if (Uri.TryCreate(configuredBase, UriKind.Absolute, out var uri))
            return uri.PathAndQuery;

        // Base configurada sin esquema/host (poco común, pero defensivo): se trata como path.
        return configuredBase.StartsWith('/') ? configuredBase : "/" + configuredBase;
    }
}
