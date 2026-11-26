using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Flit.Modules.Platform.Application.TenantProducts;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Flit.Infrastructure.Platform;

/// <summary>
/// HU #14711 — opciones del alta en el proveedor del impuesto (<see cref="Seccion"/>). Sin <see cref="IntegrationToken"/>, sin
/// <see cref="BaseUrl"/> o con una <see cref="BaseUrl"/> que no es https fuera de loopback, el cliente responde
/// <c>PROVEEDOR_NO_CONFIGURADO</c> sin enviar el Bearer; el host arranca igual.
/// </summary>
public sealed class OpcionesProveedorImpuesto
{
    public const string Seccion = "Platform:ImpuestoProveedor";

    /// <summary>
    /// Base del API del proveedor, sin <c>/</c> final. Debe ser https; http solo en loopback (en local, el stub:
    /// <c>http://127.0.0.1:8898</c>).
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>Token de integración (secreto crítico): solo por variable de entorno o secreto del ambiente, nunca en git.</summary>
    public string? IntegrationToken { get; set; }

    /// <summary>Timeout de cada llamada.</summary>
    public int TimeoutSeconds { get; set; } = 30;

    public override string ToString() =>
        $"OpcionesProveedorImpuesto {{ BaseUrl = {BaseUrl}, IntegrationToken = {(string.IsNullOrEmpty(IntegrationToken) ? "vacío" : "[REDACTADO]")}, TimeoutSeconds = {TimeoutSeconds} }}";
}

/// <summary>
/// HU #14711 (plan §3.1 punto 3; guía de integración §3) — cliente tipado del alta idempotente:
/// <c>POST {BaseUrl}/api/v1/integracion/empresas</c> con <c>Authorization: Bearer {IntegrationToken}</c> y el cuerpo exacto
/// <c>{legal_name, tax_id, contact_email}</c>. 200/201 ⇒ credenciales; 400 y 415 ⇒ <c>PROVEEDOR_SOLICITUD_INVALIDA</c>, 401 ⇒
/// <c>PROVEEDOR_TOKEN_INVALIDO</c>, 404 ⇒ <c>PROVEEDOR_INTEGRACION_DESHABILITADA</c> (otro 4xx ⇒ solicitud inválida), sin
/// reintento; 5xx, 429, timeout o red ⇒ UN reintento con el mismo cuerpo (el alta es idempotente) y luego
/// <c>PROVEEDOR_NO_DISPONIBLE</c>. Un 2xx sin llave legible también es no disponible. DTOs propios copiados del contrato, sin
/// referencia a core-impuestos. Nunca registra el token, la llave, el secreto ni el correo; el cuerpo de error del proveedor
/// no se copia a ningún mensaje.
/// </summary>
/// <example>
/// <code>
/// var r = await cliente.DarDeAltaAsync(new PeticionAltaProveedor("Flota Andina SAS", "900123456", "ops@flota.example"), ct);
/// </code>
/// </example>
internal sealed partial class ClienteAltaProveedorImpuesto(
    HttpClient http, IOptions<OpcionesProveedorImpuesto> opciones, ILogger<ClienteAltaProveedorImpuesto>? log = null)
    : IClienteAltaProveedorImpuesto
{
    public const string RutaAlta = "/api/v1/integracion/empresas";

    private const int Intentos = 2;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _http = http ?? throw new ArgumentNullException(nameof(http));
    private readonly OpcionesProveedorImpuesto _opciones = opciones?.Value ?? throw new ArgumentNullException(nameof(opciones));
    private readonly ILogger _log = (ILogger?)log ?? Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance;

    public async Task<RespuestaAltaProveedor> DarDeAltaAsync(PeticionAltaProveedor peticion, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(peticion);
        if (string.IsNullOrWhiteSpace(_opciones.IntegrationToken) || string.IsNullOrWhiteSpace(_opciones.BaseUrl)
            || !Uri.TryCreate(_opciones.BaseUrl.TrimEnd('/') + RutaAlta, UriKind.Absolute, out var destino))
        {
            SinConfigurar(_log);
            throw new AltaCredencialImpuestoException(CodigosAltaCredencialImpuesto.ProveedorNoConfigurado);
        }

        // L-2: el token de integración no sale por un canal sin cifrar; http solo hacia loopback (el stub local).
        if (!EsDestinoSeguro(destino))
        {
            SinHttps(_log);
            throw new AltaCredencialImpuestoException(CodigosAltaCredencialImpuesto.ProveedorNoConfigurado);
        }

        var cuerpo = new CuerpoAlta(peticion.LegalName, peticion.TaxId, peticion.ContactEmail);
        for (var intento = 1; ; intento++)
        {
            var (respuesta, codigo, reintentable) = await EnviarAsync(destino, cuerpo, cancellationToken).ConfigureAwait(false);
            if (respuesta is not null)
                return respuesta;
            if (!reintentable || intento >= Intentos)
            {
                Rechazada(_log, codigo, intento);
                throw new AltaCredencialImpuestoException(codigo);
            }

            Reintento(_log, intento);
        }
    }

    /// <summary>https, o http hacia loopback (127.0.0.0/8, ::1 o localhost).</summary>
    internal static bool EsDestinoSeguro(Uri destino) =>
        string.Equals(destino.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
        || (string.Equals(destino.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) && destino.IsLoopback);

    private async Task<(RespuestaAltaProveedor? Respuesta, string Codigo, bool Reintentable)> EnviarAsync(
        Uri destino, CuerpoAlta cuerpo, CancellationToken ct)
    {
        using var pedido = new HttpRequestMessage(HttpMethod.Post, destino) { Content = JsonContent.Create(cuerpo, options: Json) };
        pedido.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _opciones.IntegrationToken);
        try
        {
            using var r = await _http.SendAsync(pedido, HttpCompletionOption.ResponseContentRead, ct).ConfigureAwait(false);
            var status = (int)r.StatusCode;
            if (r.IsSuccessStatusCode)
            {
                var alta = await LeerAsync(r, ct).ConfigureAwait(false);
                return alta is null
                    ? (null, CodigosAltaCredencialImpuesto.ProveedorNoDisponible, true)
                    : (alta, string.Empty, false);
            }

            return r.StatusCode switch
            {
                HttpStatusCode.Unauthorized => (null, CodigosAltaCredencialImpuesto.ProveedorTokenInvalido, false),
                HttpStatusCode.NotFound => (null, CodigosAltaCredencialImpuesto.ProveedorIntegracionDeshabilitada, false),
                HttpStatusCode.TooManyRequests => (null, CodigosAltaCredencialImpuesto.ProveedorNoDisponible, true),
                _ when status >= 500 => (null, CodigosAltaCredencialImpuesto.ProveedorNoDisponible, true),
                _ => (null, CodigosAltaCredencialImpuesto.ProveedorSolicitudInvalida, false),
            };
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            // Timeout del HttpClient (no la cancelación de quien llama).
            return (null, CodigosAltaCredencialImpuesto.ProveedorNoDisponible, true);
        }
        catch (HttpRequestException)
        {
            return (null, CodigosAltaCredencialImpuesto.ProveedorNoDisponible, true);
        }
    }

    private static async Task<RespuestaAltaProveedor?> LeerAsync(HttpResponseMessage r, CancellationToken ct)
    {
        try
        {
            var dto = await r.Content.ReadFromJsonAsync<RespuestaAlta>(Json, ct).ConfigureAwait(false);
            return dto is { ApiKey: { Length: > 0 } llave }
                ? new RespuestaAltaProveedor(llave, string.IsNullOrEmpty(dto.WebhookSecret) ? null : dto.WebhookSecret, dto.Created ?? false, dto.ApiKeyRotated ?? false)
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Cuerpo EXACTO del alta (guía §3): tres campos, sin adicionales.</summary>
    private sealed record CuerpoAlta(
        [property: JsonPropertyName("legal_name")] string LegalName,
        [property: JsonPropertyName("tax_id")] string TaxId,
        [property: JsonPropertyName("contact_email")] string ContactEmail)
    {
        public override string ToString() => $"CuerpoAlta {{ TaxId = {PeticionAltaProveedor.EnmascararNit(TaxId)} }}";
    }

    /// <summary>Lo que se lee del 200/201 (guía §3). La empresa (<c>company</c>) no se usa.</summary>
    private sealed record RespuestaAlta(
        [property: JsonPropertyName("api_key")] string? ApiKey,
        [property: JsonPropertyName("webhook_secret")] string? WebhookSecret,
        [property: JsonPropertyName("created")] bool? Created,
        [property: JsonPropertyName("api_key_rotated")] bool? ApiKeyRotated)
    {
        public override string ToString() => $"RespuestaAlta {{ Created = {Created}, ApiKeyRotated = {ApiKeyRotated} }}";
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "El alta del proveedor del impuesto no está configurada (Platform__ImpuestoProveedor__BaseUrl / __IntegrationToken).")]
    private static partial void SinConfigurar(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "El alta del proveedor del impuesto no se envía: Platform__ImpuestoProveedor__BaseUrl no es https y no apunta a loopback.")]
    private static partial void SinHttps(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "El alta del proveedor del impuesto no salió en el intento {Intento}; se reintenta una vez.")]
    private static partial void Reintento(ILogger logger, int intento);

    [LoggerMessage(Level = LogLevel.Warning, Message = "El alta del proveedor del impuesto terminó en {Codigo} tras {Intentos} intento(s).")]
    private static partial void Rechazada(ILogger logger, string codigo, int intentos);
}

