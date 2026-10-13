using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using Flit.Consultas.Grpc.V1;
using Microsoft.Extensions.Configuration;

namespace Flit.Consultas.Api.Comparendos;

/// <summary>
/// Dirección de la API municipal: <c>MUNICIPAL_INFRACCIONES_BASE_URL</c>, <c>MUNICIPAL_INFRACCIONES_PATH</c> y
/// <c>MUNICIPAL_INFRACCIONES_TIMEOUT_SECONDS</c> (en V1, <c>MUNICIPALITY_INFRACTION_API_BASE_URL</c>,
/// <c>MUNICIPALITY_GET_INFRACTION_PATH</c> y su timeout). Sin dirección o sin ruta, el proveedor queda no conectado:
/// ninguna de las dos tiene valor por defecto.
/// </summary>
internal sealed record MunicipalOpciones(Uri? BaseUrl, string? Ruta, int TimeoutSegundos)
{
    public static MunicipalOpciones Desde(IConfiguration c) => new(
        c["MUNICIPAL_INFRACCIONES_BASE_URL"] is { Length: > 0 } u ? new Uri(u) : null,
        c["MUNICIPAL_INFRACCIONES_PATH"] is { Length: > 0 } r ? r : null,
        int.TryParse(c["MUNICIPAL_INFRACCIONES_TIMEOUT_SECONDS"], CultureInfo.InvariantCulture, out var t) && t > 0 ? t : 60);
}

/// <summary>
/// La API municipal (HU #13467): organismos que no reportan al SIMIT, con una respuesta por municipio. Contrato
/// tomado de V1 (BackCrudSmartCloud, <c>municipalityInfractionApiProxy.ts</c> y <c>municipalityInfractionService.ts</c>):
/// <c>GET {base}{ruta}?{tipo en minúscula}={número}</c>, sin autenticación, y la respuesta
/// <c>{ base: [{ documento, fuente, estadoRespuesta }], detalle: [{ documentoConsulta, fuenteConsulta, comparendo, … }] }</c>.
/// Cada entrada de <c>base</c> es un municipio: «OK» con o sin filas es exitosa; cualquier otro estado es error de
/// ese municipio y no toca los demás. Solo por identificación: V1 nunca consultó por placa.
/// </summary>
internal sealed class ProveedorMunicipal(IHttpClientFactory http, MunicipalOpciones opciones) : IProveedorComparendos
{
    public const string HttpClientName = "comparendos-municipal";

    public string Clave => "municipal";

    public string Nombre => "API municipal";

    public IReadOnlyList<CanalComparendos> Canales => [CanalComparendos.MunicipalIdentificacion];

    public string? NoConectado => (opciones.BaseUrl, opciones.Ruta) switch
    {
        (null, null) => "Falta configurar la API municipal: MUNICIPAL_INFRACCIONES_BASE_URL, MUNICIPAL_INFRACCIONES_PATH.",
        (null, _) => "Falta configurar la API municipal: MUNICIPAL_INFRACCIONES_BASE_URL.",
        (_, null) => "Falta configurar la API municipal: MUNICIPAL_INFRACCIONES_PATH.",
        _ => null,
    };

