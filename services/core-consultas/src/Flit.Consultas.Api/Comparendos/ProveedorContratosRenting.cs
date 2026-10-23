using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using Flit.Consultas.Grpc.V1;
using Microsoft.Extensions.Configuration;

namespace Flit.Consultas.Api.Comparendos;

/// <summary>
/// Credenciales de la API de contratos de Renting (HU #13712). Son las mismas variables del canal de correo de Renting
/// en core-notificaciones (mismo APIM, mismo certificado, mismo login), más la ruta de la consulta y el desvío del
/// contacto. Nada va quemado en el código: sin la configuración completa el proveedor queda no conectado.
/// <para>
/// Renting tiene un solo ambiente y es el de PRODUCCIÓN: la consulta devuelve personas reales. Por eso el contacto
/// (correo y teléfono) se desvía salvo que <c>RENTING_API_CONTRATOS_REAL_CONTACT_ENABLED</c> sea <c>true</c> (ADR-0044,
/// mismo criterio que <c>RENTING_API_SEND_EMAIL_REAL_RECIPIENTS_ENABLED</c>): sin él, los avisos al conductor de un
/// ambiente de pruebas nunca le llegan a un cliente de Renting.
/// </para>
/// </summary>
internal sealed record RentingContratosOpciones(
    Uri? BaseUrl, string NombreLlave, string ValorLlave, string RutaCertificado, string ClaveCertificado, string RutaLogin, string SecretoLogin,
    string SujetoLogin, string RutaContratos, int TimeoutSegundos, int TtlTokenSegundos, bool ContactoReal, string? CorreoDesvio)
{
    public static RentingContratosOpciones Desde(IConfiguration c)
    {
        string V(string clave) => c[clave]?.Trim() ?? string.Empty;
        int N(string clave, int defecto) => int.TryParse(c[clave], CultureInfo.InvariantCulture, out var n) && n > 0 ? n : defecto;
        return new(
            V("RENTING_API_BASE_URL") is { Length: > 0 } b ? new Uri(b) : null,
            V("RENTING_API_KEY_NAME"), V("RENTING_API_KEY_VALUE"), V("RENTING_API_PFX_CERTIFICATE_PATH"), V("RENTING_API_PASSPHRASE"),
            V("RENTING_API_LOGIN_PATH"), V("RENTING_API_LOGIN_SECRET_NAME"), V("RENTING_API_LOGIN_SUBJECT"), V("RENTING_API_CONTRATOS_PATH"),
            N("RENTING_API_SECONDS_TIMEOUT", 30), N("RENTING_API_LOGIN_CACHE_SECONDS_TTL", 600),
            string.Equals(V("RENTING_API_CONTRATOS_REAL_CONTACT_ENABLED"), "true", StringComparison.OrdinalIgnoreCase),
            V("RENTING_API_CONTRATOS_DEVELOPMENT_EMAIL") is { Length: > 0 } d ? d : null);
    }

    /// <summary>Variables que faltan para conectar (los nombres, nunca los valores).</summary>
    public IReadOnlyList<string> Faltan() => [.. new[]
    {
        BaseUrl is null ? "RENTING_API_BASE_URL" : null,
        NombreLlave.Length == 0 ? "RENTING_API_KEY_NAME" : null,
        ValorLlave.Length == 0 ? "RENTING_API_KEY_VALUE" : null,
        RutaCertificado.Length == 0 ? "RENTING_API_PFX_CERTIFICATE_PATH" : null,
        ClaveCertificado.Length == 0 ? "RENTING_API_PASSPHRASE" : null,
        RutaLogin.Length == 0 ? "RENTING_API_LOGIN_PATH" : null,
        SecretoLogin.Length == 0 ? "RENTING_API_LOGIN_SECRET_NAME" : null,
        SujetoLogin.Length == 0 ? "RENTING_API_LOGIN_SUBJECT" : null,
        RutaContratos.Length == 0 ? "RENTING_API_CONTRATOS_PATH" : null,
        !ContactoReal && CorreoDesvio is null ? "RENTING_API_CONTRATOS_DEVELOPMENT_EMAIL" : null,
    }.OfType<string>()];

    /// <summary>
    /// El certificado cliente (mTLS), o por qué no se pudo abrir. Sin mensajes técnicos crudos: nunca la clave.
    /// </summary>
    public (X509Certificate2? Certificado, string? Motivo) AbrirCertificado()
    {
        if (RutaCertificado.Length == 0 || ClaveCertificado.Length == 0)
            return (null, null);
        if (!File.Exists(RutaCertificado))
            return (null, $"No se encontró el certificado de Renting en RENTING_API_PFX_CERTIFICATE_PATH ('{RutaCertificado}').");
        try
        {
            return (X509CertificateLoader.LoadPkcs12FromFile(RutaCertificado, ClaveCertificado), null);
        }
        catch (CryptographicException)
        {
            return (null, "No se pudo abrir el certificado de Renting con RENTING_API_PASSPHRASE.");
        }
    }
}

