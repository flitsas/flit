using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Flit.Comparendos.Domain.Modelo;
using Microsoft.Extensions.Options;

namespace Flit.Comparendos.Api.Modulos.Ocr;

/// <summary>Campos que se le piden al LLM (§8.5). Los obligatorios deciden COMPLETO o INCOMPLETO (RN-053).</summary>
public static class CamposOcr
{
    public const string Numero = "numero_comparendo";
    public const string TipoDocumento = "tipo_documento_infractor";
    public const string NumeroDocumento = "numero_documento_infractor";
    public const string Nombre = "nombre_infractor";
    public const string CodigoInfraccion = "codigo_infraccion";
    public const string FechaInfraccion = "fecha_infraccion";
    public const string Placa = "placa";
    public const string Secretaria = "secretaria";
    public const string Valor = "valor";
    public const string DescripcionInfraccion = "descripcion_infraccion";
    public const string FechaNotificacion = "fecha_notificacion";
    public const string HoraInfraccion = "hora_infraccion";
    public const string CodigoOrganismo = "codigo_organismo";
    public const string EsFotomulta = "es_fotomulta";
    public const string Direccion = "direccion";
    public const string TipoVehiculo = "tipo_vehiculo";

    public static readonly string[] Obligatorios = [Numero, TipoDocumento, NumeroDocumento, Nombre, CodigoInfraccion, FechaInfraccion, Placa, Secretaria, Valor];

    public static readonly string[] Todos =
        [.. Obligatorios, DescripcionInfraccion, FechaNotificacion, HoraInfraccion, CodigoOrganismo, EsFotomulta, Direccion, TipoVehiculo];
}

public sealed record CampoExtraido(string? Valor, decimal? Confianza);

/// <summary>
/// Lo que devolvió el proveedor. Fallo técnico (timeout, 5xx, 429, red) → se reintenta; <c>TipoFallo</c> Ilegible o
/// ArchivoInvalido → no se reintenta (RN-054).
/// </summary>
public sealed record ResultadoOcr(
    IReadOnlyDictionary<string, CampoExtraido> Campos,
    string? Crudo,
    string Modelo,
    TipoFalloOcr? TipoFallo = null,
    string? Mensaje = null,
    TimeSpan? ReintentarEn = null)
{
    public bool Exitoso => TipoFallo is null;

    public static bool EsTecnico(TipoFalloOcr? t) => t is TipoFalloOcr.Timeout or TipoFalloOcr.Http5xx or TipoFalloOcr.RateLimit or TipoFalloOcr.Red;

    public static ResultadoOcr Fallo(string modelo, TipoFalloOcr tipo, string mensaje, TimeSpan? reintentar = null, string? crudo = null) =>
        new(new Dictionary<string, CampoExtraido>(), crudo, modelo, tipo, mensaje, reintentar);
}

/// <summary>
/// Extracción multimodal de un comparendo (RN-050, §8.5) detrás de una abstracción: se arranca con Anthropic (el que ya
/// usa Trámites) y se podrá cambiar por OpenAI o Gemini sin tocar el flujo. No reintenta: los reintentos con backoff
/// 5 s · 15 s · 45 s los programa el procesador por documento (RN-054).
/// </summary>
public interface IProveedorOcr
{
    Task<ResultadoOcr> ExtraerAsync(byte[] contenido, string contentType, string prompt, string modelo, CancellationToken ct);
}

