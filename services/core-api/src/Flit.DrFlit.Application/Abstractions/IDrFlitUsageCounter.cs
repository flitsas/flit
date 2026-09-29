namespace Flit.DrFlit.Application.Abstractions;

/// <summary>
/// Contador diario de mensajes al LLM por (tenant, usuario, día Colombia) — HU #12919, ADR-0060 §6. El
/// día lo resuelve la implementación (hora Colombia, no UTC); quien llama nunca pasa una fecha.
/// </summary>
public interface IDrFlitUsageCounter
{
    /// <summary>Mensajes ya consumidos hoy, sin consumir uno nuevo.</summary>
    Task<int> GetUsedTodayAsync(Guid tenantId, Guid userId, CancellationToken ct);

    /// <summary>
    /// Consume un mensaje si todavía no se alcanzó <paramref name="dailyLimit"/>. Atómico: dos llamadas
    /// concurrentes no pueden dejar el contador por encima del tope.
    /// </summary>
    Task<DrFlitUsageConsumption> TryConsumeAsync(Guid tenantId, Guid userId, int dailyLimit, CancellationToken ct);
}

/// <param name="Allowed">True si se consumió el mensaje; false si el tope ya estaba alcanzado.</param>
/// <param name="UsedToday">Mensajes consumidos hoy tras la operación.</param>
public sealed record DrFlitUsageConsumption(bool Allowed, int UsedToday);
