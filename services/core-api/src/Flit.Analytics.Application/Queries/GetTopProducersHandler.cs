using Flit.Analytics.Application.Abstractions;
using Flit.Analytics.Application.Dtos;

namespace Flit.Analytics.Application.Queries;

/// <summary>
/// Consulta del Top de productividad por tenant y rango (HU #10243, AC3).
/// <see cref="TenantId"/> null = vista global de todas las compañías (solo SuperAdmin).
/// </summary>
public sealed record GetTopProducersQuery(Guid? TenantId, DateOnly From, DateOnly To, int Limit);

/// <summary>
/// Valida el rango, acota el límite y devuelve el ranking de radicadores por <c>submitted_count</c>.
/// Error <c>invalid_range</c> cuando From &gt; To (AC4 → 400).
/// </summary>
public sealed class GetTopProducersHandler(IAnalyticsReadRepository repo)
{
    /// <summary>Límite por defecto (AC3 pide Top 5) y tope de seguridad.</summary>
    public const int DefaultLimit = 5;
    public const int MaxLimit = 50;

    public async Task<(IReadOnlyList<TopProducerDto>? Result, string? Error)> HandleAsync(
        GetTopProducersQuery query, CancellationToken ct = default)
    {
        if (query.From > query.To)
            return (null, "invalid_range");

        var limit = query.Limit <= 0 ? DefaultLimit : Math.Min(query.Limit, MaxLimit);
        var items = await repo.GetTopProducersAsync(query.TenantId, query.From, query.To, limit, ct);
        return (items, null);
    }
}
