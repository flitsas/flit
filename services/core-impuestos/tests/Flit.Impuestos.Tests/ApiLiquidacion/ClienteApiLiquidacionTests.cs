using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Flit.Impuestos.Api.Modulos.ApiLiquidacion;
using Flit.Impuestos.Api.Plataforma;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Flit.Impuestos.Tests.ApiLiquidacion;

/// <summary>
/// HU #14694 (M06, plan §5.6) — el cliente del API de liquidación contra un <see cref="ApiLiquidacionDePrueba"/>: el alta va con
/// <c>Authorization: Bearer {INTEGRATION_TOKEN}</c> y el cuerpo EXACTO <c>{legal_name, tax_id, contact_email}</c>; el resto, con
/// <c>X-API-Key</c> y sin Bearer. Las respuestas se clasifican (201, 409 <c>tax_id_taken</c>, 5xx, timeout, conexión rechazada)
/// y la configuración falla cerrada (sin token no hay alta; base http fuera de loopback no se usa).
/// </summary>
/// <example>
/// <code>
/// var (cliente, api) = ClienteApiLiquidacionTests.Crear();
/// api.Responder = _ => ApiLiquidacionDePrueba.Json(HttpStatusCode.OK, "{\"items\":[]}");
/// var r = await cliente.ObtenerFuentesAsync("llave", ct);
/// </code>
/// </example>
public sealed class ClienteApiLiquidacionTests
{
    public const string Base = "https://api.prueba";
    public const string Token = "token-integracion-de-prueba-no-real";
    public const string ApiKey = "AbCdEfGhIjKlMnOpQrStUvWxYz0123456789-_abcde";
    public const string WebhookSecret = "whsec_secreto-de-prueba-no-real";

    public const string Alta201 =
        "{\"company\":{\"id\":\"3f6c2a7e-6a0b-4c8e-9a51-2f3b5d7c9e10\",\"legal_name\":\"Flota Andina SAS\",\"tax_id\":\"900123456\"," +
        "\"contact_email\":\"ops@flota.example\",\"active\":true,\"api_key_prefix\":\"AbCdEfGh\",\"has_webhook_secret\":true}," +
        "\"api_key\":\"" + ApiKey + "\",\"webhook_secret\":\"" + WebhookSecret + "\"}";

    public const string Fuentes200 =
        "{\"items\":[{\"id\":\"f-05\",\"name\":\"Antioquia\",\"department_code\":\"05\",\"department_name\":\"ANTIOQUIA\",\"active\":true," +
        "\"params_schema\":{\"type\":\"object\"}},{\"id\":\"f-41\",\"name\":\"Huila\",\"department_code\":\"41\",\"department_name\":\"HUILA\",\"active\":false,\"params_schema\":{}}]}";

    [Fact]
    public async Task El_alta_va_con_Bearer_y_el_cuerpo_exacto_del_contrato()
    {
        var ct = TestContext.Current.CancellationToken;
        var (cliente, api, _) = Crear();
        api.Responder = _ => ApiLiquidacionDePrueba.Json(HttpStatusCode.Created, Alta201);

        var r = await cliente.CrearEmpresaAsync(new AltaEmpresaPeticion("Flota Andina SAS", "900123456", "ops@flota.example"), ct);

        var pedido = api.Pedidos.Should().ContainSingle().Subject;
        pedido.Metodo.Should().Be("POST");
        pedido.Uri.Should().Be(Base + "/api/v1/integracion/empresas");
        pedido.Cabeceras["Authorization"].Should().Be("Bearer " + Token);
        pedido.Cabeceras.Should().NotContainKey("X-API-Key");
        pedido.TipoContenido.Should().Be("application/json");
        pedido.Cuerpo.Should().Be("{\"legal_name\":\"Flota Andina SAS\",\"tax_id\":\"900123456\",\"contact_email\":\"ops@flota.example\"}");

        r.Exitoso.Should().BeTrue();
        r.Status.Should().Be(201);
        r.Valor!.Company.Id.Should().Be("3f6c2a7e-6a0b-4c8e-9a51-2f3b5d7c9e10");
        r.Valor.Company.ApiKeyPrefix.Should().Be("AbCdEfGh");
        r.Valor.ApiKey.Should().Be(ApiKey);
        r.Valor.WebhookSecret.Should().Be(WebhookSecret);
    }

