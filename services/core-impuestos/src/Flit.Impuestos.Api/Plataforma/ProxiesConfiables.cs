using System.Net;
using Microsoft.AspNetCore.HttpOverrides;

namespace Flit.Impuestos.Api.Plataforma;

/// <summary>
/// IP real del cliente detrás del proxy (HU #14694, IMP-3 / M-1): <c>UseForwardedHeaders</c> toma <c>X-Forwarded-For</c> solo
/// si la conexión llega desde un proxy configurado (sección <see cref="Seccion"/>), y entonces
/// <see cref="ConnectionInfo.RemoteIpAddress"/> pasa a ser la IP del cliente. Así el límite por IP del webhook
/// (<see cref="LimitesSolicitudes.Avisos"/>) separa a cada cliente en vez de contar a todos como la IP del gateway. Una conexión
/// directa desde otra red conserva su IP: la cabecera de un cliente no elige el cupo.
/// <para>
/// Por defecto confía en las redes privadas (10.0.0.0/8, 172.16.0.0/12, 192.168.0.0/16, donde vive la red interna del compose,
/// <c>FLIT_INTERNAL_SUBNET</c>) y en loopback. <c>Redes</c> (CIDR) o <c>Proxies</c> (IP) en la configuración reemplazan las redes
/// privadas; loopback se conserva. Un solo salto (<c>ForwardLimit = 1</c>): se usa la última IP de la cabecera, la que puso el
/// proxy de confianza.
/// </para>
/// </summary>
/// <example>
/// <code>
/// builder.Services.AddProxiesConfiables(builder.Configuration);
/// app.UseProxiesConfiables(); // antes de UseRateLimiter
/// // Impuestos__ProxiesConfiables__Redes__0=10.114.40.0/24
/// </code>
/// </example>
internal static class ProxiesConfiables
{
    public const string Seccion = "Impuestos:ProxiesConfiables";

    /// <summary>Redes privadas por defecto (RFC 1918).</summary>
    public static readonly IReadOnlyList<string> RedesPorDefecto = ["10.0.0.0/8", "172.16.0.0/12", "192.168.0.0/16"];

    public static IServiceCollection AddProxiesConfiables(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var o = configuration.GetSection(Seccion).Get<OpcionesProxiesConfiables>() ?? new OpcionesProxiesConfiables();
        var redes = Redes(o);
        var proxies = Proxies(o);
        services.Configure<ForwardedHeadersOptions>(f =>
        {
            f.ForwardedHeaders = ForwardedHeaders.XForwardedFor;
            f.ForwardLimit = 1;
            foreach (var red in redes)
                f.KnownIPNetworks.Add(red);
            foreach (var proxy in proxies)
                f.KnownProxies.Add(proxy);
        });
        return services;
    }

    public static IApplicationBuilder UseProxiesConfiables(this IApplicationBuilder app) => app.UseForwardedHeaders();

    private static List<System.Net.IPNetwork> Redes(OpcionesProxiesConfiables o)
    {
        var valores = o.Redes is { Length: > 0 } || o.Proxies is { Length: > 0 } ? o.Redes ?? [] : [.. RedesPorDefecto];
        var redes = new List<System.Net.IPNetwork>();
        foreach (var valor in valores)
        {
            if (!System.Net.IPNetwork.TryParse(valor?.Trim(), out var red))
                throw new InvalidOperationException($"{Seccion}:Redes: «{valor}» no es una red CIDR (p. ej. 10.114.40.0/24).");
            redes.Add(red);
        }

        return redes;
    }

    private static List<IPAddress> Proxies(OpcionesProxiesConfiables o)
    {
        var proxies = new List<IPAddress>();
        foreach (var valor in o.Proxies ?? [])
        {
            if (!IPAddress.TryParse(valor?.Trim(), out var ip))
                throw new InvalidOperationException($"{Seccion}:Proxies: «{valor}» no es una IP.");
            proxies.Add(ip);
        }

        return proxies;
    }
}

/// <summary>Proxies de confianza de <see cref="ProxiesConfiables"/> (sección <c>Impuestos:ProxiesConfiables</c>).</summary>
internal sealed class OpcionesProxiesConfiables
{
    /// <summary>Redes CIDR de los proxies; vacío junto con <see cref="Proxies"/> = las redes privadas.</summary>
    public string[]? Redes { get; set; }

    /// <summary>IP sueltas de los proxies.</summary>
    public string[]? Proxies { get; set; }
}
