using System.Diagnostics;
using System.Net;
using Flit.Modules.Notificaciones.Webhooks;
using Flit.Notificaciones.Api;
using Flit.Notificaciones.Api.Persistence;
using Flit.Notificaciones.Api.Webhooks;
using Flit.Platform.Sdk.Messaging;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using RabbitMQ.Client;
using Xunit;

namespace Flit.Notificaciones.Tests;

/// <summary>
/// HU #13356 (Epic #13316) — core-notificaciones entrega los webhooks salientes que le dejan como trabajos, contra
/// Postgres y RabbitMQ reales (<c>FLIT_TEST_RABBITMQ</c>; sin broker se omite): el POST sale con el cuerpo exacto y las
/// cabeceras firmadas (AC1); un destino interno se bloquea y se registra (AC2); un destino caído se reintenta y termina
/// en la .dlq (AC3). El destino es un HttpMessageHandler falso; el filtro, uno que bloquea los hosts «interno».
/// </summary>
[Collection(ColeccionBus.Nombre)]
public sealed class WebhooksSalientesTests : IAsyncLifetime
{
    private static readonly string RabbitMq = Environment.GetEnvironmentVariable("FLIT_TEST_RABBITMQ") ?? "amqp://flit:flit-prueba@127.0.0.1:5672/";
    private const string Cola = TrabajoWebhookConsumer.Cola;

    private readonly Guid _empresa = Guid.NewGuid();
    private readonly DestinoFalso _destino = new();
    private string _database = string.Empty;
    private string? _skip;
    private WebApplication? _app;

