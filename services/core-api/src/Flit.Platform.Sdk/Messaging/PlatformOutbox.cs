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
}

internal sealed class PlatformOutbox<TContext>(TContext db, PlatformMessagingOptions options, IHttpContextAccessor http, TimeProvider time)
    : IPlatformOutbox
    where TContext : DbContext
{
    public EventEnvelope Enqueue<TData>(string type, int version, Guid tenantId, TData data)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(type);
        ArgumentOutOfRangeException.ThrowIfLessThan(version, 1);
        if (tenantId == Guid.Empty)
            throw new ArgumentException("Todo evento es de una empresa (contrato §7).", nameof(tenantId));
        if (!type.StartsWith(options.Producer + ".", StringComparison.Ordinal))
            throw new ArgumentException($"El tipo debe empezar por «{options.Producer}.» (<productor>.<entidad>.<hecho>).", nameof(type));

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
            Exchange = EventEnvelope.ExchangeFor(options.Producer),
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