    public async Task<ConsultarComparendosResponse> ConsultarAsync(ConsultarComparendosRequest pedido, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(pedido);
        var reloj = Stopwatch.StartNew();
        var tipo = pedido.TipoDocumento.Trim().ToLowerInvariant();
        if (tipo.Length == 0 || !tipo.All(char.IsAsciiLetterLower))
            return Fuente.Error(Clave, TipoErrorFuente.Excepcion, $"Tipo de documento no válido para la API municipal: «{pedido.TipoDocumento}».", null, 0);
        var url = new Uri(opciones.BaseUrl!, $"{opciones.Ruta}?{tipo}={Uri.EscapeDataString(pedido.NumeroDocumento.Trim())}");
        try
        {
            using var respuesta = await http.CreateClient(HttpClientName).GetAsync(url, ct).ConfigureAwait(false);
            var cuerpo = await respuesta.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            var codigo = (int)respuesta.StatusCode;
            if (respuesta.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                return Fuente.Error(Clave, TipoErrorFuente.Autenticacion, $"La API municipal rechazó la autenticación ({codigo}).", codigo, reloj.ElapsedMilliseconds, cuerpo);
            if (!respuesta.IsSuccessStatusCode)
                return Fuente.Error(Clave, TipoErrorFuente.HttpNoExitoso, $"La API municipal respondió {codigo}.", codigo, reloj.ElapsedMilliseconds, cuerpo);

            if (JsonNode.Parse(cuerpo) is not JsonObject raiz || Propiedad(raiz, "base") is not JsonArray bases)
                return Fuente.Error(Clave, TipoErrorFuente.RespuestaInvalida, "La respuesta de la API municipal no trae la lista «base».", codigo, reloj.ElapsedMilliseconds, cuerpo);
            var detalle = (Propiedad(raiz, "detalle") as JsonArray)?.OfType<JsonObject>().ToList() ?? [];
            var pedidos = pedido.Municipios.Count == 0 ? null : pedido.Municipios.ToHashSet(StringComparer.OrdinalIgnoreCase);

            var r = new ConsultarComparendosResponse
            {
                Proveedor = Clave, HttpStatus = codigo, DuracionMs = (int)reloj.ElapsedMilliseconds, RespuestaCruda = cuerpo, ContentType = "application/json",
            };
            foreach (var b in bases.OfType<JsonObject>())
            {
                if (Texto(b, "fuente") is not { } fuente || (pedidos is not null && !pedidos.Contains(fuente)))
                    continue;
                var documento = Texto(b, "documento");
                var estado = Texto(b, "estadoRespuesta");
                if (!string.Equals(estado, "OK", StringComparison.OrdinalIgnoreCase))
                {
                    r.Parciales.Add(new ResultadoParcialFuente
                    {
                        Municipio = fuente, Resultado = ResultadoFuente.Error, TipoError = TipoErrorFuente.EstadoNoOk,
                        MensajeError = $"El municipio respondió «{estado ?? "sin estado"}».",
                    });
                    continue;
                }

                var filas = detalle.Where(d => string.Equals(Texto(d, "fuenteConsulta"), fuente, StringComparison.OrdinalIgnoreCase)
                    && (documento is null || Texto(d, "documentoConsulta") == documento)).ToList();
                var parcial = new ResultadoParcialFuente { Municipio = fuente };
                parcial.Comparendos.AddRange(filas.Select(f => Mapear(f, fuente)).OfType<ComparendoDeFuente>());
                if (parcial.Comparendos.Count == 0 && filas.Count > 0)
                {
                    parcial.Resultado = ResultadoFuente.Error;
                    parcial.TipoError = TipoErrorFuente.RespuestaInvalida;
                    parcial.MensajeError = "El municipio devolvió filas sin número de comparendo.";
                }
                else
                {
                    parcial.Resultado = parcial.Comparendos.Count == 0 ? ResultadoFuente.ExitosaVacia : ResultadoFuente.ExitosaConDatos;
                }

                r.Parciales.Add(parcial);
            }

            if (r.Parciales.Count == 0)
                return Fuente.Error(Clave, TipoErrorFuente.RespuestaInvalida, "La API municipal no respondió por ningún municipio.", codigo, reloj.ElapsedMilliseconds, cuerpo);
            return r;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (TaskCanceledException)
        {
            return Fuente.Error(Clave, TipoErrorFuente.Timeout, "La API municipal no respondió a tiempo.", null, reloj.ElapsedMilliseconds);
        }
        catch (HttpRequestException ex)
        {
            return Fuente.Error(Clave, TipoErrorFuente.Red, $"Error de red con la API municipal: {ex.Message}", null, reloj.ElapsedMilliseconds);
        }
        catch (JsonException)
        {
            return Fuente.Error(Clave, TipoErrorFuente.RespuestaInvalida, "La respuesta de la API municipal no es JSON válido.", null, reloj.ElapsedMilliseconds);
        }
    }

    private static JsonNode? Propiedad(JsonObject o, string nombre) =>
        o.FirstOrDefault(p => string.Equals(p.Key, nombre, StringComparison.OrdinalIgnoreCase)).Value;

    /// <summary>Texto de una propiedad, sea cadena o número. Null si falta o está vacía.</summary>
    private static string? Texto(JsonObject o, string nombre) => Propiedad(o, nombre) switch
    {
        JsonValue v when v.TryGetValue<string>(out var s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim(),
        JsonValue v when v.TryGetValue<decimal>(out var d) => d.ToString(CultureInfo.InvariantCulture),
        JsonValue v when v.TryGetValue<bool>(out var b) => b ? "true" : "false",
        _ => null,
    };

    private static ComparendoDeFuente? Mapear(JsonObject f, string fuente)
    {
        if (Texto(f, "comparendo") is not { } numero)
            return null;
        var c = new ComparendoDeFuente { Numero = numero, Secretaria = fuente.ToUpperInvariant() };
        if (Texto(f, "placa") is { } placa) c.Placa = placa.ToUpperInvariant();
        if (Texto(f, "documentoConsulta") is { } doc) c.NumeroDocumentoInfractor = doc;
        if (Texto(f, "infraccion") is { } inf) c.CodigoInfraccion = inf;
        if (Texto(f, "descInfraccion") is { } desc) c.DescripcionInfraccion = desc;
        if (Texto(f, "direccionComparendo") is { } dir) c.DireccionInfraccion = dir;
        if (Fuente.Fecha(Texto(f, "fechaComparendo")) is { } fecha) c.FechaImposicion = fecha;
        if (Fuente.Hora(Texto(f, "fechaComparendo")) is { } hora) c.HoraImposicion = hora;
        if (Fuente.Fecha(Texto(f, "fechaNotificacion")) is { } notif) c.FechaNotificacion = notif;
        if (Fuente.SiNo(Texto(f, "fotodeteccion")) is { } foto) c.EsFotomulta = foto;
        if (Texto(f, "tipoVehiculo") is { } tv) c.TipoVehiculo = tv;
        if (Texto(f, "servicioVehiculo") is { } sv) c.ServicioVehiculo = sv;
        if (Fuente.PesosDeTexto(Texto(f, "valor")) is { } valor) c.ValorTotal = valor;
        if (Texto(f, "estado") is { } estado) c.EstadoFuente = estado;
        return c;
    }
}
