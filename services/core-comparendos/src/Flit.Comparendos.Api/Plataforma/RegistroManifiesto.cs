using System.Net.Http.Headers;
using System.Net.Http.Json;
using Flit.Platform.Sdk.Grpc;

namespace Flit.Comparendos.Api.Plataforma;

/// <summary>
/// Registra el manifiesto del producto en la plataforma al arrancar (contrato v1 §6): <c>PUT
/// /api/v1/platform/products/comparendos/manifest</c> con el token de servicio <c>svc-comparendos</c> (scope
/// <c>platform.manifest</c>). Es idempotente y la plataforma nunca borra lo que ya existe. Si la plataforma no responde
/// se reintenta con espera creciente sin tumbar el servicio. Sin <c>Comparendos:Plataforma:GatewayUrl</c> no hace nada.
/// </summary>
internal sealed partial class RegistroManifiesto(
    IHttpClientFactory http,
    ServiceTokenProvider tokens,
    IConfiguration configuration,
    ILogger<RegistroManifiesto> logger) : BackgroundService
{
    public const string HttpClientName = "comparendos-plataforma";
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

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var gateway = configuration["Comparendos:Plataforma:GatewayUrl"];
        if (string.IsNullOrWhiteSpace(gateway))
        {
            LogSinGateway(logger);
            return;
        }

        var espera = TimeSpan.FromSeconds(5);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var token = await tokens.GetTokenAsync("platform.manifest", stoppingToken).ConfigureAwait(false);
                using var client = http.CreateClient(HttpClientName);
                using var request = new HttpRequestMessage(HttpMethod.Put, new Uri(new Uri(gateway.TrimEnd('/') + "/"), "api/v1/platform/products/comparendos/manifest"))
                {
                    Content = JsonContent.Create(Cuerpo()),
                };
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
                LogReintento(logger, espera.TotalSeconds, ex.Message);
            }

            await Task.Delay(espera, stoppingToken).ConfigureAwait(false);
            espera = TimeSpan.FromSeconds(Math.Min(espera.TotalSeconds * 2, 300));
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Manifiesto de Comparendos sin registrar: falta Comparendos:Plataforma:GatewayUrl.")]
    private static partial void LogSinGateway(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Manifiesto de Comparendos registrado ({Permisos} permisos).")]
    private static partial void LogRegistrado(ILogger logger, int permisos);

    [LoggerMessage(Level = LogLevel.Error, Message = "La plataforma rechazó el manifiesto de Comparendos: {Status} {Cuerpo}")]
    private static partial void LogRechazado(ILogger logger, int status, string cuerpo);

    [LoggerMessage(Level = LogLevel.Warning, Message = "No se pudo registrar el manifiesto de Comparendos; se reintenta en {Segundos} s: {Motivo}")]
    private static partial void LogReintento(ILogger logger, double segundos, string motivo);
}
