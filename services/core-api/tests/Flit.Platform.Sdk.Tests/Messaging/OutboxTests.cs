using System.Text;
using System.Text.Json;
using Flit.Platform.Sdk.Messaging;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using RabbitMQ.Client;
using Xunit;

namespace Flit.Platform.Sdk.Tests.Messaging;

/// <summary>
/// HU #13338 (Epic #13316) — outbox del SDK contra Postgres y RabbitMQ reales: el evento sale solo si el cambio se
/// guardó (AC1), una transacción revertida no deja nada (AC2) y con el broker caído los eventos esperan y salen todos,
/// en orden, cuando vuelve (AC3).
/// </summary>
public sealed class OutboxTests(MessagingFixture fixture) : IClassFixture<MessagingFixture>, IAsyncLifetime
{
    private readonly Guid _tenant = Guid.NewGuid();
    private ServiceProvider _services = null!;
    private IConnection? _consumer;
    private IChannel? _channel;
    private string _queue = string.Empty;

    public async ValueTask InitializeAsync()
    {
        if (fixture.SkipReason is not null)
            return;

        _services = Services(fixture.RabbitMq);
        await using (var db = fixture.NewDb())
            await db.Set<OutboxMessage>().ExecuteDeleteAsync();

        // Un consumidor de prueba: cola propia atada a todo lo que publique flit.prueba.
        _consumer = await new ConnectionFactory { Uri = new Uri(fixture.RabbitMq) }.CreateConnectionAsync();
        _channel = await _consumer.CreateChannelAsync();
        await _channel.ExchangeDeclareAsync("flit.prueba", ExchangeType.Topic, durable: true, autoDelete: false);
        _queue = (await _channel.QueueDeclareAsync(string.Empty, durable: false, exclusive: true, autoDelete: true)).QueueName;
        await _channel.QueueBindAsync(_queue, "flit.prueba", "#");
    }

    public async ValueTask DisposeAsync()
    {
        if (_channel is not null)
            await _channel.DisposeAsync();
        if (_consumer is not null)
            await _consumer.DisposeAsync();
        if (_services is not null)
            await _services.DisposeAsync();
    }

    [Fact]
    public async Task AC1_ElCambioConfirmado_PublicaElSobreEnJson_YSeMarcaPublicado()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;

        EventEnvelope enviado;
        await using (var scope = _services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PruebaDb>();
            db.Pedidos.Add(new Pedido { Id = Guid.NewGuid(), TenantId = _tenant, Nombre = "uno" });
            enviado = scope.ServiceProvider.GetRequiredService<IPlatformOutbox>().Enqueue("prueba.pedido.creado", 1, _tenant, new { nombre = "uno" });
            await db.SaveChangesAsync(ct);
        }

        (await Publisher().PublishPendingAsync(ct)).Should().Be(1);

        var recibido = await _channel!.BasicGetAsync(_queue, autoAck: true, ct);
        recibido.Should().NotBeNull();
        recibido!.BasicProperties.MessageId.Should().Be(enviado.EventId.ToString());
        recibido.BasicProperties.Type.Should().Be("prueba.pedido.creado");
        recibido.BasicProperties.ContentType.Should().Be("application/json");
        recibido.BasicProperties.Persistent.Should().BeTrue();
        recibido.RoutingKey.Should().Be("prueba.pedido.creado");

        using var json = JsonDocument.Parse(Encoding.UTF8.GetString(recibido.Body.Span));
        var sobre = json.RootElement;
        sobre.GetProperty("eventId").GetGuid().Should().Be(enviado.EventId);
        sobre.GetProperty("type").GetString().Should().Be("prueba.pedido.creado");
        sobre.GetProperty("version").GetInt32().Should().Be(1);
        sobre.GetProperty("tenantId").GetGuid().Should().Be(_tenant);
        sobre.GetProperty("producer").GetString().Should().Be("prueba");
        sobre.GetProperty("correlationId").GetString().Should().Be("corr-pedido");
        sobre.GetProperty("data").GetProperty("nombre").GetString().Should().Be("uno");
        sobre.EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo(
            ["eventId", "type", "version", "occurredAt", "tenantId", "producer", "correlationId", "data"]);

        await using var check = fixture.NewDb();
        (await check.Set<OutboxMessage>().SingleAsync(m => m.Id == enviado.EventId, ct)).PublishedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task AC2_TransaccionRevertida_NoQuedaFilaNiEvento()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;

        await using (var scope = _services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PruebaDb>();
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            db.Pedidos.Add(new Pedido { Id = Guid.NewGuid(), TenantId = _tenant, Nombre = "revertido" });
            scope.ServiceProvider.GetRequiredService<IPlatformOutbox>().Enqueue("prueba.pedido.creado", 1, _tenant, new { nombre = "revertido" });
            await db.SaveChangesAsync(ct);
            await tx.RollbackAsync(ct);
        }

