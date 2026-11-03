using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;

namespace Flit.Diagnostico.Api.Modulos.Dictamen;

/// <summary>
/// Redactor del dictamen (épica #12678, sin HU nueva). Mismo patrón que el OCR de Comparendos, con la llave propia del
/// producto en <c>Diagnostico:Anthropic</c>. Al modelo solo llegan el veredicto, las reglas y los datos del bien.
/// </summary>
public enum TipoFalloRedactor
{
    Timeout,
    Http5xx,
    RateLimit,
    Red,
    NoConfigurado,
    Ilegible,
}

/// <summary>Datos del bien que sí pueden viajar al modelo. No hay documentos ni nombres.</summary>
public sealed record DatosBien(string Placa, string? Clase, string? Marca, string? Linea, string? Modelo, string? Servicio);

/// <summary>Regla ya disparada por el motor. El monto va en pesos enteros; null si la fuente no lo trae.</summary>
public sealed record ReglaDisparada(string Codigo, string Fuente, string Efecto, long? MontoPesos);

/// <summary>
/// Pedido de redacción. <see cref="RedaccionSuspendida"/> es el interruptor de la HU #13741 y no se serializa al modelo.
/// </summary>
public sealed record PedidoRedaccion(
    string Veredicto,
    IReadOnlyList<ReglaDisparada> Reglas,
    DatosBien Bien,
    bool RedaccionSuspendida);

/// <summary>Lo que devolvió el proveedor. Un <see cref="TipoFallo"/> técnico no se reintenta dentro del redactor.</summary>
public sealed record SalidaModelo(
    string? Texto,
    string Modelo,
    TipoFalloRedactor? TipoFallo = null,
    string? Mensaje = null,
    TimeSpan? ReintentarEn = null);

/// <summary>
/// Resultado listo para persistir: <c>PLANTILLA</c>/<c>PENDIENTE</c> o <c>LLM</c>/<c>COMPLETADA</c>.
/// </summary>
public sealed record ResultadoRedaccion(
    string Texto,
    string RedactadoPor,
    string EstadoRedaccion,
    int Intentos,
    string Modelo,
    TipoFalloRedactor? TipoFallo,
    TimeSpan? ReintentarEn = null);

/// <summary>Proveedor de texto. No decide el veredicto.</summary>
public interface IProveedorRedactor
{
    /// <summary>False cuando no hay llave ni modo simulado: el servicio responde con la plantilla y no llama.</summary>
    bool Configurado { get; }

    string Modelo { get; }

    Task<SalidaModelo> RedactarAsync(string prompt, CancellationToken ct);
}

/// <summary>Opciones propias del producto. La llave no se comparte con Comparendos ni con core-api.</summary>
public sealed class OpcionesAnthropic
{
    public const string Seccion = "Diagnostico:Anthropic";

    public string BaseUrl { get; set; } = "https://api.anthropic.com";

    public string ApiKey { get; set; } = string.Empty;

    public string Modelo { get; set; } = "claude-haiku-4-5";

    public int TimeoutSegundos { get; set; } = 30;

    public int MaxTokens { get; set; } = 2000;

    /// <summary>Reintentos extra cuando el texto contradice el veredicto. 1 significa dos llamadas como máximo.</summary>
    public int ReintentosContradiccion { get; set; } = 1;

    /// <summary>En LOCAL o DEV, sin llave, usa el proveedor simulado. En QA y PDN no tiene efecto.</summary>
    public bool Simulado { get; set; }
}

/// <summary>Arma el prompt y detecta si el texto trae el veredicto contrario.</summary>
public static class PromptRedactor
{
    private const string Instruccion =
        "Redacta en español los alertamientos. El veredicto del JSON ya está decidido: explícalo y no lo cambies. No inventes personas ni identificadores. Responde solo el texto.";

    private static readonly JsonSerializerOptions Json = new();

    public static string Construir(PedidoRedaccion pedido)
    {
        ArgumentNullException.ThrowIfNull(pedido);
        if (pedido.Veredicto is not ("VIABLE" or "BLOQUEADO"))
            throw new ArgumentOutOfRangeException(nameof(pedido), pedido.Veredicto, "El veredicto solo puede ser VIABLE o BLOQUEADO.");
        ArgumentNullException.ThrowIfNull(pedido.Reglas);
        ArgumentNullException.ThrowIfNull(pedido.Bien);

        var json = JsonSerializer.Serialize(new
        {
            veredicto = pedido.Veredicto,
            reglas = pedido.Reglas.Select(r => new
            {
                codigo = r.Codigo,
                fuente = r.Fuente,
                efecto = r.Efecto,
                monto_pesos = r.MontoPesos,
            }),
            bien = new
            {
                placa = pedido.Bien.Placa,
                clase = pedido.Bien.Clase,
                marca = pedido.Bien.Marca,
                linea = pedido.Bien.Linea,
                modelo = pedido.Bien.Modelo,
                servicio = pedido.Bien.Servicio,
            },
        }, Json);
        return Instruccion + "\n" + json;
    }

