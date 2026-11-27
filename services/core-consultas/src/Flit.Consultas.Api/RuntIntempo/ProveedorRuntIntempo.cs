using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using Flit.Consultas.Grpc.V1;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Flit.Consultas.Api.RuntIntempo;

/// <summary>
/// Configuración del cliente nativo de Intempo (HU #13875, F10-H3; §2 de la especificación de migración). Solo la URL
/// base es obligatoria y no tiene defecto (Q2=a: cada ambiente la define); los paths y el timeout traen el de la
/// especificación. Sin la URL base la fuente queda no conectada y el servicio arranca igual (sin fail-fast). HU #14752 (D5 = a):
/// el tope hacia Intempo (<see cref="LimitadorIntempo"/>), por defecto 4 concurrentes y 4 por segundo; un valor fuera de rango o
/// que no parsea toma el defecto, como el timeout.
/// </summary>
internal sealed record IntempoOpciones(
    Uri? BaseUrl, string RutaPlaca, string RutaVin, int TimeoutSegundos,
    int MaxConcurrentes = IntempoOpciones.MaxConcurrentesPorDefecto, int MaxPorSegundo = IntempoOpciones.MaxPorSegundoPorDefecto)
{
    public const string VariableBaseUrl = "INTEMPO_API_BASE_URL";
    public const string VariableRutaPlaca = "INTEMPO_API_VEHICLE_PLATE_PATH";
    public const string VariableRutaVin = "INTEMPO_API_VEHICLE_VIN_PATH";
    public const string VariableTimeout = "INTEMPO_API_SECONDS_TIMEOUT";
    public const string VariableMaxConcurrentes = "INTEMPO_API_MAX_CONCURRENT";
    public const string VariableMaxPorSegundo = "INTEMPO_API_MAX_REQUESTS_PER_SECOND";
    public const string RutaPlacaPorDefecto = "/api/RuntConsulta/consultar-vehiculo";
    public const string RutaVinPorDefecto = "/api/RuntConsulta/consultar-vin";
    public const int TimeoutPorDefecto = 20;
    public const int MaxConcurrentesPorDefecto = 4;
    public const int MaxPorSegundoPorDefecto = 4;

    public static IntempoOpciones Desde(IConfiguration c)
    {
        string V(string clave) => c[clave]?.Trim() ?? string.Empty;
        // Entero en [minimo, maximo] o el defecto; el valor nunca se registra.
        int Entero(string clave, int minimo, int maximo, int porDefecto) =>
            int.TryParse(V(clave), NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) && n >= minimo && n <= maximo ? n : porDefecto;
        var timeout = Entero(VariableTimeout, 1, int.MaxValue, TimeoutPorDefecto);
        // Una URL que no es absoluta http(s) cuenta como ausente: no conecta y su valor nunca se repite en un mensaje.
        var baseUrl = Uri.TryCreate(V(VariableBaseUrl), UriKind.Absolute, out var u) && (u.Scheme == Uri.UriSchemeHttps || u.Scheme == Uri.UriSchemeHttp) ? u : null;
        return new(
            baseUrl,
            V(VariableRutaPlaca) is { Length: > 0 } placa ? placa : RutaPlacaPorDefecto,
            V(VariableRutaVin) is { Length: > 0 } vin ? vin : RutaVinPorDefecto,
            timeout,
            Entero(VariableMaxConcurrentes, 1, 64, MaxConcurrentesPorDefecto),
            Entero(VariableMaxPorSegundo, 1, 100, MaxPorSegundoPorDefecto));
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
/// Datos personales (Q1=b): la respuesta trae nombres y direcciones. En logs solo el Warning 7441, con status, tipo de
/// contenido, tamaño y clasificación (nunca la URL con la placa, el cuerpo ni las excepciones), y los mensajes de error no
/// llevan <c>ex.Message</c>, que puede traer la URL. Solo el error no-2xx lleva el cuerpo, recortado a 300 caracteres.
/// </para>
/// <para>
/// HU #14752 (D5 = a): cada petición pasa por el <see cref="LimitadorIntempo"/>. Un 200 con el texto del límite de Intempo
/// («Excedio el límite de peticiones…») es <c>LIMITE_PROVEEDOR</c> (transitorio, lo reprograma diagnóstico con al menos 60 s);
/// otro 200 que no es JSON se reintenta UNA vez a los <see cref="EsperaReintentoNoJson"/>. Como mucho 2 peticiones por llamada.
/// </para>
/// </summary>
internal sealed partial class ProveedorRuntIntempo(
    IHttpClientFactory http, IntempoOpciones opciones, LimitadorIntempo limitador, TimeProvider reloj, ILogger<ProveedorRuntIntempo> log)
{
    public const string HttpClientName = "runt-intempo";
    public const string ClaveProveedor = "intempo";
    private const int MaximoTexto = 300;
    private const int MaximoIntentos = 2;

    /// <summary>Espera antes de repetir una consulta cuyo 200 no es JSON (HU #14752).</summary>
    internal static readonly TimeSpan EsperaReintentoNoJson = TimeSpan.FromSeconds(1);

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

        var cronometro = Stopwatch.StartNew();
        var operacion = parametro == "Plate" ? "placa" : "vin";
        try
        {
            for (var intento = 1; ; intento++)
            {
                // Concurrencia primero y token después, en cada intento (HU #14752).
                await using (await limitador.AdquirirAsync(ct).ConfigureAwait(false))
                {
                    using var pedido = new HttpRequestMessage(HttpMethod.Post, Url(ruta, parametro, valor))
                    {
                        Content = new StringContent(string.Empty, Encoding.UTF8, "application/json"),
                    };
                    using var respuesta = await http.CreateClient(HttpClientName).SendAsync(pedido, ct).ConfigureAwait(false);
                    var codigo = (int)respuesta.StatusCode;
                    var tipoContenido = respuesta.Content.Headers.ContentType?.MediaType is { Length: > 0 } media ? media : "-";
                    if (respuesta.StatusCode == HttpStatusCode.NotFound)
                        return Resultado(ResultadoRuntIntempo.NoEncontrado, codigo, cronometro);
                    if (respuesta.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                    {
                        LogRespuesta(log, codigo, tipoContenido, (int)(respuesta.Content.Headers.ContentLength ?? 0), operacion, "AUTENTICACION", intento);
                        return Error(TipoErrorFuente.Autenticacion, $"Intempo rechazó la autenticación ({codigo}).", codigo, cronometro);
                    }

                    var cuerpo = await respuesta.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                    var bytes = Encoding.UTF8.GetByteCount(cuerpo);
                    if (!respuesta.IsSuccessStatusCode)
                    {
                        LogRespuesta(log, codigo, tipoContenido, bytes, operacion, "HTTP_NO_EXITOSO", intento);
                        return Error(TipoErrorFuente.HttpNoExitoso, $"Intempo respondió {codigo}: {Recortar(cuerpo)}", codigo, cronometro);
                    }

                    if (EsLimite(cuerpo))
                    {
                        // Sin reintento aquí: lo programa diagnóstico, con un mínimo de 60 s.
                        LogRespuesta(log, codigo, tipoContenido, bytes, operacion, "LIMITE_PROVEEDOR", intento);
                        return Error(TipoErrorFuente.LimiteProveedor, "Intempo limitó las peticiones de la consulta (HTTP 200).", codigo, cronometro);
                    }

                    using var documento = Parsear(cuerpo);
                    if (documento is not null)
                        return Interpretar(documento.RootElement, codigo, cronometro);

                    LogRespuesta(log, codigo, tipoContenido, bytes, operacion, "NO_JSON", intento);
                    if (intento >= MaximoIntentos)
                        return Error(TipoErrorFuente.RespuestaInvalida, "La respuesta de Intempo no es JSON válido.", codigo, cronometro);
                }

                await Task.Delay(EsperaReintentoNoJson, reloj, ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (TaskCanceledException)
        {
            return Error(TipoErrorFuente.Timeout, "Intempo no respondió a tiempo.", null, cronometro);
        }
        catch (HttpRequestException)
        {
            return Error(TipoErrorFuente.Red, "Error de red con la API de Intempo.", null, cronometro);
        }
    }

    /// <summary>
    /// Verdadero si el cuerpo es el aviso de límite de Intempo: contiene «limite de peticiones» sin tildes ni mayúsculas. No mira
    /// el <c>Content-Type</c>, que Intempo puede cambiar.
    /// </summary>
    internal static bool EsLimite(string cuerpo) =>
        !string.IsNullOrEmpty(cuerpo) && SinTildes(cuerpo).ToLowerInvariant().Contains("limite de peticiones", StringComparison.Ordinal);

    /// <summary>El documento si el cuerpo es JSON (empieza por <c>{</c> o <c>[</c> y parsea); si no, null.</summary>
    private static JsonDocument? Parsear(string cuerpo)
    {
        var texto = cuerpo.TrimStart();
        if (texto.Length == 0 || (texto[0] != '{' && texto[0] != '['))
            return null;
        try
        {
            return JsonDocument.Parse(cuerpo);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Quita las tildes con una tabla propia (el servicio corre con globalización invariante, sin <c>Normalize</c> fiable), como
    /// <c>MapperIntempo.SinTildes</c> de core-diagnostico.
    /// </summary>
    private static string SinTildes(string texto) => string.Create(texto.Length, texto, static (destino, origen) =>
    {
        for (var i = 0; i < origen.Length; i++)
        {
            destino[i] = origen[i] switch
            {
                'á' or 'à' or 'ä' => 'a',
                'é' or 'è' or 'ë' => 'e',
                'í' or 'ì' or 'ï' => 'i',
                'ó' or 'ò' or 'ö' => 'o',
                'ú' or 'ù' or 'ü' => 'u',
                'Á' or 'À' or 'Ä' => 'A',
                'É' or 'È' or 'Ë' => 'E',
                'Í' or 'Ì' or 'Ï' => 'I',
                'Ó' or 'Ò' or 'Ö' => 'O',
                'Ú' or 'Ù' or 'Ü' => 'U',
                var c => c,
            };
        }
    });

    /// <summary>
    /// HU #14752: lo único que se registra de una respuesta de Intempo. Sin cuerpo, placa, VIN, URL ni <c>ex.Message</c>: el
    /// resumen sin datos personales es la clasificación más el tamaño.
    /// </summary>
    [LoggerMessage(EventId = 7441, Level = LogLevel.Warning,
        Message = "Intempo respondió {Status} ({TipoContenido}, {Bytes} B) en la consulta por {Operacion}: {Clasificacion}, intento {Intento}")]
    private static partial void LogRespuesta(ILogger logger, int status, string tipoContenido, int bytes, string operacion, string clasificacion, int intento);

    /// <summary>
    /// Concatena base, ruta y query sin resolver la ruta contra la base: <c>new Uri(base, "/api/…")</c> descartaría el
    /// tramo de la base (p. ej. <c>/pdn/Base</c>).
    /// </summary>
    private Uri Url(string ruta, string parametro, string valor)
    {
        var baseUrl = opciones.BaseUrl!.GetLeftPart(UriPartial.Path).TrimEnd('/');
        return new Uri($"{baseUrl}/{ruta.TrimStart('/')}?{parametro}={Uri.EscapeDataString(valor)}");
    }

    private static ConsultarVehiculoIntempoResponse Interpretar(JsonElement raiz, int codigo, Stopwatch reloj)
    {
        if (raiz.ValueKind == JsonValueKind.Array)
            return ErroresDeIntempo(raiz, codigo, reloj);
        if (raiz.ValueKind != JsonValueKind.Object || !(Presente(raiz, "noPlaca") || Presente(raiz, "noVin")))
            return Error(TipoErrorFuente.RespuestaInvalida, "La respuesta de Intempo no trae un vehículo.", codigo, reloj);

        var r = Resultado(ResultadoRuntIntempo.Encontrado, codigo, reloj);
        r.Vehiculo = InterpreteIntempo.Interpretar(raiz);
        return r;
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