/// <summary>Interpreta la salida del LLM: un objeto JSON (puede venir rodeado de texto o de ```json) con cada campo como valor o como {valor, confianza}.</summary>
public static class RespuestaOcr
{
    public static Dictionary<string, CampoExtraido>? Interpretar(string? texto)
    {
        if (string.IsNullOrWhiteSpace(texto))
            return null;
        var inicio = texto.IndexOf('{', StringComparison.Ordinal);
        var fin = texto.LastIndexOf('}');
        if (inicio < 0 || fin <= inicio)
            return null;
        try
        {
            using var doc = JsonDocument.Parse(texto[inicio..(fin + 1)]);
            var raiz = doc.RootElement.TryGetProperty("campos", out var c) && c.ValueKind == JsonValueKind.Object ? c : doc.RootElement;
            var campos = new Dictionary<string, CampoExtraido>(StringComparer.Ordinal);
            foreach (var p in raiz.EnumerateObject())
            {
                var nombre = p.Name.Trim().ToLowerInvariant();
                if (!CamposOcr.Todos.Contains(nombre))
                    continue;
                campos[nombre] = p.Value.ValueKind == JsonValueKind.Object
                    ? new CampoExtraido(Texto(p.Value.TryGetProperty("valor", out var v) ? v : default), p.Value.TryGetProperty("confianza", out var conf) ? Confianza(conf) : null)
                    : new CampoExtraido(Texto(p.Value), null);
            }

            return campos;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? Texto(JsonElement e) => e.ValueKind switch
    {
        JsonValueKind.String => string.IsNullOrWhiteSpace(e.GetString()) ? null : e.GetString()!.Trim(),
        JsonValueKind.Number => e.GetRawText(),
        JsonValueKind.True => "true",
        JsonValueKind.False => "false",
        _ => null,
    };

    private static decimal? Confianza(JsonElement e) =>
        e.ValueKind == JsonValueKind.Number && e.TryGetDecimal(out var d) ? Math.Clamp(Math.Round(d, 2), 0m, 1m) : null;
}

public sealed class OpcionesAnthropic
{
    public const string Seccion = "Comparendos:Ocr:Anthropic";

    public string BaseUrl { get; set; } = "https://api.anthropic.com";
    public string ApiKey { get; set; } = string.Empty;
    public int TimeoutSegundos { get; set; } = 90;
    public int MaxTokens { get; set; } = 2000;
}

/// <summary>
/// Anthropic Messages API, sin streaming (la salida es un JSON corto). La llave solo viaja en el encabezado y nunca se
/// registra. Clasifica el fallo para que el procesador decida si reintenta (respeta <c>Retry-After</c> en un 429).
/// </summary>
public sealed class ProveedorOcrAnthropic(HttpClient http, IOptions<OpcionesAnthropic> opciones) : IProveedorOcr
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<ResultadoOcr> ExtraerAsync(byte[] contenido, string contentType, string prompt, string modelo, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(contenido);
        var o = opciones.Value;
        var bloque = contentType == "application/pdf" ? "document" : "image";
        var cuerpo = new
        {
            model = modelo,
            max_tokens = o.MaxTokens,
            messages = new object[]
            {
                new
                {
                    role = "user",
                    content = new object[]
                    {
                        new { type = bloque, source = new { type = "base64", media_type = contentType, data = Convert.ToBase64String(contenido) } },
                        new { type = "text", text = prompt },
                    },
                },
            },
        };

        using var limite = CancellationTokenSource.CreateLinkedTokenSource(ct);
        limite.CancelAfter(TimeSpan.FromSeconds(o.TimeoutSegundos));
        try
        {
            using var pedido = new HttpRequestMessage(HttpMethod.Post, new Uri(new Uri(o.BaseUrl), "/v1/messages")) { Content = JsonContent.Create(cuerpo, options: Json) };
            pedido.Headers.TryAddWithoutValidation("x-api-key", o.ApiKey);
            pedido.Headers.TryAddWithoutValidation("anthropic-version", "2023-06-01");
            using var respuesta = await http.SendAsync(pedido, limite.Token).ConfigureAwait(false);
            var texto = await respuesta.Content.ReadAsStringAsync(limite.Token).ConfigureAwait(false);
            if (respuesta.StatusCode == HttpStatusCode.TooManyRequests)
                return ResultadoOcr.Fallo(modelo, TipoFalloOcr.RateLimit, "El proveedor limitó las peticiones (429).", RetryAfter(respuesta.Headers.RetryAfter));
            if ((int)respuesta.StatusCode >= 500)
                return ResultadoOcr.Fallo(modelo, TipoFalloOcr.Http5xx, $"El proveedor respondió {(int)respuesta.StatusCode}.");
            if (!respuesta.IsSuccessStatusCode)
                return ResultadoOcr.Fallo(modelo, TipoFalloOcr.ArchivoInvalido, $"El proveedor rechazó el archivo ({(int)respuesta.StatusCode}).", crudo: Recortar(texto));

            using var doc = JsonDocument.Parse(texto);
            var salida = string.Concat(doc.RootElement.GetProperty("content").EnumerateArray()
                .Where(b => b.GetProperty("type").GetString() == "text").Select(b => b.GetProperty("text").GetString()));
            var campos = RespuestaOcr.Interpretar(salida);
            return campos is null
                ? ResultadoOcr.Fallo(modelo, TipoFalloOcr.Ilegible, "La respuesta del modelo no es un JSON de campos.", crudo: Recortar(salida))
                : new ResultadoOcr(campos, salida, modelo);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return ResultadoOcr.Fallo(modelo, TipoFalloOcr.Timeout, $"El proveedor no respondió en {o.TimeoutSegundos} s.");
        }
        catch (HttpRequestException ex)
        {
            return ResultadoOcr.Fallo(modelo, TipoFalloOcr.Red, $"Error de red: {ex.Message}");
        }
        catch (JsonException)
        {
            return ResultadoOcr.Fallo(modelo, TipoFalloOcr.Http5xx, "Respuesta del proveedor no interpretable.");
        }
    }

    private static TimeSpan? RetryAfter(RetryConditionHeaderValue? h) =>
        h?.Delta ?? (h?.Date is { } fecha ? fecha - DateTimeOffset.UtcNow : null);

    private static string Recortar(string? s) => s is { Length: > 4000 } ? s[..4000] : s ?? string.Empty;
}

/// <summary>
/// Modo simulado (local, DEV sin llave): extrae un comparendo plausible y determinista a partir de la huella del archivo,
/// para poder recorrer el flujo completo sin gastar llamadas. Un archivo cuyo nombre empieza por «ilegible» sale ILEGIBLE.
/// </summary>
public sealed class ProveedorOcrSimulado : IProveedorOcr
{
    public Task<ResultadoOcr> ExtraerAsync(byte[] contenido, string contentType, string prompt, string modelo, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(contenido);
        var h = System.Security.Cryptography.SHA256.HashData(contenido);
        var n = BitConverter.ToUInt32(h, 0) % 100000000;
        var placa = $"{(char)('A' + (h[4] % 26))}{(char)('A' + (h[5] % 26))}{(char)('A' + (h[6] % 26))}{h[7] % 1000:000}";
        var campos = new Dictionary<string, CampoExtraido>
        {
            [CamposOcr.Numero] = new($"110010000000{n:00000000}", 0.97m),
            [CamposOcr.TipoDocumento] = new("NIT", 0.9m),
            [CamposOcr.NumeroDocumento] = new("900123456", 0.92m),
            [CamposOcr.Nombre] = new("EMPRESA SIMULADA S.A.S.", 0.88m),
            [CamposOcr.CodigoInfraccion] = new("C29", 0.95m),
            [CamposOcr.FechaInfraccion] = new("2026-09-28", 0.93m),
            [CamposOcr.Placa] = new(placa, 0.96m),
            [CamposOcr.Secretaria] = new("SECRETARIA DISTRITAL DE MOVILIDAD DE BOGOTA", 0.9m),
            [CamposOcr.Valor] = new(h[8] % 3 == 0 ? null : "604100", h[8] % 3 == 0 ? null : 0.9m),
            [CamposOcr.EsFotomulta] = new("false", 0.8m),
        };
        var crudo = JsonSerializer.Serialize(campos.ToDictionary(kv => kv.Key, kv => new { valor = kv.Value.Valor, confianza = kv.Value.Confianza }));
        return Task.FromResult(new ResultadoOcr(campos, crudo, $"simulado:{modelo}"));
    }
}

/// <summary>Conversión de los valores de texto extraídos al modelo del comparendo.</summary>
public static class ValoresOcr
{
    public static DateOnly? Fecha(string? s) =>
        DateOnly.TryParseExact(s?.Trim(), ["yyyy-MM-dd", "dd/MM/yyyy", "d/M/yyyy", "dd-MM-yyyy"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;

    public static TimeOnly? Hora(string? s) =>
        TimeOnly.TryParse(s?.Trim(), CultureInfo.InvariantCulture, out var t) ? t : null;

    public static TipoDocumento? Documento(string? s) =>
        Enum.TryParse<TipoDocumento>(s?.Trim().Replace(".", string.Empty, StringComparison.Ordinal), true, out var t) && Enum.IsDefined(t) ? t : null;

    public static bool? Booleano(string? s) => s?.Trim().ToUpperInvariant() switch
    {
        "TRUE" or "SI" or "SÍ" or "1" => true,
        "FALSE" or "NO" or "0" => false,
        _ => null,
    };
}
