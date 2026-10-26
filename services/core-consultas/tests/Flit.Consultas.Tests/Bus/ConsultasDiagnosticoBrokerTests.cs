using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using Flit.Consultas.Api.Bus;
using Flit.Consultas.Grpc.V1;
using Flit.Platform.Grpc.V1;
using Flit.Platform.Sdk.Messaging;
using FluentAssertions;
using Google.Protobuf;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using RabbitMQ.Client;
using Xunit;

namespace Flit.Consultas.Tests.Bus;

/// <summary>
/// core-consultas ARRANCADO contra un RabbitMQ real (ADR-0075, T12): <c>FLIT_TEST_RABBITMQ</c> (usuario administrador) en un vhost
/// desechable con la topología de <c>deploy/rabbitmq/definitions.json</c> declarada por la prueba (los cinco exchanges, las tres
/// colas del diagnóstico con sus llaves y la política <c>dlq-7d</c>; el broker del CI no carga las definiciones), y con los
/// <c>ProductoresPermitidos</c> REALES (<c>["diagnostico"]</c>). El doble de core-diagnostico publica por un usuario desechable con
/// la etiqueta <c>impersonator</c> (solo escribe en <c>flit.consultas</c> de este vhost), así que el broker acepta el <c>user_id</c>
/// <c>diagnostico</c> sin tocar al usuario real del servicio; y lee los resultados de <c>diagnostico.consultas-resultados</c>, que
/// nadie consume (core-diagnostico «caído»). El vhost y el usuario se crean y se borran por la API de administración
/// (<c>FLIT_TEST_RABBITMQ_ADMIN</c>, por defecto el puerto 15672 del mismo host). Sin broker se omite en local y FALLA en CI.
/// </summary>
public sealed class AnfitrionBrokerDiagnostico : AnfitrionBus
{
    public const string ColaResultados = "diagnostico.consultas-resultados";

    private static readonly string[] Exchanges = ["flit.plataforma", "flit.tramites", "flit.consultas", "flit.notificaciones", "flit.diagnostico"];

    private readonly Uri _rabbit = new(Environment.GetEnvironmentVariable("FLIT_TEST_RABBITMQ") ?? "amqp://flit:flit-prueba@127.0.0.1:5672/");
    private readonly ConcurrentDictionary<Guid, MensajeBroker> _resultados = new();
    private HttpClient? _admin;

    public AnfitrionBrokerDiagnostico()
    {
        var sufijo = Guid.NewGuid().ToString("N")[..8];
        Vhost = $"prueba-t12-consultas-{sufijo}";
        Suplente = $"prueba-t12-suplente-{sufijo}";
        ClaveSuplente = Guid.NewGuid().ToString("N");
        Extra["Platform:Messaging:ConnectionString"] = Amqp(Usuario, Contrasena);
        Extra["Platform:Messaging:PollInterval"] = "00:00:00.200";
    }

    public string? SkipReason { get; private set; }

    public string Vhost { get; }

    /// <summary>Usuario desechable con <c>impersonator</c>: publica en <c>flit.consultas</c> con el <c>user_id</c> de otro servicio.</summary>
    public string Suplente { get; }

    private string ClaveSuplente { get; }

    /// <summary>Usuario administrador de las pruebas (el de la conexión de core-consultas en la prueba).</summary>
    public string Usuario => Uri.UnescapeDataString(_rabbit.UserInfo.Split(':')[0]);

    private string Contrasena => Uri.UnescapeDataString(_rabbit.UserInfo.Split(':', 2)[1]);

