using System.Collections.Concurrent;
using System.Text;
using RabbitMQ.Client;

namespace Flit.Platform.Sdk.Messaging;

/// <summary>Publica un mensaje ya serializado y vuelve solo cuando el broker lo confirmó.</summary>
public interface IEventPublisher
{
    Task PublishAsync(OutboxMessage message, CancellationToken ct);
}

/// <summary>
/// Publicador sobre RabbitMQ.Client (ADR-0064): una conexión por proceso, canal con confirmaciones del broker
/// (<c>BasicPublishAsync</c> espera el ack y falla con un nack o si se cae la conexión), mensajes persistentes y el
/// exchange topic <c>flit.&lt;productor&gt;</c> declarado (durable, idempotente) la primera vez; el exchange de otro servicio
/// (trabajos) solo se comprueba. Si la conexión se pierde, la siguiente publicación abre otra.
/// </summary>
internal sealed class RabbitMqEventPublisher(PlatformMessagingOptions options) : IEventPublisher, IAsyncDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly ConcurrentDictionary<string, bool> _declared = new(StringComparer.Ordinal);
    private IConnection? _connection;
    private IChannel? _channel;

    public async Task PublishAsync(OutboxMessage message, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(message);
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var channel = await ChannelAsync(ct).ConfigureAwait(false);
            if (!_declared.ContainsKey(message.Exchange))
            {
                // Su exchange lo declara; el de otro servicio (un trabajo, HU #13354) solo se comprueba: no tiene permiso
                // de declararlo y lo crea definitions.json del broker.
                if (string.Equals(message.Exchange, EventEnvelope.ExchangeFor(options.Producer), StringComparison.Ordinal))
                    await channel.ExchangeDeclareAsync(message.Exchange, ExchangeType.Topic, durable: true, autoDelete: false, cancellationToken: ct).ConfigureAwait(false);
                else
                    await channel.ExchangeDeclarePassiveAsync(message.Exchange, ct).ConfigureAwait(false);
                _declared[message.Exchange] = true;
            }

            var envelope = EventEnvelope.FromJson(Encoding.UTF8.GetBytes(message.Payload));
            var properties = new BasicProperties
            {
                MessageId = message.Id.ToString(),
                Type = message.RoutingKey,
                CorrelationId = envelope.CorrelationId,
                ContentType = "application/json",
                ContentEncoding = "utf-8",
                DeliveryMode = DeliveryModes.Persistent,
                Timestamp = new AmqpTimestamp(envelope.OccurredAt.ToUnixTimeSeconds()),
                Headers = new Dictionary<string, object?> { ["x-flit-tenant-id"] = envelope.TenantId.ToString() },
            };
            await channel.BasicPublishAsync(message.Exchange, message.RoutingKey, mandatory: false, properties,
                Encoding.UTF8.GetBytes(message.Payload), ct).ConfigureAwait(false);
        }
        catch
        {
            await ResetAsync().ConfigureAwait(false);
            throw;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<IChannel> ChannelAsync(CancellationToken ct)
    {
        if (_channel is { IsOpen: true } open)
            return open;

        await ResetAsync().ConfigureAwait(false);
        var factory = new ConnectionFactory
        {
            Uri = new Uri(options.ConnectionString),
            ClientProvidedName = $"flit-{options.Producer}-publicador",
            AutomaticRecoveryEnabled = false, // la recuperación es «abrir otra en el siguiente intento»
            RequestedConnectionTimeout = TimeSpan.FromSeconds(5),
        };
        _connection = await factory.CreateConnectionAsync(ct).ConfigureAwait(false);
        _channel = await _connection.CreateChannelAsync(
            new CreateChannelOptions(publisherConfirmationsEnabled: true, publisherConfirmationTrackingEnabled: true), ct).ConfigureAwait(false);
        return _channel;
    }

    private async Task ResetAsync()
    {
        _declared.Clear();
        var channel = _channel;
        var connection = _connection;
        _channel = null;
        _connection = null;
        try
        {
            if (channel is not null)
                await channel.DisposeAsync().ConfigureAwait(false);
            if (connection is not null)
                await connection.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception)
        {
            // Una conexión ya caída puede fallar al cerrarse; no importa, se abre otra.
        }
    }

    public async ValueTask DisposeAsync()
    {
        await ResetAsync().ConfigureAwait(false);
        _gate.Dispose();
    }
}
