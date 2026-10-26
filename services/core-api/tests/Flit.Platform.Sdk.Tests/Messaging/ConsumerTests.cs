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
    public async Task HU13359_UnaFallaSinReintentoAutomatico_VaDirectoALaDlq_ConSuMotivo_YSePuedeReintentar()
    {
        fixture.SkipIfUnavailable();
        var evento = Sobre();
        _comportamiento.FallarSinReintento(evento.EventId);

        await PublicarAsync(evento);
        await Eventually(async () => (await _channel!.MessageCountAsync($"{_cola}.dlq")) == 1);

        _comportamiento.Intentos(evento.EventId).Should().Be(1, "no pasa por las esperas de reintento");
        var muerto = await _channel!.BasicGetAsync($"{_cola}.dlq", autoAck: false, TestContext.Current.CancellationToken);
        Encoding.UTF8.GetString(muerto!.BasicProperties.Headers!["x-flit-dead-letter-reason"] as byte[] ?? []).Should().Be(nameof(SinReintentoAutomaticoException));
        await _channel.BasicNackAsync(muerto.DeliveryTag, multiple: false, requeue: true, TestContext.Current.CancellationToken);

        // Corregida la causa, el reintento manual lo procesa.
        _comportamiento.FallarLasPrimeras(evento.EventId, 0);
        await using var muertos = new PlatformDeadLetters(new PlatformMessagingOptions { ConnectionString = fixture.RabbitMq });
        (await muertos.RetryAsync(_cola, evento.EventId.ToString(), TestContext.Current.CancellationToken)).Should().BeTrue();
        await Eventually(async () => await EfectosAsync(evento.EventId) == 1);
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
    public async Task HU13357_UnMensajeMuerto_SeListaConSuError_YAlReintentarlo_VuelveASuColaYSeProcesa()
    {
        fixture.SkipIfUnavailable();
        var evento = Sobre();
        _comportamiento.FallarSiempre(evento.EventId);
        await PublicarAsync(evento);
        await Eventually(async () => (await _channel!.MessageCountAsync($"{_cola}.dlq")) == 1, TimeSpan.FromSeconds(20));
        await using var muertos = new PlatformDeadLetters(new PlatformMessagingOptions { Producer = Productor, ConnectionString = fixture.RabbitMq });

        var lista = await muertos.ListAsync(_cola, 50, TestContext.Current.CancellationToken);

        var muerto = lista.Should().ContainSingle().Subject;
        muerto.MessageId.Should().Be(evento.EventId.ToString());
        muerto.Type.Should().Be(Tipo);
        muerto.TenantId.Should().Be(evento.TenantId);
        muerto.Reason.Should().Be(nameof(InvalidOperationException));
        muerto.Error.Should().Be("Efecto que falla a propósito.");
        muerto.DeadLetteredAt.Should().NotBeNull();
        (await _channel!.MessageCountAsync($"{_cola}.dlq", TestContext.Current.CancellationToken)).Should().Be(1u, "listar no saca el mensaje");

        _comportamiento.FallarLasPrimeras(evento.EventId, 0);
        (await muertos.RetryAsync(_cola, evento.EventId.ToString(), TestContext.Current.CancellationToken)).Should().BeTrue();

        await Eventually(async () => await EfectosAsync(evento.EventId) == 1);
        (await _channel!.MessageCountAsync($"{_cola}.dlq", TestContext.Current.CancellationToken)).Should().Be(0u);
    }

    [Fact]
    public async Task HU13357_UnMensajeMuertoDescartado_SaleDeLaDlq_SinProcesarse()
    {
        fixture.SkipIfUnavailable();
        var evento = Sobre();
        _comportamiento.FallarSiempre(evento.EventId);
        await PublicarAsync(evento);
        await Eventually(async () => (await _channel!.MessageCountAsync($"{_cola}.dlq")) == 1, TimeSpan.FromSeconds(20));
        await using var muertos = new PlatformDeadLetters(new PlatformMessagingOptions { Producer = Productor, ConnectionString = fixture.RabbitMq });

        (await muertos.DiscardAsync(_cola, "otro-id", TestContext.Current.CancellationToken)).Should().BeFalse();
        (await muertos.DiscardAsync(_cola, evento.EventId.ToString(), TestContext.Current.CancellationToken)).Should().BeTrue();

        (await _channel!.MessageCountAsync($"{_cola}.dlq", TestContext.Current.CancellationToken)).Should().Be(0u);
        (await EfectosAsync(evento.EventId)).Should().Be(0);
    }

    [Fact]
    public void LasEsperasPorDefecto_SonLasDelAdr()
    {
        new PlatformConsumerOptions().RetryDelays.Should().Equal(TimeSpan.FromSeconds(10), TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(10));
    }

    // ── HU #13727 (ADR-0075, D1): mensajes que el consumidor atiende a la vez ─────────────────────────────────────

    [Fact]
    public async Task HU13727_ConConcurrencia2_DosEfectosEntranALaVez()
    {
        fixture.SkipIfUnavailable();
        var barrera = new Barrera(esperados: 2, tope: TimeSpan.FromSeconds(10));
        await using var consumidor = await ConsumidorExtra.ArrancarAsync<EfectoConBarrera>(
            fixture, Config(), TipoConcurrente, s => s.AddSingleton(barrera), o =>
            {
                o.Concurrencia = 2;
                o.Prefetch = 2;
            });

        await PublicarAsync(Sobre(TipoConcurrente));
        await PublicarAsync(Sobre(TipoConcurrente));
        await Eventually(() => Task.FromResult(barrera.Salidas == 2), TimeSpan.FromSeconds(15));

        barrera.Maximo.Should().Be(2, "con Concurrencia=2 los dos efectos están dentro a la vez");
    }

    [Fact]
    public async Task HU13727_PorDefecto_UnSoloEfectoALaVez()
    {
        fixture.SkipIfUnavailable();
        var barrera = new Barrera(esperados: 2, tope: TimeSpan.FromSeconds(1));
        await using var consumidor = await ConsumidorExtra.ArrancarAsync<EfectoConBarrera>(
            fixture, Config(), TipoConcurrente, s => s.AddSingleton(barrera), o => o.Prefetch = 10);

        await PublicarAsync(Sobre(TipoConcurrente));
        await PublicarAsync(Sobre(TipoConcurrente));
        await Eventually(() => Task.FromResult(barrera.Salidas == 2), TimeSpan.FromSeconds(15));

        barrera.Maximo.Should().Be(1, "sin Concurrencia el consumidor atiende un mensaje a la vez aunque tenga prefetch");
    }

    [Fact]
    public void HU13727_LaConcurrenciaPorDefecto_EsUno_YLlegaALaConexion()
    {
        new PlatformConsumerOptions().Concurrencia.Should().Be(1);

        var opciones = new PlatformConsumerOptions { Queue = "prueba.fabrica", Producer = Productor, Concurrencia = 4, Prefetch = 8 };
        Consumidor(opciones).Fabrica().ConsumerDispatchConcurrency.Should().Be(4);
        Consumidor(new PlatformConsumerOptions { Queue = "prueba.fabrica", Producer = Productor }).Fabrica().ConsumerDispatchConcurrency.Should().Be(1);
    }

    [Theory]
    [InlineData(0, 10)]
    [InlineData(4, 2)]
    public void HU13727_ConcurrenciaCero_OPrefetchMenorQueLaConcurrencia_NoRegistra(int concurrencia, int prefetch)
    {
        var registrar = () => new ServiceCollection().AddFlitConsumer<PruebaDb, EfectoDePrueba, PedidoCreado>(
            Config(), "prueba.invalida", Productor, [Tipo], o =>
            {
                o.Concurrencia = (ushort)concurrencia;
                o.Prefetch = (ushort)prefetch;
            });

        registrar.Should().Throw<InvalidOperationException>().WithMessage("*Concurrencia*");
    }

    // ── Apoyo ────────────────────────────────────────────────────────────────────────────────────────────────────

    private const string TipoConcurrente = "prueba-consumo.pedido.concurrente";

    private PlatformConsumer<PruebaDb, EfectoDePrueba, PedidoCreado> Consumidor(PlatformConsumerOptions opciones) => new(
        new ServiceCollection().BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
        opciones,
        new PlatformMessagingOptions { Producer = "consumidor-prueba", ConnectionString = fixture.RabbitMq },
        TimeProvider.System,
        Microsoft.Extensions.Logging.Abstractions.NullLogger<PlatformConsumer<PruebaDb, EfectoDePrueba, PedidoCreado>>.Instance);

    private IConfiguration Config() => new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["Platform:Messaging:Producer"] = "consumidor-prueba",
        ["Platform:Messaging:ConnectionString"] = fixture.RabbitMq,
    }).Build();

    private static EventEnvelope Sobre(string tipo = Tipo, string productor = Productor) => new(
        Guid.CreateVersion7(), tipo, 1, DateTimeOffset.UtcNow, Guid.NewGuid(), productor, "corr-consumo",
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

    /// <summary>Falla con <see cref="SinReintentoAutomaticoException"/> hasta que se cambie el comportamiento.</summary>
    public void FallarSinReintento(Guid eventId) => _fallos[eventId] = SinReintento;

    internal const int SinReintento = int.MinValue;

    internal bool SinReintentoPara(Guid eventId) => _fallos.TryGetValue(eventId, out var r) && r == SinReintento;

    public int Intentos(Guid eventId) => _momentos.TryGetValue(eventId, out var q) ? q.Count : 0;

    public IReadOnlyList<DateTimeOffset> Momentos(Guid eventId) => _momentos.TryGetValue(eventId, out var q) ? [.. q] : [];

    /// <summary>Registra el intento y dice si el efecto debe salir bien.</summary>
    internal bool Intentar(Guid eventId)
    {
        _momentos.GetOrAdd(eventId, _ => new ConcurrentQueue<DateTimeOffset>()).Enqueue(DateTimeOffset.UtcNow);
        if (!_fallos.TryGetValue(eventId, out var restantes) || restantes == 0)
            return true;
        if (restantes is not (int.MaxValue or Comportamiento.SinReintento))
            _fallos[eventId] = restantes - 1;
        return false;
    }
}