    [Fact]
    public async Task El_alta_409_es_respuesta_con_tax_id_taken_y_sin_credenciales()
    {
        var ct = TestContext.Current.CancellationToken;
        var (cliente, api, _) = Crear();
        api.Responder = _ => ApiLiquidacionDePrueba.Json(HttpStatusCode.Conflict, "{\"error\":{\"code\":\"tax_id_taken\",\"message\":\"ya existe\"}}");

        var r = await cliente.CrearEmpresaAsync(new AltaEmpresaPeticion("Flota", "900123456", "ops@flota.example"), ct);

        r.Clase.Should().Be(ClaseEnvio.Respuesta);
        r.Status.Should().Be(409);
        r.CodigoError.Should().Be(ErroresApi.TaxIdTaken);
        r.Exitoso.Should().BeFalse();
        r.Valor.Should().BeNull();
    }

    [Fact]
    public async Task El_alta_sin_INTEGRATION_TOKEN_no_sale()
    {
        var ct = TestContext.Current.CancellationToken;
        var (cliente, api, _) = Crear(token: null);

        var accion = () => cliente.CrearEmpresaAsync(new AltaEmpresaPeticion("Flota", "900123456", "ops@flota.example"), ct);

        (await accion.Should().ThrowAsync<InvalidOperationException>()).Which.Message.Should().Contain("IMPUESTOS_SVC_INTEGRATION_TOKEN");
        api.Pedidos.Should().BeEmpty();
    }

    [Fact]
    public async Task Las_fuentes_van_con_X_API_Key_y_sin_Bearer()
    {
        var ct = TestContext.Current.CancellationToken;
        var (cliente, api, _) = Crear(baseUrl: Base + "/");
        api.Responder = _ => ApiLiquidacionDePrueba.Json(HttpStatusCode.OK, Fuentes200);

        var r = await cliente.ObtenerFuentesAsync(ApiKey, ct);

        var pedido = api.Pedidos.Should().ContainSingle().Subject;
        pedido.Metodo.Should().Be("GET");
        pedido.Uri.Should().Be(Base + "/api/v1/fuentes", "la barra final de la base no duplica la de la ruta");
        pedido.Cabeceras["X-API-Key"].Should().Be(ApiKey);
        pedido.Cabeceras.Should().NotContainKey("Authorization");
        r.Exitoso.Should().BeTrue();
        r.Valor!.Items.Select(f => (f.DepartmentCode, f.Active)).Should().Equal(("05", true), ("41", false));
    }

    [Fact]
    public async Task La_consulta_manda_solo_los_campos_del_contrato_y_omite_callbackUrl_nulo()
    {
        var ct = TestContext.Current.CancellationToken;
        var (cliente, api, _) = Crear();
        api.Responder = _ => ApiLiquidacionDePrueba.Json(HttpStatusCode.Accepted, "{\"id\":\"c-1\",\"status\":\"queued\",\"source_id\":\"f-25\"}");
        var detalle = new Dictionary<string, object?> { ["secretaryDepartmentCode"] = "25", ["OwnerName"] = "PROPIETARIO DE PRUEBA" };

        var r = await cliente.CrearConsultaAsync(ApiKey, new SolicitudConsulta("CC", "1000000001", "ABC123", null, detalle), ct);

        var pedido = api.Pedidos.Should().ContainSingle().Subject;
        pedido.Metodo.Should().Be("POST");
        pedido.Uri.Should().Be(Base + "/api/v1/consultas");
        pedido.Cabeceras["X-API-Key"].Should().Be(ApiKey);
        using var cuerpo = JsonDocument.Parse(pedido.Cuerpo);
        cuerpo.RootElement.EnumerateObject().Select(p => p.Name).Should().Equal("documentType", "documentNumber", "plateNumber", "detail");
        cuerpo.RootElement.GetProperty("detail").GetProperty("secretaryDepartmentCode").GetString().Should().Be("25");
        r.Exitoso.Should().BeTrue();
        r.Valor!.Id.Should().Be("c-1");
    }

    [Theory]
    [InlineData("processStep")]
    [InlineData("lastTaxHandler")]
    [InlineData("credentials")]
    public async Task La_consulta_rechaza_los_campos_del_contrato_anterior_sin_llamar(string campo)
    {
        var ct = TestContext.Current.CancellationToken;
        var (cliente, api, _) = Crear();
        var detalle = new Dictionary<string, object?> { ["secretaryDepartmentCode"] = "05", [campo] = "x" };

        var accion = () => cliente.CrearConsultaAsync(ApiKey, new SolicitudConsulta("CC", "1000000001", "ABC123", null, detalle), ct);

        await accion.Should().ThrowAsync<ArgumentException>();
        api.Pedidos.Should().BeEmpty();
    }

