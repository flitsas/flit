using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Text;
using Flit.Platform.Sdk.Messaging;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using RabbitMQ.Client;
using Xunit;

namespace Flit.Platform.Sdk.Tests.Messaging;

/// <summary>
/// HU #13339 (Epic #13316) — consumidor base del SDK contra Postgres y RabbitMQ reales: idempotencia por la bandeja
/// (AC1), reintentos con espera y DLQ con métrica (AC2). Cada prueba usa su propia cola y esperas cortas.
/// </summary>
public sealed class ConsumerTests(MessagingFixture fixture) : IClassFixture<MessagingFixture>, IAsyncLifetime
{
    // Productor propio: las pruebas de la outbox escuchan todo flit.prueba y corren en paralelo.
    private const string Productor = "prueba-consumo";
    private const string Tipo = "prueba-consumo.pedido.creado";

    private static readonly TimeSpan[] Esperas = [TimeSpan.FromMilliseconds(200), TimeSpan.FromMilliseconds(400), TimeSpan.FromMilliseconds(600)];

    private readonly string _cola = $"prueba.consumo{Guid.NewGuid():N}"[..30];
    private readonly Comportamiento _comportamiento = new();
    private ServiceProvider? _services;
    private IConnection? _admin;
    private IChannel? _channel;

    public async ValueTask InitializeAsync()
    {
        if (fixture.SkipReason is not null)
            return;

        await using (var db = fixture.NewDb())
            await db.Database.EnsureCreatedAsync();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<PruebaDb>(o => o.UseNpgsql(fixture.Postgres));
        services.AddSingleton(_comportamiento);
        services.AddFlitConsumer<PruebaDb, EfectoDePrueba, PedidoCreado>(Config(), _cola, Productor, [Tipo], o =>
        {
            o.RetryDelays.Clear();
            foreach (var espera in Esperas)
                o.RetryDelays.Add(espera);
        });
        _services = services.BuildServiceProvider();
        foreach (var hosted in _services.GetServices<IHostedService>())
            await hosted.StartAsync(TestContext.Current.CancellationToken);

        _admin = await new ConnectionFactory { Uri = new Uri(fixture.RabbitMq) }.CreateConnectionAsync();
        // El consumidor arranca en segundo plano: se espera a que declare su cola y quede escuchando. Preguntar por una
        // cola que aún no existe cierra el canal (404), así que cada intento usa uno desechable.
        await Eventually(async () =>
        {
            await using var probe = await _admin.CreateChannelAsync();
            try
            {
                return await probe.ConsumerCountAsync(_cola) == 1;
            }
            catch (RabbitMQ.Client.Exceptions.OperationInterruptedException)
            {
                return false;
            }
        });
        _channel = await _admin.CreateChannelAsync();
    }

    public async ValueTask DisposeAsync()
    {
        if (_services is not null)
        {
            foreach (var hosted in _services.GetServices<IHostedService>())
                await hosted.StopAsync(CancellationToken.None);
            await _services.DisposeAsync();
        }

        if (fixture.SkipReason is null)
        {
            // Conexión propia: la limpieza tiene que correr aunque el arranque haya fallado a medias.
            await using var cleanup = await new ConnectionFactory { Uri = new Uri(fixture.RabbitMq) }.CreateConnectionAsync();
            await using var channel = await cleanup.CreateChannelAsync();
            foreach (var cola in new[] { _cola, $"{_cola}.retry.1", $"{_cola}.retry.2", $"{_cola}.retry.3", $"{_cola}.dlq" })
                await channel.QueueDeleteAsync(cola);
            await channel.ExchangeDeleteAsync($"{_cola}.reintentos");
        }

        if (_channel is not null)
            await _channel.DisposeAsync();

        if (_admin is not null)
            await _admin.DisposeAsync();
    }

