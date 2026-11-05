using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using Flit.Consultas.Grpc.V1;
using Microsoft.Extensions.Configuration;

namespace Flit.Consultas.Api.RuntIntempo;

/// <summary>
/// Configuración del cliente nativo de Intempo (HU #13875, F10-H3; §2 de la especificación de migración). Solo la URL
/// base es obligatoria y no tiene defecto (Q2=a: cada ambiente la define); los paths y el timeout traen el de la
/// especificación. Sin la URL base la fuente queda no conectada y el servicio arranca igual (sin fail-fast).
/// </summary>
internal sealed record IntempoOpciones(Uri? BaseUrl, string RutaPlaca, string RutaVin, int TimeoutSegundos)
{
    public const string VariableBaseUrl = "INTEMPO_API_BASE_URL";
    public const string VariableRutaPlaca = "INTEMPO_API_VEHICLE_PLATE_PATH";
    public const string VariableRutaVin = "INTEMPO_API_VEHICLE_VIN_PATH";
    public const string VariableTimeout = "INTEMPO_API_SECONDS_TIMEOUT";
    public const string RutaPlacaPorDefecto = "/api/RuntConsulta/consultar-vehiculo";
    public const string RutaVinPorDefecto = "/api/RuntConsulta/consultar-vin";
    public const int TimeoutPorDefecto = 20;

    public static IntempoOpciones Desde(IConfiguration c)
    {
        string V(string clave) => c[clave]?.Trim() ?? string.Empty;
        var timeout = int.TryParse(V(VariableTimeout), NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) && n > 0 ? n : TimeoutPorDefecto;
        // Una URL que no es absoluta http(s) cuenta como ausente: no conecta y su valor nunca se repite en un mensaje.
        var baseUrl = Uri.TryCreate(V(VariableBaseUrl), UriKind.Absolute, out var u) && (u.Scheme == Uri.UriSchemeHttps || u.Scheme == Uri.UriSchemeHttp) ? u : null;
        return new(
            baseUrl,
            V(VariableRutaPlaca) is { Length: > 0 } placa ? placa : RutaPlacaPorDefecto,
            V(VariableRutaVin) is { Length: > 0 } vin ? vin : RutaVinPorDefecto,
            timeout);
    }

    /// <summary>Variables que faltan para conectar (los nombres, nunca los valores).</summary>
    public IReadOnlyList<string> Faltan() => BaseUrl is null ? [VariableBaseUrl] : [];
}

/// <summary>
/// Cliente HTTP nativo del RUNT por Intempo (HU #13875, F10-H3), calcado de <c>Comparendos/ProveedorContratosRenting</c>.
/// Dos operaciones (G2=b), espejo de §3.1 y §3.2: <c>POST {base}{ruta}?Plate=</c> y <c>?Vin=</c> (V mayúscula), body
/// vacío con <c>Content-Type: application/json</c>, sin autenticación. El valor va escapado pero sin recortar ni cambiar
/// mayúsculas (el proxy heredado no normaliza). El timeout lo pone el <c>HttpClient</c> registrado con
/// <see cref="HttpClientName"/> (F10-H4), no el body.
/// <para>
/// Datos personales (Q1=b): la respuesta trae nombres y direcciones. No se registra nada en logs (ni la URL con la
/// placa, ni el cuerpo, ni las excepciones) y los mensajes de error no llevan <c>ex.Message</c>, que puede traer la URL.
/// Solo el error no-2xx lleva el cuerpo, recortado a 300 caracteres.
/// </para>
/// </summary>
internal sealed class ProveedorRuntIntempo(IHttpClientFactory http, IntempoOpciones opciones)
{
    public const string HttpClientName = "runt-intempo";
    public const string ClaveProveedor = "intempo";
    private const int MaximoTexto = 300;

    /// <summary>§5.6: el código del error cuando no hubo status HTTP.</summary>
    private const int CodigoSinStatus = 404;

    /// <summary>Por qué la fuente no está conectada (los nombres de las variables, nunca sus valores), o <c>null</c>.</summary>
    public string? NoConectado => opciones.Faltan() is { Count: > 0 } faltan
        ? $"Falta configurar Intempo: {string.Join(", ", faltan)}."
        : null;

    public Task<ConsultarVehiculoIntempoResponse> ConsultarPorPlacaAsync(string placa, CancellationToken ct) =>
        ConsultarAsync(opciones.RutaPlaca, "Plate", placa, ct);

    public Task<ConsultarVehiculoIntempoResponse> ConsultarPorVinAsync(string vin, CancellationToken ct) =>
        ConsultarAsync(opciones.RutaVin, "Vin", vin, ct);

