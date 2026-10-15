using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Flit.Consultas.Grpc.V1;
using Flit.Infrastructure.Consultations;
using Flit.Tramites.Application.UseCases.Consultations;
using Microsoft.Extensions.Options;

namespace Flit.Consultas.Api.Comparendos;

/// <summary>
/// Un proveedor de comparendos (HU #13520). Devuelve el registro completo y nunca convierte un error en «sin
/// comparendos»: solo una respuesta bien formada que lo diga es <see cref="ResultadoFuente.ExitosaVacia"/>.
/// </summary>
internal interface IProveedorComparendos
{
    string Clave { get; }

    string Nombre { get; }

    IReadOnlyList<CanalComparendos> Canales { get; }

    /// <summary>Null si está conectado; si no, el motivo.</summary>
    string? NoConectado { get; }

    Task<ConsultarComparendosResponse> ConsultarAsync(ConsultarComparendosRequest pedido, CancellationToken ct);
}

internal static class Fuente
{
    public static readonly CanalComparendos[] Simit = [CanalComparendos.SimitIdentificacion, CanalComparendos.SimitPlaca];
    public static readonly CanalComparendos[] Municipal = [CanalComparendos.MunicipalIdentificacion, CanalComparendos.MunicipalPlaca];

    public static bool PorPlaca(CanalComparendos c) => c is CanalComparendos.SimitPlaca or CanalComparendos.MunicipalPlaca;

    public static ConsultarComparendosResponse Error(string proveedor, TipoErrorFuente tipo, string mensaje, int? http, long ms, string? crudo = null)
    {
        var r = new ConsultarComparendosResponse
        {
            Proveedor = proveedor,
            DuracionMs = (int)Math.Min(int.MaxValue, ms),
            Parciales = { new ResultadoParcialFuente { Resultado = ResultadoFuente.Error, TipoError = tipo, MensajeError = mensaje } },
        };
        if (http is { } h)
            r.HttpStatus = h;
        if (crudo is not null)
            r.RespuestaCruda = crudo;
        return r;
    }

    public static string Pesos(decimal v) => v.ToString("0.00", CultureInfo.InvariantCulture);

    private static readonly string[] FormatosFecha =
    [
        "yyyy-MM-dd", "yyyy-MM-ddTHH:mm:ss", "yyyy-MM-ddTHH:mm:ss.fffZ", "yyyy-MM-dd HH:mm:ss", "yyyy-MM-dd HH:mm:ss.f", "yyyy-MM-dd HH:mm",
        "dd/MM/yyyy", "d/M/yyyy", "dd/MM/yyyy HH:mm:ss", "dd/MM/yyyy HH:mm", "d/M/yyyy H:mm", "dd-MM-yyyy",
    ];

    private static readonly Dictionary<string, int> Meses = new(StringComparer.OrdinalIgnoreCase)
    {
        ["enero"] = 1, ["febrero"] = 2, ["marzo"] = 3, ["abril"] = 4, ["mayo"] = 5, ["junio"] = 6, ["julio"] = 7, ["agosto"] = 8,
        ["septiembre"] = 9, ["setiembre"] = 9, ["octubre"] = 10, ["noviembre"] = 11, ["diciembre"] = 12,
    };