    [Fact]
    public async Task AC1_UnEventoYaProcesado_SeConfirmaSinRepetirElEfecto()
    {
        fixture.SkipIfUnavailable();
        var evento = Sobre();

        await PublicarAsync(evento);
        await Eventually(async () => await EfectosAsync(evento.EventId) == 1);
        await PublicarAsync(evento); // llega de nuevo (reentrega, publicación duplicada)
        await Eventually(async () => _comportamiento.Intentos(evento.EventId) == 1 && (await _channel!.MessageCountAsync(_cola)) == 0);
        await Task.Delay(500, TestContext.Current.CancellationToken);

        (await EfectosAsync(evento.EventId)).Should().Be(1);
        _comportamiento.Intentos(evento.EventId).Should().Be(1, "el segundo no llegó al efecto");
        (await _channel!.MessageCountAsync($"{_cola}.dlq", TestContext.Current.CancellationToken)).Should().Be(0);
        await using var db = fixture.NewDb();
        (await db.Set<InboxMessage>().CountAsync(m => m.EventId == evento.EventId && m.Consumer == _cola, TestContext.Current.CancellationToken)).Should().Be(1);
    }

    [Fact]
    public async Task AC2_UnEfectoQueFallaSiempre_SeReintentaConEspera_YTerminaEnLaDlqConMetrica()
    {
        fixture.SkipIfUnavailable();
        var evento = Sobre();
        _comportamiento.FallarSiempre(evento.EventId);
        var muertos = new ConcurrentBag<(long Valor, string? Cola, string? Tipo)>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == PlatformMessagingMetrics.MeterName && instrument.Name == PlatformMessagingMetrics.DeadLettered)
                l.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<long>((_, valor, tags, _) =>
        {
            string? cola = null, tipo = null;
            foreach (var tag in tags)
            {
                if (tag.Key == "queue") cola = tag.Value as string;
                if (tag.Key == "type") tipo = tag.Value as string;
            }

            muertos.Add((valor, cola, tipo));
        });
        listener.Start();

        await PublicarAsync(evento);
        await Eventually(async () => (await _channel!.MessageCountAsync($"{_cola}.dlq")) == 1, TimeSpan.FromSeconds(20));

        var intentos = _comportamiento.Momentos(evento.EventId);
        intentos.Should().HaveCount(4, "el primero y uno por cada espera");
        for (var i = 0; i < Esperas.Length; i++)
            (intentos[i + 1] - intentos[i]).Should().BeGreaterThanOrEqualTo(Esperas[i] - TimeSpan.FromMilliseconds(50));