/// <summary>
/// API de contratos de Renting (HU #13712): quién tenía el vehículo en una fecha. Contrato en ../GDC/api-renting:
/// login <c>POST {RENTING_API_LOGIN_PATH}</c> con <c>{ secretName, subject }</c> y luego
/// <c>GET {RENTING_API_CONTRATOS_PATH}?plate=&amp;offenseDate=</c> con Bearer; el certificado y la llave de suscripción
/// van en las dos llamadas. La respuesta es un objeto o un arreglo (se toma el primero); sin <c>names</c> ni
/// <c>customerEmail</c> no hay conductor.
/// <para>
/// Datos personales: no se registra nada en logs, la respuesta exitosa nunca sale como evidencia (no hay respuesta
/// cruda en el contrato gRPC) y los mensajes de error no llevan la llave ni el token.
/// </para>
/// </summary>
internal sealed class ProveedorContratosRenting(IHttpClientFactory http, RentingContratosOpciones opciones, string? motivoCertificado, TimeProvider tiempo) : IDisposable
{
    public const string HttpClientName = "contratos-renting";
    public const string ClaveProveedor = "renting";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly SemaphoreSlim _login = new(1, 1);
    private (string Valor, DateTimeOffset Vence)? _token;

    public void Dispose() => _login.Dispose();

    public string? NoConectado => opciones.Faltan() is { Count: > 0 } faltan
        ? $"Falta configurar la API de contratos de Renting: {string.Join(", ", faltan)}."
        : motivoCertificado;

