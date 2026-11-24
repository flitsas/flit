using System.Net;
using System.Text;
using System.Text.Json;
using Flit.Tramites.Application.UseCases.Consultations;

namespace Flit.Consultas.Api.Impuesto;

/// <summary>
/// Transporte HTTP asíncrono. Cabecera <c>X-API-Key</c> por petición. No escribe URL, cuerpo ni mensajes del proveedor.
/// </summary>
internal sealed class TransporteImpuestoHttp(IHttpClientFactory http, OpcionesImpuesto opciones, ILogger<TransporteImpuestoHttp> logger)
    : ITransporteImpuesto
{
    public const string HttpClientName = "impuesto";

    /// <summary>
    /// Código sintético para el 422 <c>department_unsupported</c> cuyo <c>message</c> empieza por «falta» (falta
    /// <c>detail.secretaryDepartmentCode</c>): es un fallo de datos, no de cobertura. El <c>message</c> solo se usa para distinguir
    /// los dos casos; nunca sale del método ni se registra (Ley 1581).
    /// </summary>
    public const string CodigoDepartamentoAusente = "department_code_missing";

    private const string CodigoDepartamentoNoSoportado = "department_unsupported";
    private const string CabeceraApiKey = "X-API-Key";

    public bool Listo => opciones.TieneRuta && opciones.TieneIdentidad;

    public string NombreProveedor => ProveedorImpuesto.ClaveProveedor;

    public async Task<EnvioImpuesto> EnviarAsync(string cuerpo, string apiKey, CancellationToken ct)
    {
        if (!Listo)
            throw new InvalidOperationException("El transporte de impuesto no está configurado.");
        try
        {
            using var pedido = new HttpRequestMessage(HttpMethod.Post, OpcionesImpuesto.Unir(opciones.BaseUrl!.AbsoluteUri, opciones.Ruta))
            {
                Content = new StringContent(cuerpo, Encoding.UTF8, "application/json"),
            };
            pedido.Headers.TryAddWithoutValidation(CabeceraApiKey, apiKey);
            using var respuesta = await http.CreateClient(HttpClientName).SendAsync(pedido, ct).ConfigureAwait(false);
            var codigo = (int)respuesta.StatusCode;
            if (!respuesta.IsSuccessStatusCode)
            {
                var codigoError = await CodigoErrorAsync(respuesta, ct).ConfigureAwait(false);
                if (codigo == 422)
                    TransporteImpuestoLog.DepartamentoRechazado(logger);
                return new EnvioImpuesto(null, Fallo(respuesta.StatusCode, codigo), codigoError);
            }

            var texto = await respuesta.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            var id = LeerId(texto);
            return id is null
                ? new EnvioImpuesto(null, new FalloProveedor(ClaseFalloProveedor.Caida, codigo), null)
                : new EnvioImpuesto(id, null, null);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            TransporteImpuestoLog.Tiempo(logger);
            return new EnvioImpuesto(null, new FalloProveedor(ClaseFalloProveedor.Timeout), null);
        }
        catch (HttpRequestException)
        {
            TransporteImpuestoLog.Red(logger);
            return new EnvioImpuesto(null, new FalloProveedor(ClaseFalloProveedor.Red), null);
        }
    }

    public async Task<EstadoImpuesto> ConsultarAsync(string idExterno, string apiKey, CancellationToken ct)
    {
        if (!Listo)
            throw new InvalidOperationException("El transporte de impuesto no está configurado.");
        try
        {
            var url = OpcionesImpuesto.Unir(opciones.BaseUrl!.AbsoluteUri, opciones.Ruta)
                + "/" + Uri.EscapeDataString(idExterno);
            using var pedido = new HttpRequestMessage(HttpMethod.Get, url);
            pedido.Headers.TryAddWithoutValidation(CabeceraApiKey, apiKey);
            using var respuesta = await http.CreateClient(HttpClientName).SendAsync(pedido, ct).ConfigureAwait(false);
            var codigo = (int)respuesta.StatusCode;
            if (respuesta.StatusCode == HttpStatusCode.NotFound)
                return new EstadoImpuesto(EstadoTrabajoExterno.NoEncontrado, null, new FalloProveedor(ClaseFalloProveedor.Caida, 404), "not_found");
            if (!respuesta.IsSuccessStatusCode)
            {
                TransporteImpuestoLog.Sondeo(logger);
                return new EstadoImpuesto(EstadoTrabajoExterno.Desconocido, null, Fallo(respuesta.StatusCode, codigo), await CodigoErrorAsync(respuesta, ct).ConfigureAwait(false));
            }

            var texto = await respuesta.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            return InterpretarEstado(texto);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            TransporteImpuestoLog.Tiempo(logger);
            return new EstadoImpuesto(EstadoTrabajoExterno.Desconocido, null, new FalloProveedor(ClaseFalloProveedor.Timeout), null);
        }
        catch (HttpRequestException)
        {
            TransporteImpuestoLog.Red(logger);
            return new EstadoImpuesto(EstadoTrabajoExterno.Desconocido, null, new FalloProveedor(ClaseFalloProveedor.Red), null);
        }
    }

    public async Task<ArchivoImpuesto> DescargarAsync(string archivoId, string apiKey, CancellationToken ct)
    {
        if (!Listo)
            throw new InvalidOperationException("El transporte de impuesto no está configurado.");
        try
        {
            var url = OpcionesImpuesto.Unir(opciones.BaseUrl!.AbsoluteUri, $"files/{Uri.EscapeDataString(archivoId)}/download");
            using var pedido = new HttpRequestMessage(HttpMethod.Get, url);
            pedido.Headers.TryAddWithoutValidation(CabeceraApiKey, apiKey);
            using var respuesta = await http.CreateClient(HttpClientName).SendAsync(pedido, ct).ConfigureAwait(false);
            var codigo = (int)respuesta.StatusCode;
            if (respuesta.StatusCode == HttpStatusCode.NotFound)
                return new ArchivoImpuesto(null, null, new FalloProveedor(ClaseFalloProveedor.Caida, 404), true);
            if (!respuesta.IsSuccessStatusCode)
            {
                TransporteImpuestoLog.Descarga(logger);
                return new ArchivoImpuesto(null, null, Fallo(respuesta.StatusCode, codigo), false);
            }

            var bytes = await respuesta.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
            var tipo = respuesta.Content.Headers.ContentType?.MediaType;
            return new ArchivoImpuesto(bytes, tipo, null, false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            TransporteImpuestoLog.Tiempo(logger);
            return new ArchivoImpuesto(null, null, new FalloProveedor(ClaseFalloProveedor.Timeout), false);
        }
        catch (HttpRequestException)
        {
            TransporteImpuestoLog.Red(logger);
            return new ArchivoImpuesto(null, null, new FalloProveedor(ClaseFalloProveedor.Red), false);
        }
    }

    private static EstadoImpuesto InterpretarEstado(string texto)
    {
        try
        {
            using var doc = JsonDocument.Parse(texto);
            var status = doc.RootElement.TryGetProperty("status", out var s) ? s.GetString() ?? "" : "";
            return status.ToLowerInvariant() switch
            {
                "queued" => new EstadoImpuesto(EstadoTrabajoExterno.EnCola, null, null, null),
                "running" => new EstadoImpuesto(EstadoTrabajoExterno.Ejecutando, null, null, null),
                "succeeded" => new EstadoImpuesto(EstadoTrabajoExterno.Exitoso, texto, null, null),
                "partial" => new EstadoImpuesto(EstadoTrabajoExterno.Parcial, texto, null, null),
                "failed" => new EstadoImpuesto(EstadoTrabajoExterno.Fallido, texto, null, null),
                _ => new EstadoImpuesto(EstadoTrabajoExterno.Desconocido, texto, null, null),
            };
        }
        catch (JsonException)
        {
            return new EstadoImpuesto(EstadoTrabajoExterno.Desconocido, null, new FalloProveedor(ClaseFalloProveedor.Caida, 200), null);
        }
    }

    private static string? LeerId(string texto)
    {
        try
        {
            using var doc = JsonDocument.Parse(texto);
            return doc.RootElement.TryGetProperty("id", out var id) ? id.GetString() : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static async Task<string?> CodigoErrorAsync(HttpResponseMessage respuesta, CancellationToken ct)
    {
        try
        {
            var texto = await respuesta.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(texto);
            if (doc.RootElement.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.Object
                && error.TryGetProperty("code", out var code))
            {
                var codigo = code.GetString();
                if (string.Equals(codigo, CodigoDepartamentoNoSoportado, StringComparison.Ordinal)
                    && error.TryGetProperty("message", out var mensaje) && mensaje.ValueKind == JsonValueKind.String
                    && (mensaje.GetString() ?? "").StartsWith("falta", StringComparison.OrdinalIgnoreCase))
                    return CodigoDepartamentoAusente;
                return codigo;
            }
            if (doc.RootElement.TryGetProperty("code", out var codeRaiz))
                return codeRaiz.GetString();
        }
        catch (JsonException)
        {
        }
        catch (InvalidOperationException)
        {
        }

        return null;
    }

    private static FalloProveedor Fallo(HttpStatusCode estado, int codigo) => estado switch
    {
        HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => new FalloProveedor(ClaseFalloProveedor.Credenciales, codigo),
        HttpStatusCode.TooManyRequests => new FalloProveedor(ClaseFalloProveedor.Limite, codigo),
        // 409 (p. ej. source_inactive) es una respuesta de negocio de la fuente, no una caída del proveedor.
        HttpStatusCode.BadRequest or HttpStatusCode.Conflict or HttpStatusCode.UnprocessableEntity => new FalloProveedor(ClaseFalloProveedor.Rechazo, codigo),
        _ => new FalloProveedor(ClaseFalloProveedor.Caida, codigo),
    };
}

internal static partial class TransporteImpuestoLog
{
    [LoggerMessage(EventId = 7431, Level = LogLevel.Warning, Message = "Transporte de impuesto agotó el tiempo.")]
    public static partial void Tiempo(ILogger logger);

    [LoggerMessage(EventId = 7432, Level = LogLevel.Warning, Message = "Transporte de impuesto no alcanzó la red.")]
    public static partial void Red(ILogger logger);

    [LoggerMessage(EventId = 7433, Level = LogLevel.Warning, Message = "Sondeo de impuesto falló.")]
    public static partial void Sondeo(ILogger logger);

    [LoggerMessage(EventId = 7434, Level = LogLevel.Warning, Message = "Departamento rechazado por la fuente de impuesto.")]
    public static partial void DepartamentoRechazado(ILogger logger);

    [LoggerMessage(EventId = 7435, Level = LogLevel.Warning, Message = "Descarga de liquidación de impuesto falló.")]
    public static partial void Descarga(ILogger logger);
}