    private static DateTime? Instante(string? s)
    {
        if (string.IsNullOrWhiteSpace(s))
            return null;
        var t = s.Trim();
        if (DateTime.TryParseExact(t, FormatosFecha, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var d))
            return d;

        // La fuente municipal escribe «5 de Marzo de 2025» (V1, municipalityInfractionService.ts, parseStringDate).
        var m = System.Text.RegularExpressions.Regex.Match(t, @"^(\d{1,2})\s+de\s+([a-záéíóú]+)\s+(?:de|del)\s+(\d{4})", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (m.Success && Meses.TryGetValue(m.Groups[2].Value, out var mes)
            && int.TryParse(m.Groups[1].Value, CultureInfo.InvariantCulture, out var dia) && int.TryParse(m.Groups[3].Value, CultureInfo.InvariantCulture, out var anio)
            && dia <= DateTime.DaysInMonth(anio, mes))
            return new DateTime(anio, mes, dia, 0, 0, 0, DateTimeKind.Utc);

        return DateTimeOffset.TryParse(t, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var o) ? o.UtcDateTime : null;
    }

    /// <summary>Fechas de proveedores: ISO, dd/MM/yyyy, con hora o «5 de Marzo de 2025». Null si no se entiende (no se inventa).</summary>
    public static string? Fecha(string? s) => Instante(s)?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>La hora cuando la fecha la trae (HH:mm); null si no trae hora o es medianoche exacta (fecha sin hora).</summary>
    public static string? Hora(string? s) =>
        Instante(s) is { } d && d.TimeOfDay != TimeSpan.Zero ? d.ToString("HH:mm", CultureInfo.InvariantCulture) : null;

    /// <summary>S/N, SI/NO, true/false, 1/0. Null si no se entiende.</summary>
    public static bool? SiNo(string? s) => s?.Trim().ToUpperInvariant() switch
    {
        "S" or "SI" or "SÍ" or "TRUE" or "1" or "Y" => true,
        "N" or "NO" or "FALSE" or "0" => false,
        _ => null,
    };

    /// <summary>
    /// Un valor en pesos como texto: «604100», «604100.00», «604.100», «604.100,00» o «$ 604.100». Con coma, la coma es
    /// decimal (es-CO); sin coma, un punto seguido de exactamente tres dígitos es de miles. Null si no se entiende.
    /// </summary>
    public static string? PesosDeTexto(string? s)
    {
        if (string.IsNullOrWhiteSpace(s))
            return null;
        var t = s.Replace("$", string.Empty, StringComparison.Ordinal).Replace(" ", string.Empty, StringComparison.Ordinal).Trim();
        if (t.Contains(',', StringComparison.Ordinal))
            t = t.Replace(".", string.Empty, StringComparison.Ordinal).Replace(',', '.');
        else if (System.Text.RegularExpressions.Regex.IsMatch(t, @"^\d{1,3}(\.\d{3})+$"))
            t = t.Replace(".", string.Empty, StringComparison.Ordinal);
        return decimal.TryParse(t, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var v) ? Pesos(v) : null;
    }
}

/// <summary>
/// Verifik SIMIT (el adaptador alterno que ya usa Trámites para el chequeo de multas, con sus mismas credenciales
/// <c>Verifik:*</c>). Aquí se devuelve cada multa completa. Verifik responde 404 cuando la persona no tiene registros:
/// eso, y una lista vacía bien formada, son «vacía»; cualquier otra cosa es error.
/// </summary>
internal sealed class ProveedorVerifikSimit(IHttpClientFactory http, IOptions<VerifikOptions> opciones, IOptions<ConsultationProviderModeOptions> modos) : IProveedorComparendos
{
    public const string HttpClientName = "comparendos-verifik-simit";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public string Clave => "verifik_simit";

    public string Nombre => "Verifik SIMIT";

    public IReadOnlyList<CanalComparendos> Canales => Fuente.Simit;

    public string? NoConectado =>
        ConsultationProviderModeOptions.IsMock(modos.Value.VerifikSimitMode) ? "Verifik SIMIT no está activo en este ambiente."
        : string.IsNullOrWhiteSpace(opciones.Value.ApiToken) ? "Falta la llave de Verifik en este ambiente."
        : null;