/// <summary>Efecto de prueba: una fila en prueba.efectos, en la transacción de la bandeja.</summary>
public sealed class EfectoDePrueba(PruebaDb db, Comportamiento comportamiento) : IEventConsumer<PedidoCreado>
{
    public Task HandleAsync(EventEnvelope envelope, PedidoCreado data, CancellationToken ct)
    {
        var sinReintento = comportamiento.SinReintentoPara(envelope.EventId);
        if (!comportamiento.Intentar(envelope.EventId))
        {
            if (sinReintento)
                throw new SinReintentoAutomaticoException("Falla que reintentar no arregla.");
            throw new InvalidOperationException("Efecto que falla a propósito.");
        }

        db.Set<Efecto>().Add(new Efecto { Id = Guid.NewGuid(), EventId = envelope.EventId, Cola = envelope.Type });
        return Task.CompletedTask;
    }
}

/// <summary>
/// Un consumidor más, con su cola, su efecto y sus opciones (HU #13727, ADR-0075): para probar la concurrencia y los productores
/// permitidos sin tocar el de la clase. Al cerrarse detiene el consumidor y borra su topología.
/// </summary>
internal sealed class ConsumidorExtra : IAsyncDisposable
{
    private readonly MessagingFixture _fixture;
    private readonly ServiceProvider _services;

