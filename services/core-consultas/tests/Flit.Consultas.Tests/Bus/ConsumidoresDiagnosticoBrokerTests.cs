using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Flit.Consultas.Api.Bus;
using Flit.Consultas.Api.Simulacion;
using Flit.Consultas.Grpc.V1;
using Flit.Platform.Sdk.Messaging;
using FluentAssertions;
using Google.Protobuf;
using RabbitMQ.Client;
using Xunit;

namespace Flit.Consultas.Tests.Bus;

/// <summary>
/// core-consultas ARRANCADO contra un RabbitMQ real (<c>FLIT_TEST_RABBITMQ</c>, usuario administrador) en un vhost efímero propio,
/// con un usuario de prueba que hace de productor autorizado (el broker valida su <c>user_id</c>). Crea el vhost y el usuario por la
/// API de administración (<c>FLIT_TEST_RABBITMQ_ADMIN</c>, por defecto el puerto 15672 del mismo host) y los borra al terminar. Sin
/// broker las pruebas se omiten en local y FALLAN en CI, como las del SDK.
/// </summary>
public sealed class AnfitrionBroker : AnfitrionBus
{
    private readonly string _sufijo = Guid.NewGuid().ToString("N")[..8];
    private readonly Uri _rabbit = new(Environment.GetEnvironmentVariable("FLIT_TEST_RABBITMQ") ?? "amqp://flit:flit-prueba@127.0.0.1:5672/");
    private HttpClient? _admin;

    public AnfitrionBroker()
    {
        Vhost = $"prueba-b-consultas-{_sufijo}";
        Productor = $"prueba-b-diag-{_sufijo}";
        Clave = Guid.NewGuid().ToString("N");
        Extra[OpcionesSimulacion.ClaveHabilitada] = "true";
        Extra[OpcionesSimulacion.ClaveAmbiente] = "LOCAL";
        Extra["Platform:Messaging:ConnectionString"] = Amqp(Usuario, Contrasena);
        Extra[$"{OpcionesConsumidoresDiagnostico.Seccion}:ProductoresPermitidos:0"] = Productor;
    }

    public string? SkipReason { get; private set; }

    public string Vhost { get; }

    /// <summary>Usuario del broker que publica como el «diagnóstico» de la prueba (permitido en la cola).</summary>
    public string Productor { get; }

    public string Clave { get; }

    public string Usuario => Uri.UnescapeDataString(_rabbit.UserInfo.Split(':')[0]);

    private string Contrasena => Uri.UnescapeDataString(_rabbit.UserInfo.Split(':', 2)[1]);

    public string Amqp(string usuario, string clave) =>
        $"amqp://{Uri.EscapeDataString(usuario)}:{Uri.EscapeDataString(clave)}@{_rabbit.Host}:{_rabbit.Port}/{Uri.EscapeDataString(Vhost)}";

    public override async ValueTask InitializeAsync()
    {
        var admin = Environment.GetEnvironmentVariable("FLIT_TEST_RABBITMQ_ADMIN") ?? $"http://{_rabbit.Host}:15672/";
        _admin = new HttpClient { BaseAddress = new Uri(admin), Timeout = TimeSpan.FromSeconds(10) };
        _admin.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{Usuario}:{Contrasena}")));
        try
        {
            await PutAsync($"api/vhosts/{E(Vhost)}", new { });
            await PutAsync($"api/permissions/{E(Vhost)}/{E(Usuario)}", new { configure = ".*", write = ".*", read = ".*" });
            await PutAsync($"api/users/{E(Productor)}", new { password = Clave, tags = "" });
            await PutAsync($"api/permissions/{E(Vhost)}/{E(Productor)}", new { configure = "", write = "^flit\\.consultas$", read = "" });
        }
        catch (HttpRequestException ex)
        {
            await LimpiarBrokerAsync();
            if (string.Equals(Environment.GetEnvironmentVariable("CI"), "true", StringComparison.OrdinalIgnoreCase))
                throw;
            SkipReason = $"RabbitMQ no alcanzable: {ex.GetType().Name}";
            return;
        }

        try
        {
            await base.InitializeAsync();
            await App.StartAsync();
            await EsperarAsync(async () => (await ColaAsync(TrabajoConsultaDiagnostico.ColaLote)) && await ColaAsync(TrabajoConsultaDiagnostico.ColaExpress), "las colas del consumidor");
        }
        catch
        {
            // xUnit no llama a DisposeAsync de un fixture que falló al iniciar: el vhost y el usuario se borran aquí.
            await LimpiarBrokerAsync();
            throw;
        }
    }

