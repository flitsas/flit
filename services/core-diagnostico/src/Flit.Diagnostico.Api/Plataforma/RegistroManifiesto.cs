using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Flit.Platform.Sdk.Grpc;

namespace Flit.Diagnostico.Api.Plataforma;

/// <summary>
/// Registra el manifiesto del producto en la plataforma al arrancar (contrato v1 §6, HU #13724, portado de Comparendos):
/// <c>PUT /api/v1/platform/products/diagnostico/manifest</c> con el token de servicio <c>svc-diagnostico</c> (scope
/// <c>platform.manifest</c>). Es idempotente y la plataforma no borra nada. Si la plataforma no responde, reintenta con
/// espera creciente (<see cref="SiguienteEspera"/>) sin tumbar el servicio. Sin <c>Diagnostico:Plataforma:GatewayUrl</c>
/// no hace nada. Un 4xx definitivo (manifiesto inválido o en conflicto) se registra en el log y no se reintenta.
/// La espera entre intentos usa el reloj inyectado (<see cref="TimeProvider"/>, HU #13826) para que la secuencia pedida
/// se pueda comprobar sin depender del reloj real.
/// </summary>
internal sealed partial class RegistroManifiesto(
    IHttpClientFactory http,
    ServiceTokenProvider tokens,
    IConfiguration configuration,
    TimeProvider reloj,
    ILogger<RegistroManifiesto> logger) : BackgroundService
{
    public const string HttpClientName = "diagnostico-plataforma";
    public const string GatewayUrlKey = "Diagnostico:Plataforma:GatewayUrl";
    public const string EsperaInicialKey = "Diagnostico:Plataforma:EsperaInicialMs";
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
                LogReintento(logger, espera.TotalSeconds.ToString("0.###", CultureInfo.InvariantCulture), ex.Message);
            }

            await Task.Delay(espera, reloj, stoppingToken).ConfigureAwait(false);
            espera = SiguienteEspera(espera);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Manifiesto de Diagnóstico sin registrar: falta Diagnostico:Plataforma:GatewayUrl.")]
    private static partial void LogSinGateway(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Manifiesto de Diagnóstico registrado ({Permisos} permisos).")]
    private static partial void LogRegistrado(ILogger logger, int permisos);

    [LoggerMessage(Level = LogLevel.Error, Message = "La plataforma rechazó el manifiesto de Diagnóstico: {Status} {Cuerpo}")]
    private static partial void LogRechazado(ILogger logger, int status, string cuerpo);

    [LoggerMessage(Level = LogLevel.Warning, Message = "No se pudo registrar el manifiesto de Diagnóstico; se reintenta en {Segundos} s: {Motivo}")]
    private static partial void LogReintento(ILogger logger, string segundos, string motivo);
}