        await using (var check = fixture.NewDb())
            (await check.Set<OutboxMessage>().CountAsync(ct)).Should().Be(0);
        (await Publisher().PublishPendingAsync(ct)).Should().Be(0);
        (await _channel!.BasicGetAsync(_queue, autoAck: true, ct)).Should().BeNull();
    }

    [Fact]
    public async Task AC3_BrokerCaido_LosEventosEsperan_YSalenTodosEnOrdenCuandoVuelve()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;

        var ids = new List<Guid>();
        await using (var scope = _services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PruebaDb>();
            var outbox = scope.ServiceProvider.GetRequiredService<IPlatformOutbox>();
            for (var i = 0; i < 3; i++)
            {
                ids.Add(outbox.Enqueue("prueba.pedido.creado", 1, _tenant, new { orden = i }).EventId);
                await db.SaveChangesAsync(ct);
            }
        }

        // Broker caído: un puerto donde no escucha nadie.
        await using (var caido = new RabbitMqEventPublisher(Options("amqp://flit:flit-prueba@127.0.0.1:1/")))
            (await Publisher(caido).PublishPendingAsync(ct)).Should().Be(0);

        await using (var check = fixture.NewDb())
        {
            var pendientes = await check.Set<OutboxMessage>().Where(m => m.PublishedAt == null).ToListAsync(ct);
            pendientes.Should().HaveCount(3);
            pendientes.Single(m => m.Id == ids[0]).Attempts.Should().Be(1, "el primero falló y cortó el lote");
            pendientes.Single(m => m.Id == ids[0]).LastError.Should().NotBeNullOrEmpty();
        }

        // Vuelve el broker.
        (await Publisher().PublishPendingAsync(ct)).Should().Be(3);
        var recibidos = new List<Guid>();
        for (var i = 0; i < 3; i++)
        {
            var mensaje = await _channel!.BasicGetAsync(_queue, autoAck: true, ct);
            recibidos.Add(Guid.Parse(mensaje!.BasicProperties.MessageId!));
        }

        recibidos.Should().Equal(ids);
    }

    [Fact]
    public void ElTipoDebeSerDelProductor_YLaEmpresaEsObligatoria()
    {
        fixture.SkipIfUnavailable();
        using var scope = _services.CreateScope();
        var outbox = scope.ServiceProvider.GetRequiredService<IPlatformOutbox>();

        ((Action)(() => outbox.Enqueue("otro.pedido.creado", 1, _tenant, new { }))).Should().Throw<ArgumentException>();
        ((Action)(() => outbox.Enqueue("prueba.pedido.creado", 1, Guid.Empty, new { }))).Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData("", "amqp://h/")]
    [InlineData("Consultas", "amqp://h/")]
    [InlineData("consultas", "http://h/")]
    public void ConfiguracionInvalida_NoArranca(string producer, string uri)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Platform:Messaging:Producer"] = producer,
            ["Platform:Messaging:ConnectionString"] = uri,
        }).Build();
        var act = () => new ServiceCollection().AddFlitOutbox<PruebaDb>(config);
        act.Should().Throw<InvalidOperationException>();
    }

    private ServiceProvider Services(string rabbit)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<PruebaDb>(o => o.UseNpgsql(fixture.Postgres));
        services.AddFlitOutbox<PruebaDb>(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Platform:Messaging:Producer"] = MessagingFixture.Producer,
            ["Platform:Messaging:ConnectionString"] = rabbit,
        }).Build());
        var http = new DefaultHttpContext();
        http.Request.Headers["X-Correlation-Id"] = "corr-pedido";
        services.AddSingleton<IHttpContextAccessor>(new FixedHttpContextAccessor(http));
        return services.BuildServiceProvider();
    }

    private static PlatformMessagingOptions Options(string rabbit) => new() { Producer = MessagingFixture.Producer, ConnectionString = rabbit };

    private OutboxPublisherService<PruebaDb> Publisher(IEventPublisher? publisher = null) =>
        new(_services.GetRequiredService<Microsoft.Extensions.DependencyInjection.IServiceScopeFactory>(),
            publisher ?? _services.GetRequiredService<IEventPublisher>(),
            _services.GetRequiredService<PlatformMessagingOptions>(),
            TimeProvider.System,
            NullLogger<OutboxPublisherService<PruebaDb>>.Instance);

    /// <summary>HttpContextAccessor guarda el contexto en un AsyncLocal: aquí se fija para todo el proveedor.</summary>
    private sealed class FixedHttpContextAccessor(HttpContext context) : IHttpContextAccessor
    {
        public HttpContext? HttpContext { get => context; set { } }
    }
}