    public async ValueTask InitializeAsync()
    {
        try
        {
            await using var probe = await new ConnectionFactory { Uri = new Uri(RabbitMq) }.CreateConnectionAsync();
        }
        catch (Exception ex) when (ex is RabbitMQ.Client.Exceptions.BrokerUnreachableException or System.Net.Sockets.SocketException)
        {
            _skip = $"RabbitMQ no alcanzable: {ex.Message}";
            return;
        }

        var server = Environment.GetEnvironmentVariable("ConnectionStrings__Core") ?? $"Host=127.0.0.1;Port=5432;Username={Environment.UserName}";
        _database = new NpgsqlConnectionStringBuilder(server) { Database = $"flit_svc_{Guid.NewGuid():N}"[..30] }.ConnectionString;
        try
        {
            await using var admin = new NpgsqlConnection(new NpgsqlConnectionStringBuilder(server) { Database = "postgres" }.ConnectionString);
            await admin.OpenAsync();
            await using var create = new NpgsqlCommand($"CREATE DATABASE \"{new NpgsqlConnectionStringBuilder(_database).Database}\"", admin);
            await create.ExecuteNonQueryAsync();
        }
        catch (NpgsqlException ex) when (!string.Equals(Environment.GetEnvironmentVariable("CI"), "true", StringComparison.OrdinalIgnoreCase))
        {
            _skip = $"Postgres no alcanzable: {ex.Message}";
            return;
        }

        await LimpiarColasAsync();
        _app = Program.Build([], b =>
        {
            b.WebHost.UseTestServer();
            b.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Servicio"] = _database,
                ["Platform:ServiceClient:ClientSecret"] = "secreto",
                ["Platform:ServiceClient:TokenEndpoint"] = "http://core-identity/connect/token",
                ["Platform:Auth:JwksUri"] = "http://core-identity/.well-known/jwks.json",
                ["Platform:Auth:Issuers:0"] = "https://hub.prueba/",
                ["Platform:Messaging:ConnectionString"] = RabbitMq,
                ["Smtp:UseConsoleWhenNoHost"] = "true",
                ["Notificaciones:Correo:EsperasReintento:0"] = "00:00:00.200",
                ["Notificaciones:Correo:EsperasReintento:1"] = "00:00:00.300",
                ["Notificaciones:Correo:EsperasReintento:2"] = "00:00:00.400",
                [ServicioSettings.GrpcPortKey] = "5994",
            });
            b.Services.AddSingleton<IFiltroDestinosWebhook>(new FiltroDePrueba());
            b.Services.AddHttpClient(TrabajoWebhookConsumer.ClienteHttp).ConfigurePrimaryHttpMessageHandler(() => _destino);
        });
        await Program.MigrateAsync(_app);
        await _app.StartAsync();
        await EsperarAsync(async () => await ConsumidoresAsync() == 1);
    }

    public async ValueTask DisposeAsync()
    {
        if (_app is not null)
            await _app.DisposeAsync();
        if (_skip is null)
        {
            await LimpiarColasAsync();
            NpgsqlConnection.ClearAllPools();
            await using var db = new NotificacionesDb(new DbContextOptionsBuilder<NotificacionesDb>().UseNpgsql(_database).Options);
            await db.Database.EnsureDeletedAsync();
        }
    }

    [Fact]
    public async Task AC1_ElWebhookSaleConElCuerpoExactoYLasCabecerasFirmadas()
    {
        Assert.SkipWhen(_skip is not null, _skip ?? string.Empty);
        const string cuerpo = """{"event_type":"procedure.state_changed","to_status":"aprobado"}""";
        var sobre = await PublicarAsync(Trabajo("https://ot.prueba/hook", cuerpo));

        await EsperarAsync(async () => await Registro().AnyAsync(w => w.TrabajoId == sobre.EventId));

        var pedido = _destino.Pedidos.Should().ContainSingle().Subject;
        pedido.Url.Should().Be("https://ot.prueba/hook");
        pedido.Cuerpo.Should().Be(cuerpo, "la firma es del cuerpo: no se re-serializa");
        pedido.Cabeceras["X-Webhook-Signature"].Should().Be("sha256=abc123");
        pedido.Cabeceras["X-Correlation-Id"].Should().Be("corr-ot-1");
        var w = await Registro().SingleAsync(x => x.TrabajoId == sobre.EventId, TestContext.Current.CancellationToken);
        w.Should().BeEquivalentTo(new { Resultado = "entregado", CodigoHttp = (int?)200, Origen = "ot", CorrelacionId = "corr-ot-1", TenantId = _empresa });
    }

    [Fact]
    public async Task AC2_UnDestinoInterno_SeBloquea_YSeRegistra_SinReintentos()
    {
        Assert.SkipWhen(_skip is not null, _skip ?? string.Empty);
        var sobre = await PublicarAsync(Trabajo("http://interno.prueba/metadata", "{}"));

        await EsperarAsync(async () => await Registro().AnyAsync(w => w.TrabajoId == sobre.EventId));
        await Task.Delay(1200, TestContext.Current.CancellationToken);

        (await Registro().SingleAsync(w => w.TrabajoId == sobre.EventId, TestContext.Current.CancellationToken)).Resultado.Should().Be("bloqueado");
        _destino.Pedidos.Should().BeEmpty();
        (await MensajesAsync($"{Cola}.dlq")).Should().Be(0);
    }

    [Fact]
    public async Task AC3_UnDestinoCaido_SeReintenta_YTerminaEnLaDlq()
    {
        Assert.SkipWhen(_skip is not null, _skip ?? string.Empty);
        _destino.Estado = HttpStatusCode.ServiceUnavailable;
        var sobre = await PublicarAsync(Trabajo("https://ot.prueba/caido", "{}"));

        await EsperarAsync(async () => await MensajesAsync($"{Cola}.dlq") == 1, TimeSpan.FromSeconds(20));

        var intentos = await Registro().Where(w => w.TrabajoId == sobre.EventId).ToListAsync(TestContext.Current.CancellationToken);
        intentos.Should().HaveCount(4).And.OnlyContain(w => w.Resultado == "fallido" && w.CodigoHttp == 503);
    }

    // ── Apoyo ────────────────────────────────────────────────────────────────────────────────────────────────────

    private static TrabajoWebhook Trabajo(string url, string cuerpo) => new(
        url, cuerpo, new Dictionary<string, string> { ["X-Webhook-Signature"] = "sha256=abc123", ["X-Correlation-Id"] = "corr-ot-1" },
        "ot", "procedure.state_changed");

    /// <summary>Publica como core-api: un trabajo con «tramites» de productor, en flit.notificaciones.</summary>
    private async Task<EventEnvelope> PublicarAsync(TrabajoWebhook trabajo)
    {
        var sobre = new EventEnvelope(Guid.CreateVersion7(), TrabajoWebhook.Tipo, 1, DateTimeOffset.UtcNow, _empresa, "tramites", "corr",
            System.Text.Json.JsonSerializer.SerializeToElement(trabajo, EventEnvelope.JsonOptions));
        await using var publicador = new RabbitMqEventPublisher(new PlatformMessagingOptions { Producer = "tramites", ConnectionString = RabbitMq });
        await publicador.PublishAsync(new OutboxMessage
        {
            Id = sobre.EventId, Exchange = "flit.notificaciones", RoutingKey = sobre.Type, Payload = sobre.ToJson(), OccurredAt = sobre.OccurredAt,
        }, TestContext.Current.CancellationToken);
        return sobre;
    }

    private IQueryable<EntregaWebhook> Registro() =>
        _app!.Services.CreateScope().ServiceProvider.GetRequiredService<NotificacionesDb>().Webhooks.AsNoTracking();

    private static async Task<uint> ConsumidoresAsync()
    {
        await using var conexion = await new ConnectionFactory { Uri = new Uri(RabbitMq) }.CreateConnectionAsync();
        await using var canal = await conexion.CreateChannelAsync();
        try { return await canal.ConsumerCountAsync(Cola); }
        catch (RabbitMQ.Client.Exceptions.OperationInterruptedException) { return 0; }
    }

    private static async Task<uint> MensajesAsync(string cola)
    {
        await using var conexion = await new ConnectionFactory { Uri = new Uri(RabbitMq) }.CreateConnectionAsync();
        await using var canal = await conexion.CreateChannelAsync();
        try { return await canal.MessageCountAsync(cola); }
        catch (RabbitMQ.Client.Exceptions.OperationInterruptedException) { return 0; }
    }

    private static async Task LimpiarColasAsync()
    {
        await using var conexion = await new ConnectionFactory { Uri = new Uri(RabbitMq) }.CreateConnectionAsync();
        await using var canal = await conexion.CreateChannelAsync();
        foreach (var cola in new[] { Cola, "notificaciones.email.send" })
        {
            foreach (var c in new[] { cola, $"{cola}.retry.1", $"{cola}.retry.2", $"{cola}.retry.3", $"{cola}.dlq" })
                await canal.QueueDeleteAsync(c);
            await canal.ExchangeDeleteAsync($"{cola}.reintentos");
        }
    }

    private static async Task EsperarAsync(Func<Task<bool>> condicion, TimeSpan? limite = null)
    {
        var reloj = Stopwatch.StartNew();
        while (!await condicion())
        {
            if (reloj.Elapsed > (limite ?? TimeSpan.FromSeconds(10)))
                throw new TimeoutException("La condición no se cumplió a tiempo.");
            await Task.Delay(100);
        }
    }

    private sealed class FiltroDePrueba : IFiltroDestinosWebhook
    {
        public Task<bool> PermitidoAsync(string url, CancellationToken ct) => Task.FromResult(!url.Contains("interno", StringComparison.Ordinal));
    }

    private sealed record Pedido(string Url, string Cuerpo, Dictionary<string, string> Cabeceras);

    /// <summary>El destino del webhook: guarda cada pedido y responde el estado configurado.</summary>
    private sealed class DestinoFalso : HttpMessageHandler
    {
        public HttpStatusCode Estado { get; set; } = HttpStatusCode.OK;

        public List<Pedido> Pedidos { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var cuerpo = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
            lock (Pedidos)
                Pedidos.Add(new Pedido(request.RequestUri!.ToString(), cuerpo, request.Headers.ToDictionary(h => h.Key, h => string.Join(",", h.Value))));
            return new HttpResponseMessage(Estado);
        }
    }
}
