using Flit.Analytics.Application.Abstractions;
using Flit.Analytics.Application.Dtos;

namespace Flit.Analytics.Application.Queries;

/// <summary>
/// Consulta de tendencia mensual por categoría (Dashboard).
/// <see cref="TenantId"/> null = vista global de todas las compañías (solo SuperAdmin).
/// </summary>
public sealed record GetMonthlyTrendQuery(Guid? TenantId, DateOnly From, DateOnly To);

/// <summary>
/// Valida el rango y retorna la tendencia mensual agrupada por (año, mes, categoría).
/// Error <c>invalid_range</c> cuando From &gt; To (→ 400).
/// </summary>
public sealed class GetMonthlyTrendHandler(IAnalyticsReadRepository repo)
{
    public async Task<(IReadOnlyList<MonthlyTrendPointDto>? Result, string? Error)> HandleAsync(
        GetMonthlyTrendQuery query, CancellationToken ct = default)
    {
        if (query.From > query.To)
            return (null, "invalid_range");

        var items = await repo.GetMonthlyTrendAsync(query.TenantId, query.From, query.To, ct);
        return (items, null);
    }
}
