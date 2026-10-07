using System.Diagnostics;
using System.Text.Json;
using Flit.Api.Middleware;
using Microsoft.EntityFrameworkCore;

namespace Flit.Platform.Sdk.Messaging;

/// <summary>Guarda eventos en la outbox del servicio. Se confirma con el <c>SaveChanges</c> del cambio que los origina.</summary>
public interface IPlatformOutbox
{
    /// <summary>
    /// Agrega el evento al <c>DbContext</c> del servicio. No publica nada: si la transacción se revierte, el evento
    /// desaparece con ella (AC2); si confirma, el publicador lo manda al broker (AC1).
    /// </summary>
    EventEnvelope Enqueue<TData>(string type, int version, Guid tenantId, TData data);

    /// <summary>
    /// HU #13354 — un TRABAJO para otro servicio (p. ej. <c>notificaciones.email.send</c>): a diferencia de un evento, va
    /// al exchange del servicio que lo atiende (<c>flit.&lt;servicio&gt;</c>, la primera parte del tipo) y el sobre lleva a
    /// este servicio como productor. El usuario del broker de este servicio necesita permiso de escritura en ese exchange
    /// (<c>deploy/rabbitmq/usuario-de-servicio.sh &lt;servicio&gt; &lt;clave&gt; &lt;destino&gt;</c>).
    /// </summary>
    EventEnvelope EnqueueJob<TData>(string type, int version, Guid tenantId, TData data);
}

internal sealed class PlatformOutbox<TContext>(TContext db, PlatformMessagingOptions options, IHttpContextAccessor http, TimeProvider time)
    : IPlatformOutbox
    where TContext : DbContext
{
    public EventEnvelope Enqueue<TData>(string type, int version, Guid tenantId, TData data)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(type);
        if (!type.StartsWith(options.Producer + ".", StringComparison.Ordinal))
            throw new ArgumentException($"El tipo debe empezar por «{options.Producer}.» (<productor>.<entidad>.<hecho>).", nameof(type));
        return Add(options.Producer, type, version, tenantId, data);
    }

    public EventEnvelope EnqueueJob<TData>(string type, int version, Guid tenantId, TData data)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(type);
        var punto = type.IndexOf('.', StringComparison.Ordinal);
        var destino = punto > 0 ? type[..punto] : string.Empty;
        if (destino.Length == 0 || punto == type.Length - 1)
            throw new ArgumentException("El tipo de un trabajo es <servicio>.<trabajo> (p. ej. notificaciones.email.send).", nameof(type));
        if (string.Equals(destino, options.Producer, StringComparison.Ordinal))
            throw new ArgumentException("Un servicio no se deja trabajos a sí mismo: eso es un evento (Enqueue).", nameof(type));
        return Add(destino, type, version, tenantId, data);
    }

    private EventEnvelope Add<TData>(string exchangeOwner, string type, int version, Guid tenantId, TData data)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(version, 1);
        if (tenantId == Guid.Empty)
            throw new ArgumentException("Todo evento es de una empresa (contrato §7).", nameof(tenantId));

        var envelope = new EventEnvelope(
            Guid.CreateVersion7(),
            type,
            version,
            time.GetUtcNow(),
            tenantId,
            options.Producer,
            CorrelationId(),
            JsonSerializer.SerializeToElement(data, EventEnvelope.JsonOptions));

        db.Set<OutboxMessage>().Add(new OutboxMessage
        {
            Id = envelope.EventId,
            Exchange = EventEnvelope.ExchangeFor(exchangeOwner),
            RoutingKey = type,
            Payload = envelope.ToJson(),
            OccurredAt = envelope.OccurredAt,
        });
        return envelope;
    }

    private string CorrelationId()
    {
        var header = http.HttpContext?.Request.Headers[CorrelationIdMiddleware.HeaderName].ToString();
        if (!string.IsNullOrEmpty(header))
            return header;
        return Activity.Current?.GetTagItem(CorrelationIdMiddleware.TraceTag) as string ?? Guid.CreateVersion7().ToString();
    }
}