    public async Task<ConsultarComparendosResponse> ConsultarAsync(ConsultarComparendosRequest pedido, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(pedido);
        var reloj = Stopwatch.StartNew();
        var url = Fuente.PorPlaca(pedido.Canal)
            ? $"/v2/co/simit/consultar/placa?plate={Uri.EscapeDataString(pedido.Placa)}"
            : $"/v2/co/simit/consultar?documentType={Uri.EscapeDataString(pedido.TipoDocumento)}&documentNumber={Uri.EscapeDataString(pedido.NumeroDocumento)}";
        try
        {
            using var mensaje = new HttpRequestMessage(HttpMethod.Get, url);
            mensaje.Headers.Authorization = new AuthenticationHeaderValue(opciones.Value.AuthScheme, opciones.Value.ApiToken);
            mensaje.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            using var respuesta = await http.CreateClient(HttpClientName).SendAsync(mensaje, ct).ConfigureAwait(false);
            var cuerpo = await respuesta.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            var codigo = (int)respuesta.StatusCode;
            if (respuesta.StatusCode == HttpStatusCode.NotFound)
                return Vacia(codigo, reloj.ElapsedMilliseconds, cuerpo);
            if (respuesta.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                return Fuente.Error(Clave, TipoErrorFuente.Autenticacion, $"Verifik rechazó la autenticación ({codigo}).", codigo, reloj.ElapsedMilliseconds);
            if (!respuesta.IsSuccessStatusCode)
                return Fuente.Error(Clave, TipoErrorFuente.HttpNoExitoso, $"Verifik respondió {codigo}.", codigo, reloj.ElapsedMilliseconds, cuerpo);

            var payload = JsonSerializer.Deserialize<VerifikSimitResponse>(cuerpo, Json);
            // La API viva entrega el envoltorio plano { data }; la documentación, value.value.data. Se aceptan ambos.
            var multas = (payload?.Data ?? payload?.Value?.Value?.Data)?.Multas;
            if (multas is null)
                return Fuente.Error(Clave, TipoErrorFuente.RespuestaInvalida, "La respuesta de Verifik no trae la lista de multas.", codigo, reloj.ElapsedMilliseconds, cuerpo);

            var parcial = new ResultadoParcialFuente { Resultado = multas.Count == 0 ? ResultadoFuente.ExitosaVacia : ResultadoFuente.ExitosaConDatos };
            parcial.Comparendos.AddRange(multas.Where(m => !string.IsNullOrWhiteSpace(m.NumeroComparendo)).Select(Mapear));
            if (parcial.Comparendos.Count == 0 && multas.Count > 0)
                return Fuente.Error(Clave, TipoErrorFuente.RespuestaInvalida, "Verifik devolvió multas sin número de comparendo.", codigo, reloj.ElapsedMilliseconds, cuerpo);
            return new ConsultarComparendosResponse
            {
                Proveedor = Clave, HttpStatus = codigo, DuracionMs = (int)reloj.ElapsedMilliseconds, RespuestaCruda = cuerpo, ContentType = "application/json",
                Parciales = { parcial },
            };
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (TaskCanceledException)
        {
            return Fuente.Error(Clave, TipoErrorFuente.Timeout, "Verifik no respondió a tiempo.", null, reloj.ElapsedMilliseconds);
        }
        catch (HttpRequestException ex)
        {
            return Fuente.Error(Clave, TipoErrorFuente.Red, $"Error de red con Verifik: {ex.Message}", null, reloj.ElapsedMilliseconds);
        }
        catch (JsonException)
        {
            return Fuente.Error(Clave, TipoErrorFuente.RespuestaInvalida, "La respuesta de Verifik no es JSON válido.", null, reloj.ElapsedMilliseconds);
        }
    }

    private ConsultarComparendosResponse Vacia(int codigo, long ms, string cuerpo) => new()
    {
        Proveedor = Clave, HttpStatus = codigo, DuracionMs = (int)ms, RespuestaCruda = cuerpo, ContentType = "application/json",
        Parciales = { new ResultadoParcialFuente { Resultado = ResultadoFuente.ExitosaVacia } },
    };

    private static ComparendoDeFuente Mapear(VerifikSimitMulta m)
    {
        var c = new ComparendoDeFuente { Numero = m.NumeroComparendo!.Trim() };
        if (m.Placa is { Length: > 0 } placa) c.Placa = placa.Trim();
        if (m.Infractor?.TipoDocumento is { Length: > 0 } td) c.TipoDocumentoInfractor = td.Trim().ToUpperInvariant();
        if (m.Infractor?.NumeroDocumento is { Length: > 0 } nd) c.NumeroDocumentoInfractor = nd.Trim();
        var nombre = string.Join(' ', new[] { m.Infractor?.Nombre, m.Infractor?.Apellido }.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x!.Trim()));
        if (nombre.Length > 0) c.NombreInfractor = nombre;
        if (m.Infracciones?.FirstOrDefault() is { } inf)
        {
            if (inf.CodigoInfraccion is { Length: > 0 } cod) c.CodigoInfraccion = cod.Trim();
            if (inf.DescripcionInfraccion is { Length: > 0 } des) c.DescripcionInfraccion = des.Trim();
        }

        if (Fuente.Fecha(m.FechaComparendo) is { } f) c.FechaImposicion = f;
        if (m.OrganismoTransito is { Length: > 0 } ot) c.Secretaria = ot.Trim();
        if (m.Valor is { } capital) c.ValorCapital = Fuente.Pesos(capital);
        if ((m.ValorPagar ?? m.Valor) is { } total) c.ValorTotal = Fuente.Pesos(total);
        if ((m.EstadoComparendo ?? m.EstadoCartera) is { Length: > 0 } estado) c.EstadoFuente = estado.Trim();
        return c;
    }
}

/// <summary>
/// Datos de prueba para DEV y demostraciones (Consultations:ComparendosSimulados=true o Development): cada clave
/// devuelve siempre los mismos 0 a 4 comparendos; las que terminan en «000» simulan una caída (503).
/// </summary>
internal sealed class ProveedorSimulado(TimeProvider time, bool habilitado) : IProveedorComparendos
{
    private static readonly string[] Secretarias =
    [
        "SECRETARIA DISTRITAL DE MOVILIDAD DE BOGOTA", "SECRETARIA DE MOVILIDAD DE MEDELLIN", "SECRETARIA DE MOVILIDAD DE SANTIAGO DE CALI",
        "SECRETARIA DE TRANSITO Y TRANSPORTE DE BARRANQUILLA", "SECRETARIA DE TRANSITO DE SOACHA",
    ];

