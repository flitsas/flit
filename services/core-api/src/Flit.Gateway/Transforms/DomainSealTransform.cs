using Flit.Gateway.Configuration;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Yarp.ReverseProxy.Transforms;
using Yarp.ReverseProxy.Transforms.Builder;

namespace Flit.Gateway.Transforms;

/// <summary>
/// Sello del dominio de la petición (HU #12417 AC1/AC2/AC5, ADR-0060 D2): en TODA ruta YARP,
/// elimina cualquier <c>X-Flit-Domain</c> entrante y lo fija con el host real de la petición
/// (<see cref="HttpRequest.Host"/>, minúsculas, sin puerto — el nginx externo del borde preserva el
/// <c>Host</c> tal cual llegó, #12421). Excepción documentada
/// (<c>delta-hechos-post-adr.md</c> hecho 7): si la petición llega de una red interna de confianza
/// (<see cref="DomainSealOptions.InternalAllowedNetworks"/>) y YA trae el sello, se conserva
/// (normalizado) — es el frontend Next.js llamando por la red Docker interna, no un cliente
/// público. Para cualquier otro origen el sello del cliente se descarta SIEMPRE, incluida una
/// suplantación deliberada (AC5): el valor final es SIEMPRE el host real de la conexión, nunca el
/// que mandó el cliente.
/// </summary>
public sealed class DomainSealTransform(IOptionsMonitor<DomainSealOptions> options) : ITransformProvider
{
    public const string HeaderName = "X-Flit-Domain";

    private readonly IOptionsMonitor<DomainSealOptions> _options =
        options ?? throw new ArgumentNullException(nameof(options));

    public void ValidateRoute(TransformRouteValidationContext context)
    {
        // Se aplica a TODAS las rutas por igual (AC1) — nada que validar por ruta.
    }

    public void ValidateCluster(TransformClusterValidationContext context)
    {
        // Nada específico de clúster que validar.
    }

    public void Apply(TransformBuilderContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        context.AddRequestTransform(transformContext =>
        {
            var sealedHost = ResolveSealedHost(transformContext.HttpContext, _options.CurrentValue);

            transformContext.ProxyRequest.Headers.Remove(HeaderName);
            transformContext.ProxyRequest.Headers.TryAddWithoutValidation(HeaderName, sealedHost);

            return ValueTask.CompletedTask;
        });
    }

    /// <summary>Expuesto para pruebas unitarias directas (sin levantar YARP) — HU #12417 AC1/AC5.</summary>
    public static string ResolveSealedHost(HttpContext httpContext, DomainSealOptions options)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        ArgumentNullException.ThrowIfNull(options);

        var remoteIp = httpContext.Connection.RemoteIpAddress;
        var isInternalOrigin = TrustedNetworks.Contains(options.InternalAllowedNetworks, remoteIp);

        if (isInternalOrigin
            && httpContext.Request.Headers.TryGetValue(HeaderName, out var incoming)
            && !string.IsNullOrWhiteSpace(incoming.ToString()))
        {
            return Normalize(incoming.ToString());
        }

        // Camino público (por defecto, y AC5 — suplantación): SIEMPRE el host real de la conexión,
        // nunca lo que haya mandado el cliente en X-Flit-Domain.
        return Normalize(httpContext.Request.Host.Host);
    }

    private static string Normalize(string host) => host.Trim().ToLowerInvariant();
}