    public override async ValueTask DisposeAsync()
    {
        if (SkipReason is null)
        {
            await App.StopAsync();
            await base.DisposeAsync();
        }

        await LimpiarBrokerAsync();
        _admin?.Dispose();
    }

    /// <summary>Publica un sobre en <c>flit.consultas</c> con <paramref name="usuario"/> como <c>user_id</c> (el broker lo valida).</summary>
    public async Task PublicarAsync(EventEnvelope sobre, string usuario, string clave)
    {
        await using var conexion = await new ConnectionFactory { Uri = new Uri(Amqp(usuario, clave)) }.CreateConnectionAsync(TestContext.Current.CancellationToken);
        await using var canal = await conexion.CreateChannelAsync(cancellationToken: TestContext.Current.CancellationToken);
        var propiedades = new BasicProperties { UserId = usuario, DeliveryMode = DeliveryModes.Persistent, ContentType = "application/json" };
        await canal.BasicPublishAsync("flit.consultas", sobre.Type, mandatory: false, propiedades, Encoding.UTF8.GetBytes(sobre.ToJson()), TestContext.Current.CancellationToken);
    }

    public Task PublicarComoAdministradorAsync(EventEnvelope sobre) => PublicarAsync(sobre, Usuario, Contrasena);

    /// <summary>El siguiente mensaje de <paramref name="cola"/> (null si no llega en 15 s), con su motivo de DLQ.</summary>
    public async Task<(EventEnvelope Sobre, string? Motivo)?> TomarAsync(string cola)
    {
        await using var conexion = await new ConnectionFactory { Uri = new Uri(Amqp(Usuario, Contrasena)) }.CreateConnectionAsync(TestContext.Current.CancellationToken);
        await using var canal = await conexion.CreateChannelAsync(cancellationToken: TestContext.Current.CancellationToken);
        var limite = DateTime.UtcNow.AddSeconds(15);
        while (DateTime.UtcNow < limite)
        {
            if (await canal.BasicGetAsync(cola, autoAck: true, TestContext.Current.CancellationToken) is { } m)
            {
                var motivo = m.BasicProperties.Headers?.TryGetValue(PlatformDeadLetters.ReasonHeader, out var v) == true && v is byte[] b ? Encoding.UTF8.GetString(b) : null;
                return (EventEnvelope.FromJson(m.Body.Span), motivo);
            }

            await Task.Delay(200, TestContext.Current.CancellationToken);
        }

        return null;
    }

    /// <summary>Los routing keys con que <paramref name="cola"/> está atada a <c>flit.consultas</c>.</summary>
    public async Task<List<string>> BindingsAsync(string cola)
    {
        using var json = JsonDocument.Parse(await _admin!.GetStringAsync($"api/bindings/{E(Vhost)}/e/flit.consultas/q/{E(cola)}", TestContext.Current.CancellationToken));
        return [.. json.RootElement.EnumerateArray().Select(b => b.GetProperty("routing_key").GetString()!)];
    }

    public static async Task EsperarAsync(Func<Task<bool>> condicion, string que)
    {
        var limite = DateTime.UtcNow.AddSeconds(20);
        while (DateTime.UtcNow < limite)
        {
            if (await condicion())
                return;
            await Task.Delay(200);
        }

        throw new TimeoutException($"No se cumplió a tiempo: {que}.");
    }

    private async Task<bool> ColaAsync(string cola) =>
        (await _admin!.GetAsync($"api/queues/{E(Vhost)}/{E(cola)}")).IsSuccessStatusCode;

    private async Task PutAsync(string ruta, object cuerpo) =>
        (await _admin!.PutAsJsonAsync(ruta, cuerpo)).EnsureSuccessStatusCode();

    private async Task LimpiarBrokerAsync()
    {
        if (_admin is null)
            return;
        try
        {
            await _admin.DeleteAsync($"api/vhosts/{E(Vhost)}");
            await _admin.DeleteAsync($"api/users/{E(Productor)}");
        }
        catch (HttpRequestException)
        {
            // Sin broker no hay nada que borrar.
        }
    }

    private static string E(string valor) => Uri.EscapeDataString(valor);
}

