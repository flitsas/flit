using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace Flit.Impuestos.Api.Modulos.ApiLiquidacion;

/// <summary>
/// Cliente propio del API de liquidación (plan §5.6, DEC B2; no reutiliza el de core-consultas, EP-CF1). Cada operación lleva
/// su credencial en la petición, nunca en el cliente compartido: el alta con <c>Authorization: Bearer {INTEGRATION_TOKEN}</c>, el
/// resto con <c>X-API-Key</c> de la compañía. Nunca lanza por la red: devuelve un <see cref="ResultadoEnvio{T}"/> clasificado por
/// <see cref="ClasificadorEnvio"/>. Sí lanza, sin llamar, si falta configuración o si la petición viola el contrato (campos del
/// contrato anterior). No registra cuerpos ni cabeceras (el registro de <c>HttpClient</c> las redacta, EP-CF3a).
/// </summary>
/// <example>
/// <code>
/// var alta = await api.CrearEmpresaAsync(new AltaEmpresaPeticion("Flota Andina SAS", "900123456", "ops@flota.example"), ct);
/// var fuentes = await api.ObtenerFuentesAsync(apiKey, ct);
/// </code>
/// </example>
public sealed class ClienteApiLiquidacion(HttpClient http, IOptions<OpcionesApiLiquidacion> opciones)
{
    public const string RutaAlta = "/api/v1/integracion/empresas";
    public const string RutaFuentes = "/api/v1/fuentes";
    public const string RutaConsultas = "/api/v1/consultas";
    public const string RutaArchivos = "/api/v1/files";
    public const string CabeceraApiKey = "X-API-Key";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.General);

    private OpcionesApiLiquidacion Opciones => opciones.Value;

    /// <summary><c>POST /api/v1/integracion/empresas</c> con el token de integración y el cuerpo exacto del contrato.</summary>
    public Task<ResultadoEnvio<AltaEmpresaRespuesta>> CrearEmpresaAsync(AltaEmpresaPeticion peticion, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(peticion);
        var pedido = Pedido(HttpMethod.Post, RutaAlta);
        pedido.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Opciones.TokenValidado());
        pedido.Content = CuerpoJson(peticion);
        return EnviarJsonAsync<AltaEmpresaRespuesta>(pedido, ct);
    }

    /// <summary><c>GET /api/v1/fuentes</c>: departamentos y esquemas; también sirve para validar una api_key.</summary>
    public Task<ResultadoEnvio<FuentesRespuesta>> ObtenerFuentesAsync(string apiKey, CancellationToken ct) =>
        EnviarJsonAsync<FuentesRespuesta>(ConApiKey(Pedido(HttpMethod.Get, RutaFuentes), apiKey), ct);

    /// <summary><c>POST /api/v1/consultas</c>. Sin idempotencia en el API: quien llama registra el intento antes.</summary>
    public Task<ResultadoEnvio<ConsultaCreada>> CrearConsultaAsync(string apiKey, SolicitudConsulta solicitud, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(solicitud);
        ArgumentNullException.ThrowIfNull(solicitud.Detail);
        var prohibido = solicitud.Detail.Keys.FirstOrDefault(k => SolicitudConsulta.CamposProhibidos.Contains(k, StringComparer.OrdinalIgnoreCase));
        if (prohibido is not null)
            throw new ArgumentException($"El campo '{prohibido}' es del contrato anterior y el API lo rechaza.", nameof(solicitud));

        var pedido = ConApiKey(Pedido(HttpMethod.Post, RutaConsultas), apiKey);
        pedido.Content = CuerpoJson(solicitud);
        return EnviarJsonAsync<ConsultaCreada>(pedido, ct);
    }

    /// <summary><c>GET /api/v1/consultas/{id}</c>: estado y resultado (respaldo del webhook), con el cuerpo crudo.</summary>
    public async Task<ResultadoEnvio<ConsultaApi>> ObtenerConsultaAsync(string apiKey, string consultaId, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(consultaId);
        var pedido = ConApiKey(Pedido(HttpMethod.Get, $"{RutaConsultas}/{Uri.EscapeDataString(consultaId)}"), apiKey);
        return await EnviarAsync(pedido, async (respuesta, token) =>
        {
            var crudo = await respuesta.Content.ReadAsStringAsync(token).ConfigureAwait(false);
            var consulta = JsonSerializer.Deserialize<ConsultaApi>(crudo, Json);
            return consulta is null ? null : consulta with { CuerpoCrudo = crudo };
        }, ct).ConfigureAwait(false);
    }

    /// <summary><c>GET /api/v1/consultas?status=&amp;limit=&amp;cursor=</c> (correlación del «Incierto», DA-4).</summary>
    public Task<ResultadoEnvio<ListadoConsultas>> ListarConsultasAsync(string apiKey, string? status, int? limit, string? cursor, CancellationToken ct)
    {
        var parametros = new List<string>();
        if (!string.IsNullOrWhiteSpace(status))
            parametros.Add("status=" + Uri.EscapeDataString(status));
        if (limit is { } n)
            parametros.Add("limit=" + n.ToString(CultureInfo.InvariantCulture));
        if (!string.IsNullOrWhiteSpace(cursor))
            parametros.Add("cursor=" + Uri.EscapeDataString(cursor));
        var ruta = parametros.Count == 0 ? RutaConsultas : $"{RutaConsultas}?{string.Join('&', parametros)}";
        return EnviarJsonAsync<ListadoConsultas>(ConApiKey(Pedido(HttpMethod.Get, ruta), apiKey), ct);
    }

    /// <summary><c>GET /api/v1/files/{fileId}/download</c> con la api_key de la compañía dueña del archivo.</summary>
    public Task<ResultadoEnvio<ArchivoApi>> DescargarArchivoAsync(string apiKey, string fileId, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileId);
        var pedido = ConApiKey(Pedido(HttpMethod.Get, $"{RutaArchivos}/{Uri.EscapeDataString(fileId)}/download"), apiKey);
        return EnviarAsync(pedido, async (respuesta, token) => new ArchivoApi(
            await respuesta.Content.ReadAsByteArrayAsync(token).ConfigureAwait(false),
            respuesta.Content.Headers.ContentType?.MediaType,
            respuesta.Content.Headers.ContentDisposition?.FileNameStar ?? respuesta.Content.Headers.ContentDisposition?.FileName), ct);
    }

    private HttpRequestMessage Pedido(HttpMethod metodo, string ruta) => new(metodo, new Uri(Opciones.BaseValidada().AbsoluteUri.TrimEnd('/') + ruta));

    private static HttpRequestMessage ConApiKey(HttpRequestMessage pedido, string apiKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);
        // Con validación: una llave con saltos de línea o espacios no llega a ser una cabecera.
        pedido.Headers.Add(CabeceraApiKey, apiKey);
        return pedido;
    }

    private static StringContent CuerpoJson<T>(T cuerpo) =>
        new(JsonSerializer.Serialize(cuerpo, Json), Encoding.UTF8, "application/json");

    private Task<ResultadoEnvio<T>> EnviarJsonAsync<T>(HttpRequestMessage pedido, CancellationToken ct)
        where T : class =>
        EnviarAsync(pedido, async (respuesta, token) =>
        {
            await using var cuerpo = await respuesta.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
            return await JsonSerializer.DeserializeAsync<T>(cuerpo, Json, token).ConfigureAwait(false);
        }, ct);

    /// <summary>
    /// Envía y clasifica. Un 2xx cuyo cuerpo no se puede leer es incierto: la operación pudo hacerse en el API (un alta perdida
    /// termina en 409 al reintentar).
    /// </summary>
    private async Task<ResultadoEnvio<T>> EnviarAsync<T>(
        HttpRequestMessage pedido, Func<HttpResponseMessage, CancellationToken, Task<T?>> leer, CancellationToken ct)
        where T : class
    {
        using (pedido)
        {
            HttpResponseMessage respuesta;
            try
            {
                respuesta = await http.SendAsync(pedido, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ClasificadorEnvio.Clasificable(ex))
            {
                return ClasificadorEnvio.DeExcepcion<T>(ex, ct);
            }

            using (respuesta)
            {
                var status = (int)respuesta.StatusCode;
                try
                {
                    if (!respuesta.IsSuccessStatusCode)
                    {
                        var error = await respuesta.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                        return ClasificadorEnvio.DeEstado<T>(respuesta.StatusCode, error);
                    }

                    var valor = await leer(respuesta, ct).ConfigureAwait(false);
                    return valor is null
                        ? ResultadoEnvio<T>.Incierto("respuesta 2xx sin cuerpo", status)
                        : ResultadoEnvio<T>.Exito(status, valor);
                }
                catch (JsonException)
                {
                    return ResultadoEnvio<T>.Incierto("respuesta 2xx ilegible", status);
                }
                catch (Exception ex) when (ClasificadorEnvio.Clasificable(ex))
                {
                    var clasificado = ClasificadorEnvio.DeExcepcion<T>(ex, ct);
                    return ResultadoEnvio<T>.Incierto(clasificado.Motivo ?? "corte leyendo la respuesta", status);
                }
            }
        }
    }
}

/// <summary>Registro del cliente tipado con sus opciones (<see cref="OpcionesApiLiquidacion.Seccion"/>) y su timeout.</summary>
public static class ClienteApiLiquidacionExtensions
{
    public static IServiceCollection AddClienteApiLiquidacion(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        services.Configure<OpcionesApiLiquidacion>(configuration.GetSection(OpcionesApiLiquidacion.Seccion));
        services.AddHttpClient<ClienteApiLiquidacion>((sp, cliente) =>
            cliente.Timeout = sp.GetRequiredService<IOptions<OpcionesApiLiquidacion>>().Value.Timeout);
        return services;
    }
}
