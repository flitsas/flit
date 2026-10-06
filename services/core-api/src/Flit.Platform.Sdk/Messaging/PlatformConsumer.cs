using System.Diagnostics.Metrics;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace Flit.Platform.Sdk.Messaging;

/// <summary>El efecto de un consumidor. Se resuelve por mensaje en su propio scope (mismo <c>DbContext</c> que la bandeja).</summary>
public interface IEventConsumer<TData>
{
    Task HandleAsync(EventEnvelope envelope, TData data, CancellationToken ct);
}

/// <summary>Una suscripción: la cola del consumidor y los eventos que recibe.</summary>
public sealed class PlatformConsumerOptions
{
    /// <summary><c>&lt;consumidor&gt;.&lt;propósito&gt;</c> (contrato §7), p. ej. <c>notificaciones.correo</c>.</summary>
    public string Queue { get; set; } = string.Empty;

    /// <summary>Productor cuyos eventos se consumen: exchange <c>flit.&lt;productor&gt;</c>.</summary>
    public string Producer { get; set; } = string.Empty;

    /// <summary>Tipos de evento (llaves de ruteo) que entran a la cola.</summary>
    public IList<string> EventTypes { get; } = [];

    /// <summary>Espera antes de cada reintento (ADR-0064: 10 s, 1 min y 10 min). Después, a <c>&lt;cola&gt;.dlq</c>.</summary>
    public IList<TimeSpan> RetryDelays { get; } = [TimeSpan.FromSeconds(10), TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(10)];

    /// <summary>Mensajes en vuelo por consumidor.</summary>
    public ushort Prefetch { get; set; } = 10;
}

/// <summary>Métricas del bus (OpenTelemetry las exporta si está encendido): la de mensajes muertos es la de alertar.</summary>
public static class PlatformMessagingMetrics
{
    public const string MeterName = "Flit.Platform.Messaging";
    public const string DeadLettered = "flit.messaging.dead_lettered";

    internal static readonly Meter Meter = new(MeterName);

    internal static readonly Counter<long> DeadLetteredCounter =
        Meter.CreateCounter<long>(DeadLettered, description: "Mensajes que agotaron los reintentos y quedaron en <cola>.dlq");
}

