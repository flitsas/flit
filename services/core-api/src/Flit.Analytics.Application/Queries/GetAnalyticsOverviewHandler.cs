using Flit.Analytics.Application.Abstractions;
using Flit.Analytics.Application.Dtos;

namespace Flit.Analytics.Application.Queries;

/// <summary>
/// Consulta del overview analítico por tenant y rango de fechas (HU #10243, AC1/AC2).
/// <see cref="TenantId"/> null = vista global de todas las compañías (solo SuperAdmin).
/// </summary>
public sealed record GetAnalyticsOverviewQuery(Guid? TenantId, DateOnly From, DateOnly To);

/// <summary>
/// Valida el rango y arma <see cref="AnalyticsOverviewDto"/> a partir de los agregados.
/// Error <c>invalid_range</c> cuando From &gt; To (AC4 → 400).
/// </summary>
public sealed class GetAnalyticsOverviewHandler(IAnalyticsReadRepository repo)
{
    public async Task<(AnalyticsOverviewDto? Result, string? Error)> HandleAsync(
        GetAnalyticsOverviewQuery query, CancellationToken ct = default)
    {
        if (query.From > query.To)
            return (null, "invalid_range");

        var categories = await repo.GetOverviewAsync(query.TenantId, query.From, query.To, ct);
        // TenantId null indica vista global (SuperAdmin); Guid.Empty es el centinela en el DTO.
        var dto = new AnalyticsOverviewDto(query.TenantId ?? Guid.Empty, query.From, query.To, categories);
        return (dto, null);
    }
}
