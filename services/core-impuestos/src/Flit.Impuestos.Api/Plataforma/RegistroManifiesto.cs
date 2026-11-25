using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Flit.Platform.Sdk.Grpc;

namespace Flit.Impuestos.Api.Plataforma;

/// <summary>
/// Registra el manifiesto del producto en la plataforma al arrancar (plan §5.2): <c>PUT /api/v1/platform/products/impuestos/manifest</c>
/// con el token de servicio <c>svc-impuestos</c> (scope <c>platform.manifest</c>). Es idempotente y la plataforma no borra nada. Si
/// la plataforma no responde, reintenta con espera creciente (<see cref="SiguienteEspera"/>) sin tumbar el servicio. Sin
/// <see cref="GatewayUrlKey"/> no hace nada. Un 4xx definitivo se registra en el log y no se reintenta.
/// </summary>
internal sealed partial class RegistroManifiesto(
    IHttpClientFactory http,
    ServiceTokenProvider tokens,
    IConfiguration configuration,
    TimeProvider reloj,
    ILogger<RegistroManifiesto> logger) : BackgroundService
{
    public const string HttpClientName = "impuestos-plataforma";
    public const string GatewayUrlKey = "Impuestos:Plataforma:GatewayUrl";
    public const string EsperaInicialKey = "Impuestos:Plataforma:EsperaInicialMs";
    public const string RegistrarKey = "Impuestos:Plataforma:RegistrarManifiesto";
    public const string ManifestScope = "platform.manifest";

    private static readonly TimeSpan EsperaMaxima = TimeSpan.FromMinutes(5);
    private static readonly int TotalPermisos = Permisos.Modulos.Sum(m => m.Permisos.Length);

    public static object Cuerpo() => new
    {
        version = "1",
        modules = Permisos.Modulos.Select(m => new
        {
            code = m.Codigo,
            name = m.Nombre,
            permissions = m.Permisos.Select(p => new { slug = p.Slug, name = p.Nombre }),
        }),
        defaultRoles = Permisos.RolesPorDefecto.Select(r => new { code = r.Codigo, name = r.Nombre, permissions = r.Permisos }),
    };

    /// <summary>Espera creciente: el doble de la anterior, con techo de cinco minutos.</summary>
    public static TimeSpan SiguienteEspera(TimeSpan actual) =>
        TimeSpan.FromTicks(Math.Min(actual.Ticks * 2, EsperaMaxima.Ticks));

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var gateway = configuration[GatewayUrlKey];
        if (string.IsNullOrWhiteSpace(gateway))
        {
            LogSinGateway(logger);
            return;
        }

        var destino = new Uri(new Uri(gateway.TrimEnd('/') + "/"), "api/v1/platform/products/" + ServicioSettings.Codigo + "/manifest");
        var espera = TimeSpan.FromMilliseconds(configuration.GetValue(EsperaInicialKey, 5000));
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var token = await tokens.GetTokenAsync(ManifestScope, stoppingToken).ConfigureAwait(false);
                using var client = http.CreateClient(HttpClientName);
                using var request = new HttpRequestMessage(HttpMethod.Put, destino) { Content = JsonContent.Create(Cuerpo()) };
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                using var response = await client.SendAsync(request, stoppingToken).ConfigureAwait(false);
                if (response.IsSuccessStatusCode)
                {
                    LogRegistrado(logger, TotalPermisos);
                    return;
                }

                var cuerpo = await response.Content.ReadAsStringAsync(stoppingToken).ConfigureAwait(false);
                LogRechazado(logger, (int)response.StatusCode, cuerpo.Length > 300 ? cuerpo[..300] : cuerpo);
                if ((int)response.StatusCode is >= 400 and < 500 and not 401 and not 408 and not 429)
                    return;
            }
            catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or TaskCanceledException && !stoppingToken.IsCancellationRequested)
            {
                LogReintento(logger, espera.TotalSeconds.ToString("0.###", CultureInfo.InvariantCulture), ex.GetType().Name);
            }

            await Task.Delay(espera, reloj, stoppingToken).ConfigureAwait(false);
            espera = SiguienteEspera(espera);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Manifiesto de Impuestos sin registrar: falta Impuestos:Plataforma:GatewayUrl.")]
    private static partial void LogSinGateway(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Manifiesto de Impuestos registrado ({Permisos} permisos).")]
    private static partial void LogRegistrado(ILogger logger, int permisos);

    [LoggerMessage(Level = LogLevel.Error, Message = "La plataforma rechazó el manifiesto de Impuestos: {Status} {Cuerpo}")]
    private static partial void LogRechazado(ILogger logger, int status, string cuerpo);

    [LoggerMessage(Level = LogLevel.Warning, Message = "No se pudo registrar el manifiesto de Impuestos; se reintenta en {Segundos} s ({Motivo}).")]
    private static partial void LogReintento(ILogger logger, string segundos, string motivo);
}