    private ConsumidorExtra(MessagingFixture fixture, ServiceProvider services, string cola)
    {
        _fixture = fixture;
        _services = services;
        Cola = cola;
    }

    public string Cola { get; }

    public static async Task<ConsumidorExtra> ArrancarAsync<THandler>(
        MessagingFixture fixture, IConfiguration config, string tipo, Action<IServiceCollection> registrar, Action<PlatformConsumerOptions> opciones)
        where THandler : class, IEventConsumer<PedidoCreado>
    {
        var cola = $"prueba.extra{Guid.NewGuid():N}"[..30];
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<PruebaDb>(o => o.UseNpgsql(fixture.Postgres));
        registrar(services);
        services.AddFlitConsumer<PruebaDb, THandler, PedidoCreado>(config, cola, "prueba-consumo", [tipo], o =>
        {
            o.RetryDelays.Clear();
            o.RetryDelays.Add(TimeSpan.FromMilliseconds(200));
            opciones(o);
        });
        var provider = services.BuildServiceProvider();
        foreach (var hosted in provider.GetServices<IHostedService>())
            await hosted.StartAsync(TestContext.Current.CancellationToken);

        var extra = new ConsumidorExtra(fixture, provider, cola);
        await using var admin = await new ConnectionFactory { Uri = new Uri(fixture.RabbitMq) }.CreateConnectionAsync();
        var limite = Stopwatch.StartNew();
        while (true)
        {
            await using var probe = await admin.CreateChannelAsync();
            try
            {
                if (await probe.ConsumerCountAsync(cola) == 1)
                    return extra;
            }
            catch (RabbitMQ.Client.Exceptions.OperationInterruptedException)
            {
                // La cola todavía no existe.
            }

            if (limite.Elapsed > TimeSpan.FromSeconds(10))
                throw new TimeoutException("El consumidor extra no quedó escuchando.");
            await Task.Delay(50);
        }
    }

