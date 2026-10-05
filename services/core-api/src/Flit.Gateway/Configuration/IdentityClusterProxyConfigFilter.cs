using Yarp.ReverseProxy.Configuration;

namespace Flit.Gateway.Configuration;

/// <summary>
/// HU #13225 (Epic #13217) — manda a <c>core-identity</c> las rutas marcadas con <c>Metadata.FlitIdentity = "true"</c>
/// (las del login: <c>/connect</c>, <c>/.well-known</c>, <c>/api/v1/auth</c>, <c>/api/v1/platform</c>,
/// <c>/api/v1/public/branding</c>). Solo cambia el destino: la política de autorización y las transformaciones de cada
/// ruta se quedan igual. Se registra únicamente con <c>Gateway:IdentityCluster:Enabled</c>; sin ella esas rutas van a
/// <c>core-api</c> como siempre. Ver <c>docs/suite/identidad-frontera.md</c> §3 y §4.
/// </summary>
public sealed class IdentityClusterProxyConfigFilter : IProxyConfigFilter
{
    public const string EnabledKey = "Gateway:IdentityCluster:Enabled";
    public const string MetadataKey = "FlitIdentity";
    public const string ClusterId = "core-identity-cluster";

    public ValueTask<RouteConfig> ConfigureRouteAsync(RouteConfig route, ClusterConfig? cluster, CancellationToken cancel) =>
        ValueTask.FromResult(IsIdentityRoute(route) ? route with { ClusterId = ClusterId } : route);

    public ValueTask<ClusterConfig> ConfigureClusterAsync(ClusterConfig cluster, CancellationToken cancel) =>
        ValueTask.FromResult(cluster);

    public static bool IsIdentityRoute(RouteConfig route) =>
        route.Metadata?.TryGetValue(MetadataKey, out var value) == true
        && string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
}
