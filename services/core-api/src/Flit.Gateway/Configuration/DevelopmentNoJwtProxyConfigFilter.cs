using Yarp.ReverseProxy.Configuration;

namespace Flit.Gateway.Configuration;

/// <summary>
/// En Development el front aún no envía JWT; quita AuthorizationPolicy de todas las rutas YARP.
/// QA/PDN conservan JwtRequired vía appsettings.json.
/// </summary>
public sealed class DevelopmentNoJwtProxyConfigFilter : IProxyConfigFilter
{
    public ValueTask<RouteConfig> ConfigureRouteAsync(
        RouteConfig route,
        ClusterConfig? cluster,
        CancellationToken cancel) =>
        ValueTask.FromResult(route with { AuthorizationPolicy = null });

    public ValueTask<ClusterConfig> ConfigureClusterAsync(ClusterConfig cluster, CancellationToken cancel) =>
        ValueTask.FromResult(cluster);
}