/// <summary>Registro del cliente del alta en los dos hosts de la plataforma (core-identity y el respaldo de core-api).</summary>
public static class ClienteAltaProveedorImpuestoExtensions
{
    /// <summary>
    /// Opciones <see cref="OpcionesProveedorImpuesto.Seccion"/> y el <c>HttpClient</c> tipado con su timeout. No valida al
    /// arrancar: sin token el endpoint responde <c>503 PROVEEDOR_NO_CONFIGURADO</c>.
    /// </summary>
    public static IServiceCollection AddClienteAltaProveedorImpuesto(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        services.Configure<OpcionesProveedorImpuesto>(configuration.GetSection(OpcionesProveedorImpuesto.Seccion));
        services.AddHttpClient<ClienteAltaProveedorImpuesto>((sp, cliente) =>
        {
            var segundos = sp.GetRequiredService<IOptions<OpcionesProveedorImpuesto>>().Value.TimeoutSeconds;
            cliente.Timeout = TimeSpan.FromSeconds(segundos > 0 ? segundos : 30);
        });
        services.TryAddTransient<IClienteAltaProveedorImpuesto>(sp => sp.GetRequiredService<ClienteAltaProveedorImpuesto>());
        return services;
    }

    /// <summary>
    /// El trabajador del alta automática de <c>diagnostico</c>, solo en core-identity. Se apaga con
    /// <see cref="TrabajadorAltaCredencialDiagnostico.ActivoKey"/>=false.
    /// </summary>
    public static IServiceCollection AddTrabajadorAltaCredencialDiagnostico(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        services.TryAddSingleton(TimeProvider.System);
        if (configuration.GetValue(TrabajadorAltaCredencialDiagnostico.ActivoKey, true))
            services.AddHostedService<TrabajadorAltaCredencialDiagnostico>();
        return services;
    }
}