    [Fact]
    public async Task Consulta_listado_y_archivo_usan_sus_rutas_con_X_API_Key()
    {
        var ct = TestContext.Current.CancellationToken;
        var (cliente, api, _) = Crear();
        api.Responder = p => p.RequestUri!.AbsolutePath switch
        {
            "/api/v1/consultas/c%201" => ApiLiquidacionDePrueba.Json(HttpStatusCode.OK, "{\"id\":\"c 1\",\"status\":\"running\",\"attempt\":1,\"result\":null}"),
            "/api/v1/consultas" => ApiLiquidacionDePrueba.Json(HttpStatusCode.OK, "{\"items\":[{\"id\":\"c-2\",\"status\":\"succeeded\",\"callback_url\":\"https://x/avisos/t\"}],\"next_cursor\":\"k2\"}"),
            _ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([0x25, 0x50, 0x44, 0x46]) { Headers = { ContentType = new("application/pdf") } } },
        };

        var consulta = await cliente.ObtenerConsultaAsync(ApiKey, "c 1", ct);
        var listado = await cliente.ListarConsultasAsync(ApiKey, "succeeded", 50, "k1", ct);
        var archivo = await cliente.DescargarArchivoAsync(ApiKey, "f-1", ct);

        api.Pedidos.Select(p => p.Uri).Should().Equal(
            Base + "/api/v1/consultas/c%201",
            Base + "/api/v1/consultas?status=succeeded&limit=50&cursor=k1",
            Base + "/api/v1/files/f-1/download");
        api.Pedidos.Should().OnlyContain(p => p.Metodo == "GET" && p.Cabeceras["X-API-Key"] == ApiKey && !p.Cabeceras.ContainsKey("Authorization"));
        consulta.Valor!.Status.Should().Be("running");
        consulta.Valor.CuerpoCrudo.Should().Contain("\"result\":null");
        listado.Valor!.NextCursor.Should().Be("k2");
        listado.Valor.Items.Should().ContainSingle().Which.CallbackUrl.Should().Be("https://x/avisos/t");
        archivo.Valor!.Contenido.Should().Equal(0x25, 0x50, 0x44, 0x46);
        archivo.Valor.TipoContenido.Should().Be("application/pdf");
    }

    [Fact]
    public async Task Un_5xx_es_incierto_y_un_2xx_ilegible_tambien()
    {
        var ct = TestContext.Current.CancellationToken;
        var (cliente, api, _) = Crear();
        api.Responder = _ => ApiLiquidacionDePrueba.Json(HttpStatusCode.BadGateway, "<html>");
        var r5xx = await cliente.CrearEmpresaAsync(new AltaEmpresaPeticion("Flota", "900123456", "ops@flota.example"), ct);
        api.Responder = _ => ApiLiquidacionDePrueba.Json(HttpStatusCode.Created, "{no es json");
        var ilegible = await cliente.CrearEmpresaAsync(new AltaEmpresaPeticion("Flota", "900123456", "ops@flota.example"), ct);

        r5xx.Clase.Should().Be(ClaseEnvio.Incierto);
        r5xx.Status.Should().Be(502);
        ilegible.Clase.Should().Be(ClaseEnvio.Incierto, "el alta pudo crearse: perder la respuesta es un incierto, no un error");
        ilegible.Status.Should().Be(201);
    }

    [Fact]
    public async Task Conexion_rechazada_es_no_salio_y_el_timeout_es_incierto()
    {
        var ct = TestContext.Current.CancellationToken;
        var (cliente, api, _) = Crear(timeoutSegundos: 1);
        api.Responder = _ => throw new HttpRequestException("rechazada", new SocketException((int)SocketError.ConnectionRefused));
        var rechazada = await cliente.ObtenerFuentesAsync(ApiKey, ct);
        api.Demora = TimeSpan.FromSeconds(10);
        api.Responder = _ => ApiLiquidacionDePrueba.Json(HttpStatusCode.OK, Fuentes200);
        var timeout = await cliente.ObtenerFuentesAsync(ApiKey, ct);

        rechazada.Clase.Should().Be(ClaseEnvio.NoSalio);
        timeout.Clase.Should().Be(ClaseEnvio.Incierto);
    }

    [Fact]
    public async Task La_base_http_fuera_de_loopback_no_se_usa()
    {
        var ct = TestContext.Current.CancellationToken;
        var (cliente, api, _) = Crear(baseUrl: "http://api.prueba");

        var accion = () => cliente.ObtenerFuentesAsync(ApiKey, ct);

        (await accion.Should().ThrowAsync<InvalidOperationException>()).Which.Message.Should().Contain("IMPUESTOS_SVC_API_BASE_URL");
        api.Pedidos.Should().BeEmpty();
    }

    [Fact]
    public async Task El_log_del_cliente_no_lleva_el_token_ni_la_api_key()
    {
        var ct = TestContext.Current.CancellationToken;
        var (cliente, api, logs) = Crear();
        api.Responder = p => p.RequestUri!.AbsolutePath == ClienteApiLiquidacion.RutaAlta
            ? ApiLiquidacionDePrueba.Json(HttpStatusCode.Created, Alta201)
            : ApiLiquidacionDePrueba.Json(HttpStatusCode.OK, Fuentes200);

        await cliente.CrearEmpresaAsync(new AltaEmpresaPeticion("Flota", "900123456", "ops@flota.example"), ct);
        await cliente.ObtenerFuentesAsync(ApiKey, ct);

        var log = logs.Todo;
        log.Should().Contain("X-API-Key", "el registro de HttpClient sí escribe las cabeceras (si no, la prueba no prueba nada)");
        log.Should().NotContain(Token).And.NotContain(ApiKey).And.NotContain(WebhookSecret);
    }

    /// <summary>Cliente real (registro del servicio + redacción de cabeceras + log en memoria) contra el doble del API.</summary>
    internal static (ClienteApiLiquidacion Cliente, ApiLiquidacionDePrueba Api, RegistroDeLogs Logs) Crear(
        string? baseUrl = Base, string? token = Token, int timeoutSegundos = 30)
    {
        var api = new ApiLiquidacionDePrueba();
        var logs = new RegistroDeLogs();
        var configuracion = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Impuestos:Api:BaseUrl"] = baseUrl,
            ["Impuestos:Api:IntegrationToken"] = token,
            ["Impuestos:Api:TimeoutSegundos"] = timeoutSegundos.ToString(System.Globalization.CultureInfo.InvariantCulture),
        }).Build();
        var services = new ServiceCollection();
        services.AddLogging(l => l.SetMinimumLevel(LogLevel.Trace).AddProvider(logs));
        services.AddRedaccionCabeceras();
        services.AddClienteApiLiquidacion(configuracion);
        services.AddHttpClient<ClienteApiLiquidacion>().ConfigurePrimaryHttpMessageHandler(() => api);
        var provider = services.BuildServiceProvider();
        return (provider.GetRequiredService<ClienteApiLiquidacion>(), api, logs);
    }
}