    /// <summary>Mensajes en la DLQ de este consumidor.</summary>
    public async Task<uint> MuertosAsync()
    {
        await using var conexion = await new ConnectionFactory { Uri = new Uri(_fixture.RabbitMq) }.CreateConnectionAsync();
        await using var canal = await conexion.CreateChannelAsync();
        return await canal.MessageCountAsync($"{Cola}.dlq");
    }

    /// <summary>Motivo del primer mensaje de la DLQ (lo saca).</summary>
    public async Task<string?> MotivoDelMuertoAsync()
    {
        await using var conexion = await new ConnectionFactory { Uri = new Uri(_fixture.RabbitMq) }.CreateConnectionAsync();
        await using var canal = await conexion.CreateChannelAsync();
        var muerto = await canal.BasicGetAsync($"{Cola}.dlq", autoAck: true);
        return muerto?.BasicProperties.Headers?[PlatformDeadLetters.ReasonHeader] is byte[] motivo ? Encoding.UTF8.GetString(motivo) : null;
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var hosted in _services.GetServices<IHostedService>())
            await hosted.StopAsync(CancellationToken.None);
        await _services.DisposeAsync();
        await using var conexion = await new ConnectionFactory { Uri = new Uri(_fixture.RabbitMq) }.CreateConnectionAsync();
        await using var canal = await conexion.CreateChannelAsync();
        foreach (var cola in new[] { Cola, $"{Cola}.retry.1", $"{Cola}.dlq" })
            await canal.QueueDeleteAsync(cola);
        await canal.ExchangeDeleteAsync($"{Cola}.reintentos");
    }
}

/// <summary>Cuántos efectos hay dentro a la vez: cada uno espera a que entren <c>esperados</c> o a que venza el tope.</summary>
public sealed class Barrera(int esperados, TimeSpan tope)
{
    private int _dentro;
    private int _maximo;
    private int _salidas;

    public int Maximo => Volatile.Read(ref _maximo);

    public int Salidas => Volatile.Read(ref _salidas);

    public async Task EntrarAsync(CancellationToken ct)
    {
        var dentro = Interlocked.Increment(ref _dentro);
        int previo;
        while ((previo = Volatile.Read(ref _maximo)) < dentro && Interlocked.CompareExchange(ref _maximo, dentro, previo) != previo)
        {
        }

        var limite = Stopwatch.StartNew();
        while (Volatile.Read(ref _dentro) < esperados && limite.Elapsed < tope)
            await Task.Delay(10, ct);
        Interlocked.Decrement(ref _dentro);
        Interlocked.Increment(ref _salidas);
    }
}

/// <summary>Efecto de prueba que pasa por la <see cref="Barrera"/> antes de dejar su fila.</summary>
public sealed class EfectoConBarrera(PruebaDb db, Barrera barrera) : IEventConsumer<PedidoCreado>
{
    public async Task HandleAsync(EventEnvelope envelope, PedidoCreado data, CancellationToken ct)
    {
        await barrera.EntrarAsync(ct);
        db.Set<Efecto>().Add(new Efecto { Id = Guid.NewGuid(), EventId = envelope.EventId, Cola = envelope.Type });
    }
}
