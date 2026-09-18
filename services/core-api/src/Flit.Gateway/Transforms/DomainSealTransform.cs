using System.Security.Cryptography;
using System.Text;
using Flit.Gateway.Configuration;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Yarp.ReverseProxy.Transforms;
using Yarp.ReverseProxy.Transforms.Builder;

namespace Flit.Gateway.Transforms;

/// <summary>
/// Sello del dominio de la petición (HU #12417 AC1/AC2/AC5, ADR-0060 D2; follow-up de seguridad
/// <c>delta-hechos-post-adr.md</c> hecho 24): en TODA ruta YARP, elimina cualquier
/// <c>X-Flit-Domain</c> entrante y lo fija con el host real de la petición
/// (<see cref="HttpRequest.Host"/>, minúsculas, sin puerto — el nginx externo del borde preserva el
/// <c>Host</c> tal cual llegó, #12421). Excepción documentada (hecho 7): si la petición llega de
/// una red interna de confianza (<see cref="DomainSealOptions.InternalAllowedNetworks"/>) Y trae
/// además la cabecera <c>X-Internal-Key</c> con el valor de <c>Internal:ApiKey</c>
/// (<see cref="InternalApiOptions"/>), se conserva el sello entrante (normalizado) — es el
/// frontend Next.js llamando por la red Docker interna, no un cliente público. Solo el CIDR ya NO
/// basta: con <c>docker-proxy</c>/DNAT las conexiones externas pueden llegar con IP del gateway del
/// bridge (hecho 24), así que se exige también la clave compartida en tiempo constante. Clave
/// vacía en configuración ⇒ nunca se confía (fail-closed). Para cualquier otro caso el sello del
/// cliente se descarta SIEMPRE, incluida una suplantación deliberada (AC5): el valor final es
/// SIEMPRE el host real de la conexión, nunca el que mandó el cliente.
/// </summary>
public sealed class DomainSealTransform(
    IOptionsMonitor<DomainSealOptions> options,
    IOptionsMonitor<InternalApiOptions> internalApiOptions) : ITransformProvider
{
    public const string HeaderName = "X-Flit-Domain";
    public const string InternalKeyHeaderName = "X-Internal-Key";

    private readonly IOptionsMonitor<DomainSealOptions> _options =
        options ?? throw new ArgumentNullException(nameof(options));
    private readonly IOptionsMonitor<InternalApiOptions> _internalApiOptions =
        internalApiOptions ?? throw new ArgumentNullException(nameof(internalApiOptions));

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
            var sealedHost = ResolveSealedHost(
                transformContext.HttpContext,
                _options.CurrentValue,
                _internalApiOptions.CurrentValue);

            transformContext.ProxyRequest.Headers.Remove(HeaderName);
            transformContext.ProxyRequest.Headers.TryAddWithoutValidation(HeaderName, sealedHost);

            return ValueTask.CompletedTask;
        });
    }

    /// <summary>Expuesto para pruebas unitarias directas (sin levantar YARP) — HU #12417 AC1/AC5 (follow-up de seguridad, hecho 24).</summary>
    public static string ResolveSealedHost(
        HttpContext httpContext,
        DomainSealOptions options,
        InternalApiOptions internalApiOptions)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(internalApiOptions);

        var remoteIp = httpContext.Connection.RemoteIpAddress;
        var isInternalOrigin = TrustedNetworks.Contains(options.InternalAllowedNetworks, remoteIp);
        var hasValidInternalKey = HasValidInternalKey(httpContext, internalApiOptions.ApiKey);

        if (isInternalOrigin
            && hasValidInternalKey
            && httpContext.Request.Headers.TryGetValue(HeaderName, out var incoming)
            && !string.IsNullOrWhiteSpace(incoming.ToString()))
        {
            return Normalize(incoming.ToString());
        }

        // Camino público (por defecto, y AC5 — suplantación): SIEMPRE el host real de la conexión,
        // nunca lo que haya mandado el cliente en X-Flit-Domain. También aplica si falta o no
        // coincide X-Internal-Key, aunque la IP remota caiga en el CIDR interno (hecho 24: con
        // docker-proxy/DNAT un cliente público puede llegar con IP del gateway del bridge).
        return Normalize(httpContext.Request.Host.Host);
    }

    /// <summary>
    /// Compara <c>X-Internal-Key</c> contra la clave configurada en tiempo constante. Clave
    /// configurada vacía ⇒ nunca se confía (fail-closed), aunque la cabecera venga presente.
    /// </summary>
    private static bool HasValidInternalKey(HttpContext httpContext, string configuredApiKey)
    {
        if (string.IsNullOrEmpty(configuredApiKey))
        {
            return false;
        }

        if (!httpContext.Request.Headers.TryGetValue(InternalKeyHeaderName, out var provided))
        {
            return false;
        }

        var providedValue = provided.ToString();
        var expectedBytes = Encoding.UTF8.GetBytes(configuredApiKey);
        var providedBytes = Encoding.UTF8.GetBytes(providedValue);

        if (providedBytes.Length != expectedBytes.Length)
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(providedBytes, expectedBytes);
    }

    private static string Normalize(string host) => host.Trim().ToLowerInvariant();
}