    private static readonly (string Codigo, string Descripcion, decimal Valor)[] Infracciones =
    [
        ("C29", "Conducir un vehículo a velocidad superior a la máxima permitida.", 604100m),
        ("C14", "Transitar por sitios restringidos o en horas prohibidas por la autoridad competente (pico y placa).", 604100m),
        ("D04", "No detenerse ante una luz roja o amarilla de semáforo.", 1208200m),
        ("C02", "Estacionar un vehículo en sitios prohibidos.", 604100m),
        ("C35", "No realizar la revisión técnico-mecánica en el plazo legal.", 604100m),
    ];

    public string Clave => "simulado";

    public string Nombre => "Simulado (solo DEV)";

    public IReadOnlyList<CanalComparendos> Canales => [.. Fuente.Simit, .. Fuente.Municipal];

    public string? NoConectado => habilitado ? null : "El proveedor simulado solo existe en el ambiente de desarrollo.";

    public Task<ConsultarComparendosResponse> ConsultarAsync(ConsultarComparendosRequest pedido, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(pedido);
        var porPlaca = Fuente.PorPlaca(pedido.Canal);
        var clave = porPlaca ? pedido.Placa : pedido.NumeroDocumento;
        if (clave.EndsWith("000", StringComparison.Ordinal))
            return Task.FromResult(Fuente.Error(Clave, TipoErrorFuente.HttpNoExitoso, "Fuente simulada no disponible (503).", 503, 40));

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"{pedido.Canal}|{clave}"));
        var municipal = Fuente.Municipal.Contains(pedido.Canal);
        var hoy = DateOnly.FromDateTime(time.GetUtcNow().AddHours(-5).DateTime);
        var parcial = new ResultadoParcialFuente();
        for (var i = 0; i < hash[0] % 5; i++)
        {
            var b = hash[(i * 5) + 1];
            var inf = Infracciones[b % Infracciones.Length];
            var fotomulta = b % 4 != 0;
            var dias = new[] { 2, 6, 14, 25, 45, 120, 400, 1200 }[hash[(i * 5) + 2] % 8];
            var imposicion = hoy.AddDays(-dias - (fotomulta ? 5 : 0));
            var c = new ComparendoDeFuente
            {
                Numero = $"{(municipal ? "M" : string.Empty)}{1100100000000 + (BitConverter.ToUInt32(hash, i * 4 % 28) % 899999999)}",
                Placa = porPlaca ? clave : $"{(char)('A' + (hash[(i * 5) + 4] % 26))}{(char)('A' + (b % 26))}{(char)('A' + (hash[i + 20] % 26))}{100 + (hash[i + 25] % 900)}",
                TipoDocumentoInfractor = porPlaca ? "NIT" : (string.IsNullOrEmpty(pedido.TipoDocumento) ? "NIT" : pedido.TipoDocumento),
                NumeroDocumentoInfractor = porPlaca ? "900123456" : clave,
                NombreInfractor = "EMPRESA DE PRUEBA S.A.S.",
                CodigoInfraccion = inf.Codigo,
                DescripcionInfraccion = inf.Descripcion,
                DireccionInfraccion = $"CALLE {10 + (b % 90)} # {b % 50}-{b % 99}",
                FechaImposicion = imposicion.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                HoraImposicion = $"{6 + (b % 14):00}:{b % 60:00}",
                EsFotomulta = fotomulta,
                Secretaria = Secretarias[hash[(i * 5) + 2] % Secretarias.Length],
                TipoVehiculo = "AUTOMOVIL",
                ServicioVehiculo = "PARTICULAR",
                ValorTotal = Fuente.Pesos(inf.Valor),
                EstadoFuente = dias > 300 ? "COBRO COACTIVO" : "PENDIENTE",
            };
            if (!fotomulta || hash[(i * 5) + 3] % 6 != 0)
                c.FechaNotificacion = (fotomulta ? hoy.AddDays(-dias) : imposicion).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            parcial.Comparendos.Add(c);
        }

        parcial.Resultado = parcial.Comparendos.Count == 0 ? ResultadoFuente.ExitosaVacia : ResultadoFuente.ExitosaConDatos;
        return Task.FromResult(new ConsultarComparendosResponse
        {
            Proveedor = Clave, HttpStatus = 200, DuracionMs = 25, ContentType = "application/json",
            RespuestaCruda = $$"""{"simulado":true,"canal":"{{pedido.Canal}}","items":{{parcial.Comparendos.Count}}}""",
            Parciales = { parcial },
        });
    }
}