/// <summary>
/// HU #13876 / #13727 (ADR-0075, Q3 = 3a, Q5) — del lado de core-consultas, con broker real: las dos colas se atan a
/// <c>flit.consultas</c> con su comodín, un trabajo del productor permitido se atiende por su cola (lotes o Express) y deja su
/// resultado, y uno de un productor no autorizado va a la DLQ con <c>productor_no_autorizado</c> sin resultado. El punta a punta
/// con core-diagnostico (caídas, reconciliador) es de T12.
/// <para>Uso de ejemplo: <c>await _broker.PublicarAsync(sobre, _broker.Productor, _broker.Clave)</c>.</para>
/// </summary>
public sealed class ConsumidoresDiagnosticoBrokerTests(AnfitrionBroker broker) : IClassFixture<AnfitrionBroker>
{
    [Fact]
    public async Task Las_colas_de_lotes_y_Express_se_atan_a_flit_consultas_con_su_comodin()
    {
        Assert.SkipWhen(broker.SkipReason is not null, broker.SkipReason ?? string.Empty);

        (await broker.BindingsAsync(TrabajoConsultaDiagnostico.ColaLote)).Should().Equal("consultas.diagnostico.*");
        (await broker.BindingsAsync(TrabajoConsultaDiagnostico.ColaExpress)).Should().Equal("consultas.diagnostico-express.*");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Un_trabajo_del_productor_permitido_se_atiende_por_su_cola_y_deja_su_resultado(bool esExpress)
    {
        Assert.SkipWhen(broker.SkipReason is not null, broker.SkipReason ?? string.Empty);
        var empresa = Guid.NewGuid();
        var sobre = AnfitrionBus.Trabajo(empresa, FuentesBus.Runt, RpcConsultas.PorPlaca, new ConsultarPorPlacaRequest { Placa = "TST002" }, esExpress, broker.Productor);

        await broker.PublicarAsync(sobre, broker.Productor, broker.Clave);

        await AnfitrionBroker.EsperarAsync(async () => (await broker.ResultadosAsync(sobre)).Count == 1, "el resultado del trabajo");
        var resultado = (await broker.ResultadosAsync(sobre)).Single();
        (resultado.TenantId, resultado.CorrelationId).Should().Be((empresa, sobre.CorrelationId));
        var datos = resultado.DataAs<ResultadoConsultaDiagnostico>();
        datos.Estado.Should().Be("OK");
        JsonParser.Default.Parse<ConsultarVehiculoIntempoResponse>(datos.Respuesta!.Value.GetRawText()).Vehiculo.PropietariosActuales.Single().NoDocumento
            .Should().Be("999990002");
        (await broker.ConsumosAsync(empresa)).Should().ContainSingle().Which.Producto.Should().Be(broker.Productor, "el producto es el productor del sobre");
    }

    [Fact]
    public async Task Un_trabajo_de_un_productor_no_autorizado_va_a_la_DLQ_sin_resultado()
    {
        Assert.SkipWhen(broker.SkipReason is not null, broker.SkipReason ?? string.Empty);
        var ajeno = AnfitrionBus.Trabajo(Guid.NewGuid(), FuentesBus.Runt, RpcConsultas.PorPlaca, new ConsultarPorPlacaRequest { Placa = "TST001" }, producer: broker.Usuario);
        var suplantado = AnfitrionBus.Trabajo(Guid.NewGuid(), FuentesBus.Runt, RpcConsultas.PorPlaca, new ConsultarPorPlacaRequest { Placa = "TST001" }, producer: broker.Productor);

        await broker.PublicarComoAdministradorAsync(ajeno);
        await broker.PublicarComoAdministradorAsync(suplantado);

        var muertos = new List<(EventEnvelope Sobre, string? Motivo)>();
        for (var i = 0; i < 2; i++)
            muertos.Add((await broker.TomarAsync(TrabajoConsultaDiagnostico.ColaLote + ".dlq")) ?? throw new TimeoutException("sin mensaje en la DLQ"));
        muertos.Select(m => m.Sobre.EventId).Should().BeEquivalentTo([ajeno.EventId, suplantado.EventId]);
        muertos.Should().OnlyContain(m => m.Motivo == VerificacionProductor.MotivoNoAutorizado);
        (await broker.ResultadosAsync(ajeno)).Should().BeEmpty();
        (await broker.ResultadosAsync(suplantado)).Should().BeEmpty("el user_id no es el del productor del sobre");
    }
}
