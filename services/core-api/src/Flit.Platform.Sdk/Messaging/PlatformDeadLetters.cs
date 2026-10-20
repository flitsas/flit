using System.Globalization;
using System.Text;
using System.Text.Json;
using RabbitMQ.Client;

namespace Flit.Platform.Sdk.Messaging;

/// <summary>Un mensaje en la cola de mensajes muertos de un consumidor, con lo que sirve para decidir qué hacer.</summary>
/// <param name="MessageId">Id del mensaje (= <see cref="EventEnvelope.EventId"/>).</param>
/// <param name="Reason">Tipo de la última falla (excepción) o <c>poison</c> si no era un sobre.</param>
/// <param name="Error">Mensaje de la última falla, recortado.</param>
public sealed record DeadLetterMessage(
    string MessageId,
    string Type,
    Guid? TenantId,
    string? Producer,
    DateTimeOffset? OccurredAt,
    DateTimeOffset? DeadLetteredAt,
    string? Reason,
    string? Error,
    int Attempts);

/// <summary>
/// HU #13357 (Epic #13316) — administración de la cola <c>&lt;cola&gt;.dlq</c> de un consumidor del SDK, directamente en el
/// broker: listar, reintentar (el mensaje vuelve a su cola con los intentos en cero, por el exchange
/// <c>&lt;cola&gt;.reintentos</c>) y descartar. Para buscar un mensaje recorre la cola con <c>basic.get</c> sin confirmar y
/// devuelve el resto tal como estaba: pensado para colas de mensajes muertos (pocas decenas), no para colas de trabajo.
/// Lo usa el servicio dueño de la cola, con su propio usuario del broker.
/// </summary>
public sealed class PlatformDeadLetters(PlatformMessagingOptions options) : IAsyncDisposable
{
    public const string ReasonHeader = "x-flit-dead-letter-reason";
    public const string ErrorHeader = "x-flit-error";
    public const string AtHeader = "x-flit-dead-lettered-at";

    private readonly SemaphoreSlim _gate = new(1, 1);
    private IConnection? _connection;

    /// <summary>Hasta <paramref name="max"/> mensajes de <c>&lt;cola&gt;.dlq</c>, del más antiguo al más reciente.</summary>
    public async Task<IReadOnlyList<DeadLetterMessage>> ListAsync(string queue, int max, CancellationToken ct)
    {
        var lista = new List<DeadLetterMessage>();
        await RecorrerAsync(queue, (canal, mensaje) =>
        {
            if (lista.Count < max)
                lista.Add(Describir(mensaje));
            return Task.FromResult(false);
        }, ct).ConfigureAwait(false);
        return lista;
    }

    /// <summary>Devuelve el mensaje a su cola con los intentos en cero. false si ya no está en la .dlq.</summary>
    public Task<bool> RetryAsync(string queue, string messageId, CancellationToken ct) =>
        RecorrerAsync(queue, async (canal, mensaje) =>
        {
            if (mensaje.BasicProperties.MessageId != messageId)
                return false;

            var headers = new Dictionary<string, object?>(mensaje.BasicProperties.Headers ?? new Dictionary<string, object?>());
            headers.Remove(ReasonHeader);
            headers.Remove(ErrorHeader);
            headers.Remove(AtHeader);
            headers["x-flit-attempt"] = 0;
            var propiedades = new BasicProperties(mensaje.BasicProperties) { DeliveryMode = DeliveryModes.Persistent, Headers = headers };
            await canal.BasicPublishAsync($"{queue}.reintentos", queue, mandatory: false, propiedades, mensaje.Body, ct).ConfigureAwait(false);
            await canal.BasicAckAsync(mensaje.DeliveryTag, multiple: false, ct).ConfigureAwait(false);
            return true;
        }, ct);

    /// <summary>Saca el mensaje de la .dlq sin procesarlo. false si ya no estaba.</summary>
    public Task<bool> DiscardAsync(string queue, string messageId, CancellationToken ct) =>
        RecorrerAsync(queue, async (canal, mensaje) =>
        {
            if (mensaje.BasicProperties.MessageId != messageId)
                return false;
            await canal.BasicAckAsync(mensaje.DeliveryTag, multiple: false, ct).ConfigureAwait(false);
            return true;
        }, ct);

    public async ValueTask DisposeAsync()
    {
        if (_connection is not null)
            await _connection.DisposeAsync().ConfigureAwait(false);
        _gate.Dispose();
    }

    /// <summary>
    /// Toma uno a uno los mensajes de la .dlq (sin confirmar) hasta que <paramref name="accion"/> diga que terminó o se
    /// acabe la cola; al cerrar el canal, los que no se confirmaron vuelven a la cola.
    /// </summary>
    private async Task<bool> RecorrerAsync(string queue, Func<IChannel, BasicGetResult, Task<bool>> accion, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(queue);
        var conexion = await ConnectionAsync(ct).ConfigureAwait(false);
        await using var canal = await conexion.CreateChannelAsync(cancellationToken: ct).ConfigureAwait(false);
        var dlq = $"{queue}.dlq";
        var total = await canal.MessageCountAsync(dlq, ct).ConfigureAwait(false);
        for (var i = 0; i < total; i++)
        {
            var mensaje = await canal.BasicGetAsync(dlq, autoAck: false, ct).ConfigureAwait(false);
            if (mensaje is null)
                break;
            if (await accion(canal, mensaje).ConfigureAwait(false))
                return true;
        }

        return false;
    }

    private async Task<IConnection> ConnectionAsync(CancellationToken ct)
    {
        if (_connection is { IsOpen: true })
            return _connection;
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_connection is { IsOpen: true })
                return _connection;
            if (_connection is not null)
                await _connection.DisposeAsync().ConfigureAwait(false);
            _connection = await new ConnectionFactory { Uri = new Uri(options.ConnectionString) }.CreateConnectionAsync(ct).ConfigureAwait(false);
            return _connection;
        }
        finally
        {
            _gate.Release();
        }
    }

    private static DeadLetterMessage Describir(BasicGetResult mensaje)
    {
        var p = mensaje.BasicProperties;
        Guid? tenant = null;
        string? producer = null;
        DateTimeOffset? ocurrido = null;
        try
        {
            var sobre = EventEnvelope.FromJson(mensaje.Body.Span);
            tenant = sobre.TenantId;
            producer = sobre.Producer;
            ocurrido = sobre.OccurredAt;
        }
        catch (JsonException)
        {
            // Un mensaje que no es un sobre (poison) igual se lista, sin empresa.
        }

        return new DeadLetterMessage(
            p.MessageId ?? string.Empty,
            p.Type ?? string.Empty,
            tenant,
            producer,
            ocurrido,
            DateTimeOffset.TryParse(Texto(p, AtHeader), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var at) ? at : null,
            Texto(p, ReasonHeader),
            Texto(p, ErrorHeader),
            p.Headers is { } h && h.TryGetValue("x-flit-attempt", out var a) && a is not null ? Convert.ToInt32(a, CultureInfo.InvariantCulture) : 0);
    }

    private static string? Texto(IReadOnlyBasicProperties p, string header) =>
        p.Headers is { } h && h.TryGetValue(header, out var v) ? v switch
        {
            byte[] bytes => Encoding.UTF8.GetString(bytes),
            string s => s,
            null => null,
            _ => v.ToString(),
        } : null;
}
