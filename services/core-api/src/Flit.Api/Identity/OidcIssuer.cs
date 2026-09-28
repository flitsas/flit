using Flit.Modules.Platform.Application.Hosts;
using Flit.Modules.Security.Application.Auth;
using Flit.Modules.Security.Application.Products;

namespace Flit.Api.Identity;

/// <summary>
/// Emisor OIDC de una petición (FLIT Suite A-05, HU #12990; espiga A-04, pregunta 2). Sale SOLO del host sellado por el
/// gateway (<c>X-Flit-Domain</c>, ADR-0060), nunca de un parámetro, <c>Origin</c> ni <c>Referer</c>:
/// <list type="bullet">
/// <item>el host de la plataforma en este ambiente (<c>dev.flitsas.online</c>) o el dominio de una red ⇒ ese host;</item>
/// <item>en local, un host de loopback con reemplazo de <c>plataforma</c> en <c>Suite:Hosts:Overrides</c> ⇒ ese reemplazo;</item>
/// <item>cualquier otro caso (un servicio que llama por la red interna, un host de producto, sin sello) ⇒ el hub del
/// ambiente, para que el token de servicio lleve un emisor conocido.</item>
/// </list>
/// </summary>
public static class OidcIssuer
{
    public static Uri Resolve(IDomainContextAccessor domain, IProductHosts hosts, string scheme)
    {
        var host = domain.Host;
        if (!string.IsNullOrWhiteSpace(host))
        {
            if (domain.Kind == DomainKind.Network || hosts.ProductForHost(host) == ProductCodes.Plataforma)
                return new Uri($"{scheme}://{host}/", UriKind.Absolute);

            if (IsLoopback(host) && TryAbsolute(hosts.UrlFor(ProductCodes.Plataforma), out var local) && IsLoopback(local.Host))
                return local;
        }

        return TryAbsolute(hosts.UrlFor(ProductCodes.Plataforma), out var hub)
            ? hub
            : throw new InvalidOperationException("Suite:Hosts no produce una URL válida para la plataforma.");
    }

    private static bool IsLoopback(string host) =>
        host is "localhost" or "127.0.0.1" or "[::1]" || host.EndsWith(".localhost", StringComparison.Ordinal);

    private static bool TryAbsolute(string url, out Uri uri)
    {
        var ok = Uri.TryCreate(url.EndsWith('/') ? url : url + "/", UriKind.Absolute, out var parsed);
        uri = parsed!;
        return ok;
    }
}