/// <summary>
/// Consumidor base del SDK (HU #13339, ADR-0064). Solo hay que escribir el efecto (<see cref="IEventConsumer{TData}"/>):
/// <list type="bullet">
///   <item>Topología declarada al arrancar, idempotente: cola durable <c>&lt;cola&gt;</c> atada al exchange del productor,
///   una cola de espera por reintento (<c>&lt;cola&gt;.retry.&lt;n&gt;</c>, con TTL que la devuelve a la cola principal) y
///   <c>&lt;cola&gt;.dlq</c>. Sin plugins del broker.</item>
///   <item>Bandeja de entrada: un <c>eventId</c> ya procesado por esta cola se confirma sin repetir el efecto (AC1).</item>
///   <item>Si el efecto falla, el mensaje va a la siguiente cola de espera; agotadas las esperas, a la DLQ y suma la
///   métrica <see cref="PlatformMessagingMetrics.DeadLettered"/> (AC2). Un mensaje que no es un sobre válido va
///   directo a la DLQ.</item>
/// </list>
/// </summary>
internal sealed class PlatformConsumer<TContext, THandler, TData>(
    IServiceScopeFactory scopes,
    PlatformConsumerOptions consumer,
    PlatformMessagingOptions messaging,
    TimeProvider time,
    ILogger<PlatformConsumer<TContext, THandler, TData>> logger) : BackgroundService
    where TContext : DbContext
    where THandler : class, IEventConsumer<TData>
{
    internal const string AttemptHeader = "x-flit-attempt";

    private IConnection? _connection;
    private IChannel? _channel;

    internal string RetryQueue(int index) => $"{consumer.Queue}.retry.{index + 1}";

    internal string DeadLetterQueue => $"{consumer.Queue}.dlq";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await StartAsync().ConfigureAwait(false);
                return; // el consumidor queda escuchando; la recuperación automática del cliente atiende las caídas
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                ConsumerLog.StartFailed(logger, consumer.Queue, ex);
                await DisposeConnectionAsync().ConfigureAwait(false);
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(5), time, stoppingToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }

        async Task StartAsync()
        {
            var factory = new ConnectionFactory
            {
                Uri = new Uri(messaging.ConnectionString),
                ClientProvidedName = $"flit-{consumer.Queue}",
                AutomaticRecoveryEnabled = true,
                TopologyRecoveryEnabled = true,
            };
            _connection = await factory.CreateConnectionAsync(stoppingToken).ConfigureAwait(false);
            _channel = await _connection.CreateChannelAsync(cancellationToken: stoppingToken).ConfigureAwait(false);
            await DeclareTopologyAsync(_channel, stoppingToken).ConfigureAwait(false);
            await _channel.BasicQosAsync(0, consumer.Prefetch, global: false, stoppingToken).ConfigureAwait(false);

            var listener = new AsyncEventingBasicConsumer(_channel);
            listener.ReceivedAsync += (_, delivery) => OnReceivedAsync(_channel, delivery, stoppingToken);
            await _channel.BasicConsumeAsync(consumer.Queue, autoAck: false, listener, stoppingToken).ConfigureAwait(false);
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken).ConfigureAwait(false);
        await DisposeConnectionAsync().ConfigureAwait(false);
    }

    internal async Task DeclareTopologyAsync(IChannel channel, CancellationToken ct)
    {
        var exchange = EventEnvelope.ExchangeFor(consumer.Producer);
        await channel.ExchangeDeclareAsync(exchange, ExchangeType.Topic, durable: true, autoDelete: false, cancellationToken: ct).ConfigureAwait(false);
        await channel.QueueDeclareAsync(consumer.Queue, durable: true, exclusive: false, autoDelete: false, cancellationToken: ct).ConfigureAwait(false);
        foreach (var type in consumer.EventTypes)
            await channel.QueueBindAsync(consumer.Queue, exchange, type, cancellationToken: ct).ConfigureAwait(false);

        for (var i = 0; i < consumer.RetryDelays.Count; i++)
        {
            // Al vencer el TTL, el broker devuelve el mensaje a la cola principal por el exchange por defecto.
            await channel.QueueDeclareAsync(RetryQueue(i), durable: true, exclusive: false, autoDelete: false,
                new Dictionary<string, object?>
                {
                    ["x-message-ttl"] = (long)consumer.RetryDelays[i].TotalMilliseconds,
                    ["x-dead-letter-exchange"] = string.Empty,
                    ["x-dead-letter-routing-key"] = consumer.Queue,
                }, cancellationToken: ct).ConfigureAwait(false);
        }

        await channel.QueueDeclareAsync(DeadLetterQueue, durable: true, exclusive: false, autoDelete: false, cancellationToken: ct).ConfigureAwait(false);
    }

    private async Task OnReceivedAsync(IChannel channel, BasicDeliverEventArgs delivery, CancellationToken ct)
    {
        var attempt = Attempt(delivery.BasicProperties);
        EventEnvelope envelope;
        try
        {
            envelope = EventEnvelope.FromJson(delivery.Body.Span);
        }
        catch (JsonException ex)
        {
            ConsumerLog.Poison(logger, consumer.Queue, ex);
            await ForwardAsync(channel, delivery, DeadLetterQueue, attempt, "poison", ct).ConfigureAwait(false);
            return;
        }

        try
        {
            await ProcessAsync(envelope, ct).ConfigureAwait(false);
            await channel.BasicAckAsync(delivery.DeliveryTag, multiple: false, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            if (attempt < consumer.RetryDelays.Count)
            {
                ConsumerLog.Retrying(logger, envelope.EventId, consumer.Queue, attempt + 1, consumer.RetryDelays[attempt], ex);
                await ForwardAsync(channel, delivery, RetryQueue(attempt), attempt + 1, null, ct).ConfigureAwait(false);
            }
            else
            {
                ConsumerLog.DeadLettered(logger, envelope.EventId, consumer.Queue, attempt, ex);
                await ForwardAsync(channel, delivery, DeadLetterQueue, attempt, ex.GetType().Name, ct).ConfigureAwait(false);
                PlatformMessagingMetrics.DeadLetteredCounter.Add(1,
                    new KeyValuePair<string, object?>("queue", consumer.Queue),
                    new KeyValuePair<string, object?>("type", envelope.Type));
            }
        }
    }

    /// <summary>Efecto + fila de la bandeja en una transacción. Un evento ya procesado no repite el efecto.</summary>
    internal async Task<bool> ProcessAsync(EventEnvelope envelope, CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TContext>();
        var handler = scope.ServiceProvider.GetRequiredService<THandler>();
        var strategy = db.Database.CreateExecutionStrategy();

        return await strategy.ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();
            await using var tx = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
            if (await db.Set<InboxMessage>().AnyAsync(m => m.EventId == envelope.EventId && m.Consumer == consumer.Queue, ct).ConfigureAwait(false))
            {
                ConsumerLog.Duplicate(logger, envelope.EventId, consumer.Queue);
                return false;
            }

            await handler.HandleAsync(envelope, envelope.DataAs<TData>(), ct).ConfigureAwait(false);
            db.Set<InboxMessage>().Add(new InboxMessage
            {
                EventId = envelope.EventId,
                Consumer = consumer.Queue,
                Type = envelope.Type,
                ProcessedAt = time.GetUtcNow(),
            });
            try
            {
                await db.SaveChangesAsync(ct).ConfigureAwait(false);
            }
            catch (DbUpdateException)
            {
                if (!await AlreadyProcessedAsync(envelope, ct).ConfigureAwait(false))
                    throw;

                // Otra réplica lo procesó a la vez: la llave de la bandeja lo impidió, el efecto se revierte con la transacción.
                ConsumerLog.Duplicate(logger, envelope.EventId, consumer.Queue);
                return false;
            }

            await tx.CommitAsync(ct).ConfigureAwait(false);
            return true;
        }).ConfigureAwait(false);
    }

    private async Task<bool> AlreadyProcessedAsync(EventEnvelope envelope, CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TContext>();
        return await db.Set<InboxMessage>().AsNoTracking()
            .AnyAsync(m => m.EventId == envelope.EventId && m.Consumer == consumer.Queue, ct).ConfigureAwait(false);
    }

    /// <summary>Republica el mensaje a otra cola (por el exchange por defecto) y confirma el original.</summary>
    private static async Task ForwardAsync(IChannel channel, BasicDeliverEventArgs delivery, string queue, int attempt, string? reason, CancellationToken ct)
    {
        var properties = new BasicProperties(delivery.BasicProperties)
        {
            DeliveryMode = DeliveryModes.Persistent,
            Headers = new Dictionary<string, object?>(delivery.BasicProperties.Headers ?? new Dictionary<string, object?>())
            {
                [AttemptHeader] = attempt,
            },
        };
        if (reason is not null)
            properties.Headers["x-flit-dead-letter-reason"] = reason;

        await channel.BasicPublishAsync(string.Empty, queue, mandatory: false, properties, delivery.Body, ct).ConfigureAwait(false);
        await channel.BasicAckAsync(delivery.DeliveryTag, multiple: false, ct).ConfigureAwait(false);
    }

    private static int Attempt(IReadOnlyBasicProperties properties) =>
        properties.Headers is { } headers && headers.TryGetValue(AttemptHeader, out var value) && value is not null
            ? Convert.ToInt32(value, System.Globalization.CultureInfo.InvariantCulture)
            : 0;

    private async Task DisposeConnectionAsync()
    {
        try
        {
            if (_channel is not null)
                await _channel.DisposeAsync().ConfigureAwait(false);
            if (_connection is not null)
                await _connection.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception)
        {
            // Una conexión ya caída puede fallar al cerrarse.
        }

        _channel = null;
        _connection = null;
    }
}

