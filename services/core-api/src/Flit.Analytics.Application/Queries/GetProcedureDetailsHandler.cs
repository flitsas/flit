using Flit.Analytics.Application.Abstractions;
using Flit.Analytics.Application.Dtos;

namespace Flit.Analytics.Application.Queries;

/// <summary>
/// Consulta del detalle paginado de trámites (HU #10244). <see cref="Category"/>/<see cref="Status"/>
/// nulos o vacíos = sin filtro.
/// </summary>
public sealed record GetProcedureDetailsQuery(
    Guid TenantId, DateOnly From, DateOnly To, string? Category, string? Status, int Page, int PageSize);

/// <summary>
/// Valida el rango, acota la paginación y delega la consulta. Error <c>invalid_range</c> cuando
/// From &gt; To (AC → 400). Categoría se normaliza a minúsculas para el contrato del dashboard.
/// </summary>
public sealed class GetProcedureDetailsHandler(IAnalyticsReadRepository repo)
{
    public const int DefaultPageSize = 20;
    public const int MaxPageSize = 100;

    public async Task<(ProcedureDetailsPageDto? Result, string? Error)> HandleAsync(
        GetProcedureDetailsQuery query, CancellationToken ct = default)
    {
        if (query.From > query.To)
            return (null, "invalid_range");

        var page = query.Page <= 0 ? 1 : query.Page;
        var pageSize = query.PageSize <= 0 ? DefaultPageSize : Math.Min(query.PageSize, MaxPageSize);
        var category = Normalize(query.Category)?.ToLowerInvariant();
        var status = Normalize(query.Status);

        var result = await repo.GetProcedureDetailsAsync(
            query.TenantId, query.From, query.To, category, status, page, pageSize, ct);
        return (result, null);
    }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