    public override async ValueTask InitializeAsync()
    {
        var admin = Environment.GetEnvironmentVariable("FLIT_TEST_RABBITMQ_ADMIN") ?? $"http://{_rabbit.Host}:15672/";
        _admin = new HttpClient { BaseAddress = new Uri(admin), Timeout = TimeSpan.FromSeconds(10) };
        _admin.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{Usuario}:{Contrasena}")));
        try
        {
            await PutAsync($"api/vhosts/{E(Vhost)}", new { });
            await PutAsync($"api/permissions/{E(Vhost)}/{E(Usuario)}", new { configure = ".*", write = ".*", read = ".*" });
            await PutAsync($"api/users/{E(Suplente)}", new { password = ClaveSuplente, tags = "impersonator" });
            await PutAsync($"api/permissions/{E(Vhost)}/{E(Suplente)}", new { configure = "", write = "^flit\\.consultas$", read = "" });
            await PutAsync($"api/policies/{E(Vhost)}/dlq-7d", new Dictionary<string, object>
            {
                ["pattern"] = "^(consultas\\.diagnostico|consultas\\.diagnostico-express|diagnostico\\.consultas-resultados)\\.dlq$",
                ["apply-to"] = "queues",
                ["priority"] = 10,
                ["definition"] = new Dictionary<string, object> { ["message-ttl"] = 604800000 },
            });
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
            await DeclararTopologiaAsync();
            await base.InitializeAsync();
            await App.StartAsync();
            await EsperarConsumidoresAsync(1);
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

    public string Amqp(string usuario, string clave) =>
        $"amqp://{Uri.EscapeDataString(usuario)}:{Uri.EscapeDataString(clave)}@{_rabbit.Host}:{_rabbit.Port}/{Uri.EscapeDataString(Vhost)}";

    /// <summary>Publica <paramref name="sobre"/> en <c>flit.consultas</c> con <paramref name="userId"/> (por el usuario suplente).</summary>
    public Task PublicarAsync(EventEnvelope sobre, string userId) => PublicarAsync(sobre, Amqp(Suplente, ClaveSuplente), userId);

    /// <summary>Publica <paramref name="sobre"/> en <c>flit.consultas</c> como el administrador (su propio <c>user_id</c>).</summary>
    public Task PublicarComoAdministradorAsync(EventEnvelope sobre) => PublicarAsync(sobre, Amqp(Usuario, Contrasena), Usuario);

    /// <summary>El resultado del trabajo <paramref name="trabajoId"/> leído de <see cref="ColaResultados"/> (los de otros trabajos se guardan).</summary>
    public async Task<MensajeBroker?> ResultadoAsync(Guid trabajoId, TimeSpan espera)
    {
        var limite = DateTime.UtcNow + espera;
        do
        {
            if (_resultados.TryRemove(trabajoId, out var guardado))
                return guardado;
            while (await TomarAsync(ColaResultados, TimeSpan.Zero) is { } m)
                _resultados[m.Sobre.Data.GetProperty("trabajoId").GetGuid()] = m;
            if (_resultados.TryRemove(trabajoId, out var leido))
                return leido;
            await Task.Delay(100, TestContext.Current.CancellationToken);
        }
        while (DateTime.UtcNow < limite);

        return null;
    }

    /// <summary>El siguiente mensaje de <paramref name="cola"/> (null si no llega en <paramref name="espera"/>), con su <c>user_id</c> y su motivo de DLQ.</summary>
    public async Task<MensajeBroker?> TomarAsync(string cola, TimeSpan espera)
    {
        await using var conexion = await Fabrica().CreateConnectionAsync(TestContext.Current.CancellationToken);
        await using var canal = await conexion.CreateChannelAsync(cancellationToken: TestContext.Current.CancellationToken);
        var limite = DateTime.UtcNow + espera;
        do
        {
            if (await canal.BasicGetAsync(cola, autoAck: true, TestContext.Current.CancellationToken) is { } m)
            {
                var motivo = m.BasicProperties.Headers?.TryGetValue(PlatformDeadLetters.ReasonHeader, out var v) == true && v is byte[] b ? Encoding.UTF8.GetString(b) : null;
                return new MensajeBroker(EventEnvelope.FromJson(m.Body.Span), m.RoutingKey, m.BasicProperties.UserId, motivo);
            }

            if (espera > TimeSpan.Zero)
                await Task.Delay(100, TestContext.Current.CancellationToken);
        }
        while (DateTime.UtcNow < limite);

        return null;
    }

    /// <summary>Mensajes listos y consumidores de <paramref name="cola"/> (declaración pasiva: sin el retraso de las estadísticas).</summary>
    public async Task<(uint Mensajes, uint Consumidores)> ColaAsync(string cola)
    {
        await using var conexion = await Fabrica().CreateConnectionAsync(TestContext.Current.CancellationToken);
        await using var canal = await conexion.CreateChannelAsync(cancellationToken: TestContext.Current.CancellationToken);
        var ok = await canal.QueueDeclarePassiveAsync(cola, TestContext.Current.CancellationToken);
        return (ok.MessageCount, ok.ConsumerCount);
    }

    /// <summary>Los consumidores de lotes y Express del SDK (servicios hospedados), para apagar y volver a encender core-consultas.</summary>
    public IReadOnlyList<IHostedService> Consumidores() =>
        [.. App.Services.GetServices<IHostedService>().Where(s => s.GetType().Name.StartsWith("PlatformConsumer", StringComparison.Ordinal)
            && s.GetType().GetGenericArguments()[1] is var efecto && (efecto == typeof(ConsumidorTrabajosLote) || efecto == typeof(ConsumidorTrabajosExpress)))];

    public async Task EsperarConsumidoresAsync(uint cuantos) =>
        await EsperarAsync(async () => (await ColaAsync(TrabajoConsultaDiagnostico.ColaLote)).Consumidores == cuantos
            && (await ColaAsync(TrabajoConsultaDiagnostico.ColaExpress)).Consumidores == cuantos, $"{cuantos} consumidor(es) en cada cola de trabajos");

    public static async Task EsperarAsync(Func<Task<bool>> condicion, string que, int segundos = 30)
    {
        var limite = DateTime.UtcNow.AddSeconds(segundos);
        while (DateTime.UtcNow < limite)
        {
            if (await condicion())
                return;
            await Task.Delay(150);
        }

        throw new TimeoutException($"No se cumplió a tiempo: {que}.");
    }

    private ConnectionFactory Fabrica() => new() { Uri = new Uri(Amqp(Usuario, Contrasena)) };

    private async Task DeclararTopologiaAsync()
    {
        await using var conexion = await Fabrica().CreateConnectionAsync();
        await using var canal = await conexion.CreateChannelAsync();
        foreach (var exchange in Exchanges)
            await canal.ExchangeDeclareAsync(exchange, ExchangeType.Topic, durable: true, autoDelete: false);
        foreach (var (cola, llave) in new[]
        {
            (TrabajoConsultaDiagnostico.ColaLote, "consultas.diagnostico.*"),
            (TrabajoConsultaDiagnostico.ColaExpress, "consultas.diagnostico-express.*"),
            (ColaResultados, ResultadoConsultaDiagnostico.Tipo),
        })
        {
            await canal.QueueDeclareAsync(cola, durable: true, exclusive: false, autoDelete: false);
            await canal.QueueBindAsync(cola, "flit.consultas", llave);
        }
    }

    private static async Task PublicarAsync(EventEnvelope sobre, string uri, string userId)
    {
        await using var conexion = await new ConnectionFactory { Uri = new Uri(uri) }.CreateConnectionAsync(TestContext.Current.CancellationToken);
        await using var canal = await conexion.CreateChannelAsync(new CreateChannelOptions(publisherConfirmationsEnabled: true, publisherConfirmationTrackingEnabled: true), TestContext.Current.CancellationToken);
        var propiedades = new BasicProperties { UserId = userId, DeliveryMode = DeliveryModes.Persistent, ContentType = "application/json" };
        await canal.BasicPublishAsync("flit.consultas", sobre.Type, mandatory: false, propiedades, Encoding.UTF8.GetBytes(sobre.ToJson()), TestContext.Current.CancellationToken);
    }

    private async Task PutAsync(string ruta, object cuerpo) =>
        (await _admin!.PutAsJsonAsync(ruta, cuerpo)).EnsureSuccessStatusCode();

    private async Task LimpiarBrokerAsync()
    {
        if (_admin is null)
            return;
        try
        {
            await _admin.DeleteAsync($"api/vhosts/{E(Vhost)}");
            await _admin.DeleteAsync($"api/users/{E(Suplente)}");
        }
        catch (HttpRequestException)
        {
            // Sin broker no hay nada que borrar.
        }
    }

    private static string E(string valor) => Uri.EscapeDataString(valor);
}

/// <summary>Un mensaje leído del broker: el sobre, la llave, el <c>user_id</c> que validó el broker y el motivo de DLQ si lo hay.</summary>
public sealed record MensajeBroker(EventEnvelope Sobre, string RoutingKey, string? UserId, string? Motivo);

/// <summary>
/// HU #13727 / #13876 (ADR-0075, T12, Q3–Q5) — del lado de core-consultas, con broker real y los productores permitidos reales: un
/// trabajo publicado por «diagnostico» (<c>user_id</c> validado por el broker) se atiende y su resultado espera en
/// <c>diagnostico.consultas-resultados</c> aunque core-diagnostico no lo consuma; con core-consultas apagado el trabajo espera en su
/// cola y se procesa al volver, sin ERROR; el Express no espera detrás de un lote lleno de consultas lentas; y un trabajo de otro
/// productor (o suplantado) va a la DLQ sin consulta ni resultado. El lado de core-diagnostico (cierre de celdas, liberación de
/// diferidas, reconciliador) es la otra mitad (<c>Flit.Diagnostico.Tests.Bus.ConsultasDiagnosticoBrokerTests</c>).
/// <para>Uso de ejemplo: <c>await broker.PublicarAsync(AnfitrionBus.Trabajo(empresa, FuentesBus.Runt, RpcConsultas.PorPlaca, solicitud), userId: "diagnostico")</c>.</para>
/// </summary>
public sealed class ConsultasDiagnosticoBrokerTests(AnfitrionBrokerDiagnostico broker) : IClassFixture<AnfitrionBrokerDiagnostico>
{
    private const string Diagnostico = OpcionesConsumidoresDiagnostico.ProductorDiagnostico;

    [Fact]
    public async Task Punta_a_punta_el_trabajo_de_diagnostico_deja_su_resultado_esperando_en_la_cola_de_diagnostico()
    {
        Assert.SkipWhen(broker.SkipReason is not null, broker.SkipReason ?? string.Empty);
        var empresa = Guid.NewGuid();
        var sobre = AnfitrionBus.Trabajo(empresa, FuentesBus.Runt, RpcConsultas.PorPlaca, new ConsultarPorPlacaRequest { Placa = AnfitrionBus.PlacaEncontrada });

        await broker.PublicarAsync(sobre, userId: Diagnostico);

        await AnfitrionBrokerDiagnostico.EsperarAsync(async () => (await broker.ColaAsync(AnfitrionBrokerDiagnostico.ColaResultados)) == (1u, 0u),
            "el resultado esperando en diagnostico.consultas-resultados sin consumidor (core-diagnostico caído)");
        var mensaje = await broker.ResultadoAsync(sobre.DataAs<TrabajoConsultaDiagnostico>().TrabajoId, TimeSpan.FromSeconds(5));
        mensaje.Should().NotBeNull();
        (mensaje!.Sobre.Producer, mensaje.Sobre.TenantId, mensaje.Sobre.CorrelationId, mensaje.RoutingKey).Should().Be(("consultas", empresa, sobre.CorrelationId, ResultadoConsultaDiagnostico.Tipo));
        mensaje.UserId.Should().Be(broker.Usuario, "el publicador del SDK pone el usuario de su conexión; en producción, consultas");
        var datos = mensaje.Sobre.DataAs<ResultadoConsultaDiagnostico>();
        (datos.Estado, datos.ConsultaId).Should().Be((ResultadoConsultaDiagnostico.Ok, sobre.DataAs<TrabajoConsultaDiagnostico>().ConsultaId));
        JsonParser.Default.Parse<ConsultarVehiculoIntempoResponse>(datos.Respuesta!.Value.GetRawText()).Vehiculo.Plate.Should().Be(AnfitrionBus.PlacaEncontrada);
        (await broker.ConsumosAsync(empresa)).Should().ContainSingle().Which.Producto.Should().Be(Diagnostico);
    }

    [Fact]
    public async Task Con_consultas_caido_el_trabajo_espera_en_su_cola_y_se_procesa_al_volver_sin_ERROR()
    {
        Assert.SkipWhen(broker.SkipReason is not null, broker.SkipReason ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        foreach (var consumidor in broker.Consumidores())
            await consumidor.StopAsync(ct);
        await broker.EsperarConsumidoresAsync(0);
        var empresa = Guid.NewGuid();
        var sobre = AnfitrionBus.Trabajo(empresa, FuentesBus.Runt, RpcConsultas.PorPlaca, new ConsultarPorPlacaRequest { Placa = AnfitrionBus.PlacaEncontrada });
        var trabajoId = sobre.DataAs<TrabajoConsultaDiagnostico>().TrabajoId;

        try
        {
            await broker.PublicarAsync(sobre, userId: Diagnostico);
            await Task.Delay(TimeSpan.FromSeconds(2), ct);
            (await broker.ColaAsync(TrabajoConsultaDiagnostico.ColaLote)).Should().Be((1u, 0u), "el trabajo espera en la cola durable");
            (await broker.ResultadoAsync(trabajoId, TimeSpan.Zero)).Should().BeNull();
        }
        finally
        {
            foreach (var consumidor in broker.Consumidores())
                await consumidor.StartAsync(ct);
        }

        await broker.EsperarConsumidoresAsync(1);
        var resultado = await broker.ResultadoAsync(trabajoId, TimeSpan.FromSeconds(20));
        resultado.Should().NotBeNull("al volver, core-consultas atiende lo que esperaba");
        resultado!.Sobre.DataAs<ResultadoConsultaDiagnostico>().Estado.Should().Be(ResultadoConsultaDiagnostico.Ok);
        (await broker.ColaAsync(TrabajoConsultaDiagnostico.ColaLote)).Mensajes.Should().Be(0);
    }

    [Fact]
    public async Task El_Express_no_espera_detras_de_un_lote_lleno()
    {
        Assert.SkipWhen(broker.SkipReason is not null, broker.SkipReason ?? string.Empty);
        var empresa = Guid.NewGuid();
        // 16 consultas que tardan el tiempo máximo (2 s en la prueba) con concurrencia 8: el lote tiene trabajo para unos 4 s.
        var lote = Enumerable.Range(0, 16)
            .Select(_ => AnfitrionBus.Trabajo(empresa, FuentesBus.SimitPropietario, RpcConsultas.Multas, new ConsultarMultasRequest
            {
                Opciones = new OpcionesConsulta { Proveedor = "falso_lento" },
                Documento = new DocumentoIdentidad { Tipo = "CC", Numero = "9999900011" },
                TipoPersona = TipoPersona.Natural,
            }))
            .ToList();
        foreach (var trabajo in lote)
            await broker.PublicarAsync(trabajo, userId: Diagnostico);
        var express = AnfitrionBus.Trabajo(empresa, FuentesBus.Runt, RpcConsultas.PorPlaca, new ConsultarPorPlacaRequest { Placa = AnfitrionBus.PlacaEncontrada }, esExpress: true);

        await broker.PublicarAsync(express, userId: Diagnostico);

        var resultadoExpress = await broker.ResultadoAsync(express.DataAs<TrabajoConsultaDiagnostico>().TrabajoId, TimeSpan.FromSeconds(10));
        resultadoExpress.Should().NotBeNull();
        resultadoExpress!.Sobre.DataAs<ResultadoConsultaDiagnostico>().Estado.Should().Be(ResultadoConsultaDiagnostico.Ok);
        var loteListos = 0;
        foreach (var trabajo in lote)
        {
            if (await broker.ResultadoAsync(trabajo.DataAs<TrabajoConsultaDiagnostico>().TrabajoId, TimeSpan.Zero) is not null)
                loteListos++;
        }

        loteListos.Should().BeLessThan(lote.Count, "el Express salió antes de que terminara el lote");

        // El lote termina con su DeadlineExceeded (lo decide Diagnóstico): no queda trabajo pendiente para las demás pruebas.
        var estados = new List<string?>();
        foreach (var trabajo in lote)
            estados.Add((await broker.ResultadoAsync(trabajo.DataAs<TrabajoConsultaDiagnostico>().TrabajoId, TimeSpan.FromSeconds(30)))?.Sobre.DataAs<ResultadoConsultaDiagnostico>().GrpcStatus);
        estados.Should().HaveCount(16).And.OnlyContain(s => s == "DeadlineExceeded");
    }

    [Fact]
    public async Task Un_trabajo_de_un_productor_no_autorizado_o_suplantado_va_a_la_DLQ_sin_consulta_ni_resultado()
    {
        Assert.SkipWhen(broker.SkipReason is not null, broker.SkipReason ?? string.Empty);
        var empresa = Guid.NewGuid();
        var deTramites = AnfitrionBus.Trabajo(empresa, FuentesBus.Runt, RpcConsultas.PorPlaca, new ConsultarPorPlacaRequest { Placa = AnfitrionBus.PlacaEncontrada }, producer: "tramites");
        var suplantado = AnfitrionBus.Trabajo(empresa, FuentesBus.Runt, RpcConsultas.PorPlaca, new ConsultarPorPlacaRequest { Placa = AnfitrionBus.PlacaEncontrada }, producer: Diagnostico);

        await broker.PublicarAsync(deTramites, userId: "tramites");
        await broker.PublicarComoAdministradorAsync(suplantado);

        var muertos = new List<MensajeBroker>();
        for (var i = 0; i < 2; i++)
            muertos.Add(await broker.TomarAsync(TrabajoConsultaDiagnostico.ColaLote + ".dlq", TimeSpan.FromSeconds(15)) ?? throw new TimeoutException("sin mensaje en la DLQ"));
        muertos.Select(m => m.Sobre.EventId).Should().BeEquivalentTo([deTramites.EventId, suplantado.EventId]);
        muertos.Should().OnlyContain(m => m.Motivo == VerificacionProductor.MotivoNoAutorizado);
        (await broker.ResultadoAsync(deTramites.DataAs<TrabajoConsultaDiagnostico>().TrabajoId, TimeSpan.FromSeconds(1))).Should().BeNull();
        (await broker.ResultadoAsync(suplantado.DataAs<TrabajoConsultaDiagnostico>().TrabajoId, TimeSpan.Zero)).Should().BeNull("el user_id no es el del productor del sobre");
        (await broker.ConsumosAsync(empresa)).Should().BeEmpty("no se consultó al proveedor");
    }
}