    public async Task<ConsultarContratoConductorResponse> ConsultarAsync(string placa, DateOnly fecha, CancellationToken ct)
    {
        var reloj = Stopwatch.StartNew();
        try
        {
            var token = await TokenAsync(ct).ConfigureAwait(false);
            var respuesta = await PedirAsync(token, placa, fecha, ct).ConfigureAwait(false);
            if (respuesta.StatusCode == HttpStatusCode.Unauthorized)
            {
                // Token vencido antes del TTL: se pide uno nuevo una sola vez.
                respuesta.Dispose();
                Invalidar(token);
                respuesta = await PedirAsync(await TokenAsync(ct).ConfigureAwait(false), placa, fecha, ct).ConfigureAwait(false);
            }

            using (respuesta)
            {
                var codigo = (int)respuesta.StatusCode;
                var cuerpo = await respuesta.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                if (respuesta.StatusCode == HttpStatusCode.NotFound)
                    return Resultado(ResultadoContratoConductor.NoEncontrado, codigo, reloj);
                if (respuesta.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                    return Error(TipoErrorFuente.Autenticacion, $"Renting rechazó la autenticación ({codigo}).", codigo, reloj);
                if (!respuesta.IsSuccessStatusCode)
                    return Error(TipoErrorFuente.HttpNoExitoso, $"Renting respondió {codigo}: {Recortar(cuerpo)}", codigo, reloj);
                return Interpretar(cuerpo, codigo, reloj);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (TaskCanceledException)
        {
            return Error(TipoErrorFuente.Timeout, "Renting no respondió a tiempo.", null, reloj);
        }
        catch (HttpRequestException)
        {
            return Error(TipoErrorFuente.Red, "Error de red o de certificado con la API de Renting.", null, reloj);
        }
        catch (ErrorLoginRenting ex)
        {
            return Error(TipoErrorFuente.Autenticacion, ex.Message, ex.Codigo, reloj);
        }
    }

    /// <summary>
    /// HU #13843: prueba la conexión con un inicio de sesión nuevo (descarta el token guardado). No consulta ninguna placa:
    /// no toca datos personales.
    /// </summary>
    public async Task<ProbarContratoConductorResponse> ProbarAsync(CancellationToken ct)
    {
        var r = new ProbarContratoConductorResponse { Proveedor = ClaveProveedor, ContactoDesviado = !opciones.ContactoReal };
        if (!opciones.ContactoReal && opciones.CorreoDesvio is { } desvio)
            r.CorreoDesvio = desvio;
        if (NoConectado is { } motivo)
        {
            r.Motivo = motivo;
            return r;
        }

        r.Configurado = true;
        var reloj = Stopwatch.StartNew();
        try
        {
            _token = null;
            await TokenAsync(ct).ConfigureAwait(false);
            r.Conectado = true;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (TaskCanceledException)
        {
            r.Motivo = "Renting no respondió a tiempo.";
        }
        catch (HttpRequestException)
        {
            r.Motivo = "Error de red o de certificado con la API de Renting.";
        }
        catch (ErrorLoginRenting ex)
        {
            (r.Motivo, r.HttpStatus) = (ex.Message, ex.Codigo);
        }

        r.DuracionMs = (int)reloj.ElapsedMilliseconds;
        return r;
    }

    private Task<HttpResponseMessage> PedirAsync(string token, string placa, DateOnly fecha, CancellationToken ct)
    {
        // Mediodía UTC: la fecha del comparendo no cambia de día en ningún huso de Colombia.
        var instante = fecha.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + "T12:00:00.000Z";
        var ruta = $"{opciones.RutaContratos}?plate={Uri.EscapeDataString(placa.Trim().ToUpperInvariant())}&offenseDate={Uri.EscapeDataString(instante)}";
        var mensaje = Mensaje(HttpMethod.Get, ruta);
        mensaje.Headers.Authorization = new("Bearer", token);
        return http.CreateClient(HttpClientName).SendAsync(mensaje, ct);
    }

    private async Task<string> TokenAsync(CancellationToken ct)
    {
        if (_token is { } t && t.Vence > tiempo.GetUtcNow())
            return t.Valor;
        await _login.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_token is { } otra && otra.Vence > tiempo.GetUtcNow())
                return otra.Valor;
            using var pedido = Mensaje(HttpMethod.Post, opciones.RutaLogin);
            pedido.Content = JsonContent.Create(new { secretName = opciones.SecretoLogin, subject = opciones.SujetoLogin }, options: Json);
            using var respuesta = await http.CreateClient(HttpClientName).SendAsync(pedido, ct).ConfigureAwait(false);
            var codigo = (int)respuesta.StatusCode;
            if (!respuesta.IsSuccessStatusCode)
                throw new ErrorLoginRenting($"Renting rechazó el login ({codigo}).", codigo);
            var cuerpo = (await respuesta.Content.ReadAsStringAsync(ct).ConfigureAwait(false)).Trim();
            // El contrato devuelve { token, refresh }; la documentación de V1 admite también el token como texto plano.
            string? valor;
            try
            {
                using var doc = JsonDocument.Parse(cuerpo);
                valor = doc.RootElement.ValueKind switch
                {
                    JsonValueKind.String => doc.RootElement.GetString(),
                    JsonValueKind.Object => Texto(doc.RootElement, "token"),
                    _ => null,
                };
            }
            catch (JsonException)
            {
                valor = cuerpo;
            }

            if (string.IsNullOrWhiteSpace(valor))
                throw new ErrorLoginRenting("El login de Renting no devolvió token.", codigo);
            _token = (valor, tiempo.GetUtcNow().AddSeconds(opciones.TtlTokenSegundos));
            return valor;
        }
        finally
        {
            _login.Release();
        }
    }

    private void Invalidar(string rechazado)
    {
        if (_token is { } t && t.Valor == rechazado)
            _token = null;
    }

    /// <summary>La llave de suscripción del APIM va en todas las llamadas, también en el login.</summary>
    private HttpRequestMessage Mensaje(HttpMethod metodo, string ruta)
    {
        var m = new HttpRequestMessage(metodo, new Uri(opciones.BaseUrl!, ruta));
        m.Headers.TryAddWithoutValidation(opciones.NombreLlave, opciones.ValorLlave);
        return m;
    }

    private ConsultarContratoConductorResponse Interpretar(string cuerpo, int codigo, Stopwatch reloj)
    {
        if (string.IsNullOrWhiteSpace(cuerpo))
            return Resultado(ResultadoContratoConductor.NoEncontrado, codigo, reloj);
        JsonElement fila;
        try
        {
            using var doc = JsonDocument.Parse(cuerpo);
            var raiz = doc.RootElement;
            if (raiz.ValueKind == JsonValueKind.Null || raiz.ValueKind == JsonValueKind.Array && raiz.GetArrayLength() == 0)
                return Resultado(ResultadoContratoConductor.NoEncontrado, codigo, reloj);
            fila = (raiz.ValueKind == JsonValueKind.Array ? raiz[0] : raiz).Clone();
        }
        catch (JsonException)
        {
            return Error(TipoErrorFuente.RespuestaInvalida, "La respuesta de Renting no es JSON válido.", codigo, reloj);
        }

        if (fila.ValueKind != JsonValueKind.Object)
            return Error(TipoErrorFuente.RespuestaInvalida, "La respuesta de Renting no trae un contrato.", codigo, reloj);
        var nombres = string.Join(' ', new[] { Texto(fila, "names"), Texto(fila, "firstSurname"), Texto(fila, "secondSurname") }.OfType<string>());
        var correo = Texto(fila, "customerEmail");
        if (nombres.Length == 0 && correo is null)
            return Resultado(ResultadoContratoConductor.NoEncontrado, codigo, reloj);

        var conductor = new ConductorDeContrato();
        if (nombres.Length > 0) conductor.Nombres = nombres.ToUpperInvariant();
        if (Texto(fila, "documentType", "customerDocumentType", "identificationType") is { } tipo) conductor.TipoDocumento = tipo.ToUpperInvariant();
        if (Texto(fila, "documentNumber", "customerDocument", "customerDocumentNumber", "identification", "identificationNumber") is { } numero) conductor.NumeroDocumento = numero;
        if (Texto(fila, "address", "customerAddress") is { } dir) conductor.Direccion = dir;
        if (Texto(fila, "city", "customerCity") is { } ciudad) conductor.Ciudad = ciudad;
        if (Texto(fila, "branch", "branchName") is { } sucursal) conductor.Sucursal = sucursal;
        if (Texto(fila, "contractType") is { } contrato) conductor.TipoContrato = contrato;
        if (opciones.ContactoReal)
        {
            if (correo is not null) conductor.Correo = correo;
            if (Texto(fila, "customerPhone", "phoneNumber", "phone") is { } tel) conductor.Telefono = tel;
        }
        else if (correo is not null)
        {
            conductor.Correo = opciones.CorreoDesvio;
        }

        var r = Resultado(ResultadoContratoConductor.Encontrado, codigo, reloj);
        r.Conductor = conductor;
        r.ContactoDesviado = !opciones.ContactoReal;
        return r;
    }

    private static string? Texto(JsonElement fila, params string[] nombres)
    {
        foreach (var p in fila.EnumerateObject())
        {
            if (!nombres.Contains(p.Name, StringComparer.OrdinalIgnoreCase))
                continue;
            var v = p.Value.ValueKind switch
            {
                JsonValueKind.String => p.Value.GetString(),
                JsonValueKind.Number => p.Value.GetRawText(),
                _ => null,
            };
            if (!string.IsNullOrWhiteSpace(v))
                return v.Trim();
        }

        return null;
    }

    private string Recortar(string cuerpo)
    {
        var limpio = opciones.ValorLlave.Length == 0 ? cuerpo : cuerpo.Replace(opciones.ValorLlave, "***", StringComparison.Ordinal);
        return limpio.Length > 300 ? limpio[..300] : limpio;
    }

    private static ConsultarContratoConductorResponse Resultado(ResultadoContratoConductor resultado, int? codigo, Stopwatch reloj)
    {
        var r = new ConsultarContratoConductorResponse { Proveedor = ClaveProveedor, Resultado = resultado, DuracionMs = (int)reloj.ElapsedMilliseconds };
        if (codigo is { } c) r.HttpStatus = c;
        return r;
    }

    private static ConsultarContratoConductorResponse Error(TipoErrorFuente tipo, string mensaje, int? codigo, Stopwatch reloj)
    {
        var r = Resultado(ResultadoContratoConductor.Error, codigo, reloj);
        (r.TipoError, r.MensajeError) = (tipo, mensaje);
        return r;
    }

    private sealed class ErrorLoginRenting(string mensaje, int codigo) : Exception(mensaje)
    {
        public int Codigo { get; } = codigo;
    }
}