    /// <summary>True si el texto está vacío o nombra el veredicto contrario. El texto se descarta.</summary>
    public static bool Contradice(string veredicto, string? texto)
    {
        if (string.IsNullOrWhiteSpace(texto))
            return true;
        var opuesto = veredicto == "VIABLE" ? "BLOQUEADO" : "VIABLE";
        return Regex.IsMatch(texto, $@"\b{opuesto}\b", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
    }
}

/// <summary>Texto fijo cuando el modelo no puede redactar. Lleva la marca que ve el cliente.</summary>
public static class PlantillaRedaccion
{
    public static string Texto(PedidoRedaccion pedido)
    {
        ArgumentNullException.ThrowIfNull(pedido);
        var reglas = string.Join("; ", pedido.Reglas.Select(r =>
            r.MontoPesos is { } monto ? $"{r.Codigo} ({r.Efecto}, {monto} pesos)" : $"{r.Codigo} ({r.Efecto})"));
        return $"Redacción IA pendiente. Veredicto {pedido.Veredicto}. Placa {pedido.Bien.Placa}. {reglas}";
    }
}

/// <summary>
/// Pide el texto, descarta el que contradice el veredicto y, si el modelo no responde, entrega la plantilla.
/// Un fallo técnico no se reintenta aquí: la cola durable del dictamen lo hará cuando exista la fila.
/// </summary>
public sealed class ServicioRedactor(IProveedorRedactor proveedor, IOptions<OpcionesAnthropic> opciones)
{
    public async Task<ResultadoRedaccion> RedactarAsync(PedidoRedaccion pedido, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(pedido);
        if (pedido.RedaccionSuspendida || !proveedor.Configurado)
        {
            return Plantilla(pedido, 0, proveedor.Modelo, pedido.RedaccionSuspendida ? null : TipoFalloRedactor.NoConfigurado, null);
        }

        var prompt = PromptRedactor.Construir(pedido);
        var tope = Math.Max(0, opciones.Value.ReintentosContradiccion);
        var intentos = 0;
        SalidaModelo? ultima = null;
        for (var i = 0; i <= tope; i++)
        {
            intentos++;
            ultima = await proveedor.RedactarAsync(prompt, ct).ConfigureAwait(false);
            if (ultima.TipoFallo is not null)
                return Plantilla(pedido, intentos, ultima.Modelo, ultima.TipoFallo, ultima.ReintentarEn);
            if (!PromptRedactor.Contradice(pedido.Veredicto, ultima.Texto))
                return new ResultadoRedaccion(ultima.Texto!, "LLM", "COMPLETADA", intentos, ultima.Modelo, null);
        }

        return Plantilla(pedido, intentos, ultima?.Modelo ?? proveedor.Modelo, null, null);
    }

    private static ResultadoRedaccion Plantilla(PedidoRedaccion pedido, int intentos, string modelo, TipoFalloRedactor? fallo, TimeSpan? reintentarEn) =>
        new(PlantillaRedaccion.Texto(pedido), "PLANTILLA", "PENDIENTE", intentos, modelo, fallo, reintentarEn);
}

/// <summary>QA y PDN sin llave: no se inventa una redacción del modelo.</summary>
public sealed class ProveedorRedactorNoConfigurado : IProveedorRedactor
{
    public bool Configurado => false;

    public string Modelo => "no-configurado";

    public Task<SalidaModelo> RedactarAsync(string prompt, CancellationToken ct) =>
        Task.FromResult(new SalidaModelo(null, Modelo, TipoFalloRedactor.NoConfigurado, "El redactor no está configurado en este ambiente."));
}

/// <summary>
/// LOCAL y DEV sin llave. El último carácter de la placa elige el escenario: F fallo de red, T tiempo agotado,
/// X texto con el veredicto contrario, cualquier otro un texto alineado.
/// </summary>
public sealed class ProveedorRedactorSimulado : IProveedorRedactor
{
    public bool Configurado => true;

    public string Modelo => "simulado";

    public Task<SalidaModelo> RedactarAsync(string prompt, CancellationToken ct)
    {
        var inicio = prompt.IndexOf('{');
        if (inicio < 0)
            return Task.FromResult(Fallo(TipoFalloRedactor.Ilegible, "El pedido simulado no trae JSON."));
        try
        {
            using var doc = JsonDocument.Parse(prompt[inicio..]);
            var raiz = doc.RootElement;
            var veredicto = raiz.GetProperty("veredicto").GetString() ?? string.Empty;
            var placa = raiz.GetProperty("bien").GetProperty("placa").GetString() ?? string.Empty;
            if (placa.Length == 0 || veredicto is not ("VIABLE" or "BLOQUEADO"))
                return Task.FromResult(Fallo(TipoFalloRedactor.Ilegible, "El pedido simulado no trae placa o veredicto."));

            var opuesto = veredicto == "VIABLE" ? "BLOQUEADO" : "VIABLE";
            return char.ToUpperInvariant(placa[^1]) switch
            {
                'F' => Task.FromResult(Fallo(TipoFalloRedactor.Red, "Fallo de red simulado.")),
                'T' => Task.FromResult(Fallo(TipoFalloRedactor.Timeout, "Tiempo agotado simulado.")),
                'X' => Task.FromResult(new SalidaModelo($"El dictamen del bien es {opuesto}.", Modelo)),
                _ => Task.FromResult(new SalidaModelo($"El dictamen del bien es {veredicto}.", Modelo)),
            };
        }
        catch (JsonException)
        {
            return Task.FromResult(Fallo(TipoFalloRedactor.Ilegible, "El pedido simulado no es JSON."));
        }
        catch (KeyNotFoundException)
        {
            return Task.FromResult(Fallo(TipoFalloRedactor.Ilegible, "El pedido simulado no trae placa o veredicto."));
        }
    }