/// <summary>
/// Doble del API de liquidación para las pruebas: guarda cada pedido (método, URI, cabeceras, tipo y cuerpo) y responde con
/// <see cref="Responder"/> (o lanza lo que este lance), después de <see cref="Demora"/> si se fija.
/// </summary>
public sealed class ApiLiquidacionDePrueba : HttpMessageHandler
{
    public ConcurrentQueue<PedidoRegistrado> Cola { get; } = new();

    public IReadOnlyList<PedidoRegistrado> Pedidos => Cola.ToList();

    public Func<HttpRequestMessage, HttpResponseMessage> Responder { get; set; } =
        _ => Json(HttpStatusCode.OK, ClienteApiLiquidacionTests.Fuentes200);

    public TimeSpan Demora { get; set; } = TimeSpan.Zero;

    public static HttpResponseMessage Json(HttpStatusCode status, string cuerpo) =>
        new(status) { Content = new StringContent(cuerpo, Encoding.UTF8, "application/json") };

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var cuerpo = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
        var cabeceras = request.Headers.ToDictionary(h => h.Key, h => string.Join(",", h.Value), StringComparer.OrdinalIgnoreCase);
        Cola.Enqueue(new PedidoRegistrado(request.Method.Method, request.RequestUri!.AbsoluteUri, cabeceras, request.Content?.Headers.ContentType?.MediaType, cuerpo));
        if (Demora > TimeSpan.Zero)
            await Task.Delay(Demora, cancellationToken);
        return Responder(request);
    }
}

public sealed record PedidoRegistrado(string Metodo, string Uri, IReadOnlyDictionary<string, string> Cabeceras, string? TipoContenido, string Cuerpo);

/// <summary>Log en memoria de todo nivel y categoría, para probar que no lleva secretos.</summary>
public sealed class RegistroDeLogs : ILoggerProvider
{
    private readonly ConcurrentQueue<string> _lineas = new();

    public string Todo => string.Join("\n", _lineas);

    public ILogger CreateLogger(string categoryName) => new Logger(_lineas);

    public void Dispose()
    {
    }

    private sealed class Logger(ConcurrentQueue<string> lineas) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            lineas.Enqueue(formatter(state, exception) + (exception is null ? string.Empty : " " + exception));
    }
}