    private async Task<ConsultarVehiculoIntempoResponse> ConsultarAsync(string ruta, string parametro, string valor, CancellationToken ct)
    {
        if (NoConectado is { } motivo)
            throw new InvalidOperationException(motivo);
        ArgumentNullException.ThrowIfNull(valor);

        var reloj = Stopwatch.StartNew();
        try
        {
            using var pedido = new HttpRequestMessage(HttpMethod.Post, Url(ruta, parametro, valor))
            {
                Content = new StringContent(string.Empty, Encoding.UTF8, "application/json"),
            };
            using var respuesta = await http.CreateClient(HttpClientName).SendAsync(pedido, ct).ConfigureAwait(false);
            var codigo = (int)respuesta.StatusCode;
            if (respuesta.StatusCode == HttpStatusCode.NotFound)
                return Resultado(ResultadoRuntIntempo.NoEncontrado, codigo, reloj);
            if (respuesta.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                return Error(TipoErrorFuente.Autenticacion, $"Intempo rechazó la autenticación ({codigo}).", codigo, reloj);
            var cuerpo = await respuesta.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            if (!respuesta.IsSuccessStatusCode)
                return Error(TipoErrorFuente.HttpNoExitoso, $"Intempo respondió {codigo}: {Recortar(cuerpo)}", codigo, reloj);
            return Interpretar(cuerpo, codigo, reloj);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (TaskCanceledException)
        {
            return Error(TipoErrorFuente.Timeout, "Intempo no respondió a tiempo.", null, reloj);
        }
        catch (HttpRequestException)
        {
            return Error(TipoErrorFuente.Red, "Error de red con la API de Intempo.", null, reloj);
        }
    }

    /// <summary>
    /// Concatena base, ruta y query sin resolver la ruta contra la base: <c>new Uri(base, "/api/…")</c> descartaría el
    /// tramo de la base (p. ej. <c>/pdn/Base</c>).
    /// </summary>
    private Uri Url(string ruta, string parametro, string valor)
    {
        var baseUrl = opciones.BaseUrl!.GetLeftPart(UriPartial.Path).TrimEnd('/');
        return new Uri($"{baseUrl}/{ruta.TrimStart('/')}?{parametro}={Uri.EscapeDataString(valor)}");
    }

    private static ConsultarVehiculoIntempoResponse Interpretar(string cuerpo, int codigo, Stopwatch reloj)
    {
        try
        {
            using var doc = JsonDocument.Parse(cuerpo);
            var raiz = doc.RootElement;
            if (raiz.ValueKind == JsonValueKind.Array)
                return ErroresDeIntempo(raiz, codigo, reloj);
            if (raiz.ValueKind != JsonValueKind.Object || !(Presente(raiz, "noPlaca") || Presente(raiz, "noVin")))
                return Error(TipoErrorFuente.RespuestaInvalida, "La respuesta de Intempo no trae un vehículo.", codigo, reloj);

            var r = Resultado(ResultadoRuntIntempo.Encontrado, codigo, reloj);
            r.Vehiculo = InterpreteIntempo.Interpretar(raiz);
            return r;
        }
        catch (JsonException)
        {
            return Error(TipoErrorFuente.RespuestaInvalida, "La respuesta de Intempo no es JSON válido.", codigo, reloj);
        }
    }

    /// <summary>§5.6: la raíz <c>[{code, description}]</c> es un error de la fuente; los errores se copian tal cual.</summary>
    private static ConsultarVehiculoIntempoResponse ErroresDeIntempo(JsonElement raiz, int codigo, Stopwatch reloj)
    {
        var errores = new List<ErrorIntempo>();
        foreach (var e in raiz.EnumerateArray())
        {
            if (e.ValueKind != JsonValueKind.Object)
                return Error(TipoErrorFuente.RespuestaInvalida, "La respuesta de Intempo no trae un vehículo.", codigo, reloj);
            var code = e.TryGetProperty("code", out var c) && c.ValueKind == JsonValueKind.Number && c.TryGetInt32(out var n) ? n : codigo;
            var description = e.TryGetProperty("description", out var d) ? d.ValueKind switch
            {
                JsonValueKind.String => d.GetString() ?? string.Empty,
                JsonValueKind.Null or JsonValueKind.Undefined => string.Empty,
                _ => d.GetRawText(),
            } : string.Empty;
            errores.Add(new ErrorIntempo { Code = code, Description = Recortar(description) });
        }

        if (errores.Count == 0)
            return Error(TipoErrorFuente.RespuestaInvalida, "La respuesta de Intempo no trae un vehículo.", codigo, reloj);
        var r = Resultado(ResultadoRuntIntempo.Error, codigo, reloj);
        (r.TipoError, r.MensajeError) = (TipoErrorFuente.EstadoNoOk, $"Intempo devolvió el error {errores[0].Code}: {errores[0].Description}");
        r.Errores.AddRange(errores);
        return r;
    }

    private static bool Presente(JsonElement raiz, string clave) =>
        raiz.TryGetProperty(clave, out var v) && v.ValueKind switch
        {
            JsonValueKind.String => !string.IsNullOrWhiteSpace(v.GetString()),
            JsonValueKind.Number => true,
            _ => false,
        };

    private static string Recortar(string texto) => texto.Length > MaximoTexto ? texto[..MaximoTexto] : texto;

    private static ConsultarVehiculoIntempoResponse Resultado(ResultadoRuntIntempo resultado, int? codigo, Stopwatch reloj)
    {
        var r = new ConsultarVehiculoIntempoResponse { Proveedor = ClaveProveedor, Resultado = resultado, DuracionMs = (int)reloj.ElapsedMilliseconds };
        if (codigo is { } c) r.HttpStatus = c;
        return r;
    }

    /// <summary>Todo error lleva <c>errores=[{code: status o 404, description: mensaje técnico}]</c> (§5.6).</summary>
    private static ConsultarVehiculoIntempoResponse Error(TipoErrorFuente tipo, string mensaje, int? codigo, Stopwatch reloj)
    {
        var r = Resultado(ResultadoRuntIntempo.Error, codigo, reloj);
        (r.TipoError, r.MensajeError) = (tipo, mensaje);
        r.Errores.Add(new ErrorIntempo { Code = codigo ?? CodigoSinStatus, Description = mensaje });
        return r;
    }
}