    private SalidaModelo Fallo(TipoFalloRedactor tipo, string mensaje) => new(null, Modelo, tipo, mensaje);
}

/// <summary>
/// Anthropic Messages API, sin documentos ni imágenes. La llave solo viaja en el encabezado y no se copia a los mensajes
/// de error. Una respuesta cortada o un rechazo quedan ilegibles; 429, 5xx, red y tiempo agotado quedan clasificados.
/// </summary>
public sealed class ProveedorRedactorAnthropic(HttpClient http, IOptions<OpcionesAnthropic> opciones) : IProveedorRedactor
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public bool Configurado => true;

    public string Modelo => opciones.Value.Modelo;

    public async Task<SalidaModelo> RedactarAsync(string prompt, CancellationToken ct)
    {
        var o = opciones.Value;
        var cuerpo = new
        {
            model = o.Modelo,
            max_tokens = o.MaxTokens,
            messages = new object[]
            {
                new
                {
                    role = "user",
                    content = new object[] { new { type = "text", text = prompt } },
                },
            },
        };

        using var limite = CancellationTokenSource.CreateLinkedTokenSource(ct);
        limite.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, o.TimeoutSegundos)));
        try
        {
            var baseUrl = o.BaseUrl.EndsWith('/') ? o.BaseUrl : o.BaseUrl + "/";
            using var pedido = new HttpRequestMessage(HttpMethod.Post, new Uri(new Uri(baseUrl), "v1/messages"))
            {
                Content = JsonContent.Create(cuerpo, options: Json),
            };
            pedido.Headers.TryAddWithoutValidation("x-api-key", o.ApiKey);
            pedido.Headers.TryAddWithoutValidation("anthropic-version", "2023-06-01");
            using var respuesta = await http.SendAsync(pedido, limite.Token).ConfigureAwait(false);
            if (respuesta.StatusCode == HttpStatusCode.TooManyRequests)
                return Fallo(o.Modelo, TipoFalloRedactor.RateLimit, "El proveedor limitó las peticiones (429).", RetryAfter(respuesta.Headers.RetryAfter));
            if ((int)respuesta.StatusCode >= 500)
                return Fallo(o.Modelo, TipoFalloRedactor.Http5xx, $"El proveedor respondió {(int)respuesta.StatusCode}.");
            if (!respuesta.IsSuccessStatusCode)
                return Fallo(o.Modelo, TipoFalloRedactor.Ilegible, $"El proveedor rechazó la solicitud ({(int)respuesta.StatusCode}).");

            var texto = await respuesta.Content.ReadAsStringAsync(limite.Token).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(texto);
            switch (doc.RootElement.TryGetProperty("stop_reason", out var parada) ? parada.GetString() : null)
            {
                case "max_tokens":
                    return Fallo(o.Modelo, TipoFalloRedactor.Ilegible, "La respuesta del modelo quedó cortada (se acabó el límite de tokens).");
                case "refusal":
                    return Fallo(o.Modelo, TipoFalloRedactor.Ilegible, "El modelo rechazó la redacción.");
            }

            var salida = string.Concat(doc.RootElement.GetProperty("content").EnumerateArray()
                .Where(b => b.TryGetProperty("type", out var tipo) && tipo.GetString() == "text")
                .Select(b => b.GetProperty("text").GetString()));
            return string.IsNullOrWhiteSpace(salida)
                ? Fallo(o.Modelo, TipoFalloRedactor.Ilegible, "La respuesta del modelo no trae texto.")
                : new SalidaModelo(salida, o.Modelo);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return Fallo(o.Modelo, TipoFalloRedactor.Timeout, $"El proveedor no respondió en {o.TimeoutSegundos} s.");
        }
        catch (HttpRequestException ex)
        {
            return Fallo(o.Modelo, TipoFalloRedactor.Red, $"Error de red: {ex.Message}");
        }
        catch (JsonException)
        {
            return Fallo(o.Modelo, TipoFalloRedactor.Http5xx, "Respuesta del proveedor no interpretable.");
        }
    }

    private static SalidaModelo Fallo(string modelo, TipoFalloRedactor tipo, string mensaje, TimeSpan? reintentarEn = null) =>
        new(null, modelo, tipo, mensaje, reintentarEn);

    private static TimeSpan? RetryAfter(RetryConditionHeaderValue? encabezado) =>
        encabezado?.Delta ?? (encabezado?.Date is { } fecha ? fecha - DateTimeOffset.UtcNow : null);
}
