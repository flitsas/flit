using Flit.Admin.Domain.Companies.Domains;
using Flit.Api.Endpoints.Platform;
using Flit.Api.RateLimiting;
using Flit.Modules.Security.Application.Auth;
using Flit.Modules.Security.Application.Products;

namespace Flit.Api.Endpoints.Public;

/// <summary>
/// <c>GET /api/v1/public/network</c> — hub de la red del dominio sellado (Marca Blanca con la suite, A-08). Lo consulta
/// <c>@flit/auth</c> desde el servidor de cada app antes del authorize: en un dominio de red el emisor es el hub de ESA
/// red, no el de FLIT (por el de FLIT un usuario de red recibe <c>NETWORK_DOMAIN_REQUIRED</c> y el retorno a un dominio
/// de red se rechaza). Igual que <c>/public/branding</c>, el dominio sale solo del sello y la respuesta tiene la misma
/// forma siempre: <c>{ hubUrl: null }</c> en FLIT, en un host desconocido o en una red sin dominio <c>HUB</c> activo.
/// <c>tramitesUrl</c> es el dominio de Trámites de la red: el hub enlaza allí su administración, que aún vive en Trámites
/// y en el dominio de FLIT rechazaría al usuario de la red.
/// </summary>
internal static class PublicNetworkEndpoints
{
    internal static IEndpointRouteBuilder MapPublicNetworkEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v1/public/network", async (
            IDomainContextAccessor domain,
            ITenantDomainRepository domains,
            HttpResponse response,
            CancellationToken ct) =>
        {
            string? hubUrl = null;
            string? tramitesUrl = null;
            if (domain is { Kind: DomainKind.Network, HeadTenantId: { } head })
            {
                var hosts = await domains.ListActiveHostsByPurposeAsync(head, ct).ConfigureAwait(false);
                hubUrl = NetworkHosts.HubUrl(hosts);
                tramitesUrl = NetworkHosts.ProductUrl(hosts, ProductCodes.Tramites);
            }

            response.Headers.CacheControl = "public, max-age=60";
            response.Headers["Vary"] = "X-Flit-Domain";
            return Results.Ok(new PublicNetworkView(hubUrl, tramitesUrl));
        }).WithName("GetPublicNetwork").WithTags("Public").AllowAnonymous().RequireRateLimiting(PublicBrandingRateLimit.PolicyName);

        return app;
    }

    internal sealed record PublicNetworkView(string? HubUrl, string? TramitesUrl);
}