internal static partial class ConsumerLog
{
    [LoggerMessage(EventId = 7311, Level = LogLevel.Error, Message = "Consumidor {Queue}: no pudo arrancar; se reintenta")]
    public static partial void StartFailed(ILogger logger, string queue, Exception ex);

    [LoggerMessage(EventId = 7312, Level = LogLevel.Information, Message = "Consumidor {Queue}: {EventId} ya procesado; se confirma sin repetir el efecto")]
    public static partial void Duplicate(ILogger logger, Guid eventId, string queue);

    [LoggerMessage(EventId = 7313, Level = LogLevel.Warning, Message = "Consumidor {Queue}: {EventId} falló; reintento {Attempt} en {Delay}")]
    public static partial void Retrying(ILogger logger, Guid eventId, string queue, int attempt, TimeSpan delay, Exception ex);

    [LoggerMessage(EventId = 7314, Level = LogLevel.Error, Message = "Consumidor {Queue}: {EventId} agotó {Attempts} reintentos; queda en la DLQ")]
    public static partial void DeadLettered(ILogger logger, Guid eventId, string queue, int attempts, Exception ex);

    [LoggerMessage(EventId = 7315, Level = LogLevel.Error, Message = "Consumidor {Queue}: mensaje que no es un sobre válido; va a la DLQ")]
    public static partial void Poison(ILogger logger, string queue, Exception ex);
}