        var muerto = await _channel!.BasicGetAsync($"{_cola}.dlq", autoAck: true, TestContext.Current.CancellationToken);
        muerto!.BasicProperties.MessageId.Should().Be(evento.EventId.ToString());
        Convert.ToInt32(muerto.BasicProperties.Headers!["x-flit-attempt"], System.Globalization.CultureInfo.InvariantCulture).Should().Be(3);
        muertos.Should().ContainSingle(m => m.Cola == _cola).Which.Should().Be((1L, _cola, Tipo));
        (await EfectosAsync(evento.EventId)).Should().Be(0);
    }

    [Fact]
    public async Task UnFalloPasajero_SeReintenta_YElEfectoQuedaUnaVez()
    {
        fixture.SkipIfUnavailable();
        var evento = Sobre();
        _comportamiento.FallarLasPrimeras(evento.EventId, 1);

        await PublicarAsync(evento);
        await Eventually(async () => await EfectosAsync(evento.EventId) == 1);

        _comportamiento.Intentos(evento.EventId).Should().Be(2);
        (await _channel!.MessageCountAsync($"{_cola}.dlq", TestContext.Current.CancellationToken)).Should().Be(0);
    }

    [Fact]
    public async Task UnMensajeQueNoEsUnSobre_VaDirectoALaDlq()
    {
        fixture.SkipIfUnavailable();
        await _channel!.BasicPublishAsync(EventEnvelope.ExchangeFor(Productor), Tipo, Encoding.UTF8.GetBytes("esto no es json"), TestContext.Current.CancellationToken);

        await Eventually(async () => (await _channel!.MessageCountAsync($"{_cola}.dlq")) == 1);
        var muerto = await _channel!.BasicGetAsync($"{_cola}.dlq", autoAck: true, TestContext.Current.CancellationToken);
        Encoding.UTF8.GetString(muerto!.BasicProperties.Headers!["x-flit-dead-letter-reason"] as byte[] ?? []).Should().Be("poison");
    }

    [Fact]
    public void LasEsperasPorDefecto_SonLasDelAdr()
    {
        new PlatformConsumerOptions().RetryDelays.Should().Equal(TimeSpan.FromSeconds(10), TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(10));
    }

    // ── Apoyo ────────────────────────────────────────────────────────────────────────────────────────────────────

    private IConfiguration Config() => new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["Platform:Messaging:Producer"] = "consumidor-prueba",
        ["Platform:Messaging:ConnectionString"] = fixture.RabbitMq,
    }).Build();

    private static EventEnvelope Sobre() => new(
        Guid.CreateVersion7(), Tipo, 1, DateTimeOffset.UtcNow, Guid.NewGuid(), Productor, "corr-consumo",
        System.Text.Json.JsonSerializer.SerializeToElement(new PedidoCreado("uno"), EventEnvelope.JsonOptions));

    private async Task PublicarAsync(EventEnvelope evento)
    {
        await using var publisher = new RabbitMqEventPublisher(new PlatformMessagingOptions { Producer = Productor, ConnectionString = fixture.RabbitMq });
        await publisher.PublishAsync(new OutboxMessage
        {
            Id = evento.EventId,
            Exchange = EventEnvelope.ExchangeFor(Productor),
            RoutingKey = evento.Type,
            Payload = evento.ToJson(),
            OccurredAt = evento.OccurredAt,
        }, TestContext.Current.CancellationToken);
    }

    private async Task<int> EfectosAsync(Guid eventId)
    {
        await using var db = fixture.NewDb();
        return await db.Set<Efecto>().CountAsync(e => e.EventId == eventId);
    }

    private static async Task Eventually(Func<Task<bool>> condition, TimeSpan? timeout = null)
    {
        var limit = Stopwatch.StartNew();
        while (!await condition())
        {
            if (limit.Elapsed > (timeout ?? TimeSpan.FromSeconds(10)))
                throw new TimeoutException("La condición no se cumplió a tiempo.");
            await Task.Delay(50);
        }
    }
}

public sealed record PedidoCreado(string Nombre);

/// <summary>Qué hace el efecto de prueba con cada evento, y cuándo lo intentó.</summary>
public sealed class Comportamiento
{
    private readonly ConcurrentDictionary<Guid, ConcurrentQueue<DateTimeOffset>> _momentos = new();
    private readonly ConcurrentDictionary<Guid, int> _fallos = new();

    public void FallarSiempre(Guid eventId) => _fallos[eventId] = int.MaxValue;

    public void FallarLasPrimeras(Guid eventId, int n) => _fallos[eventId] = n;

    public int Intentos(Guid eventId) => _momentos.TryGetValue(eventId, out var q) ? q.Count : 0;

    public IReadOnlyList<DateTimeOffset> Momentos(Guid eventId) => _momentos.TryGetValue(eventId, out var q) ? [.. q] : [];

    /// <summary>Registra el intento y dice si el efecto debe salir bien.</summary>
    internal bool Intentar(Guid eventId)
    {
        _momentos.GetOrAdd(eventId, _ => new ConcurrentQueue<DateTimeOffset>()).Enqueue(DateTimeOffset.UtcNow);
        if (!_fallos.TryGetValue(eventId, out var restantes) || restantes == 0)
            return true;
        if (restantes != int.MaxValue)
            _fallos[eventId] = restantes - 1;
        return false;
    }
}

/// <summary>Efecto de prueba: una fila en prueba.efectos, en la transacción de la bandeja.</summary>
public sealed class EfectoDePrueba(PruebaDb db, Comportamiento comportamiento) : IEventConsumer<PedidoCreado>
{
    public Task HandleAsync(EventEnvelope envelope, PedidoCreado data, CancellationToken ct)
    {
        if (!comportamiento.Intentar(envelope.EventId))
            throw new InvalidOperationException("Efecto que falla a propósito.");

        db.Set<Efecto>().Add(new Efecto { Id = Guid.NewGuid(), EventId = envelope.EventId, Cola = envelope.Type });
        return Task.CompletedTask;
    }
}
