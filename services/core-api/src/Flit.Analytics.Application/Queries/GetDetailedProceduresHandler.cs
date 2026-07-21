using Flit.Analytics.Application.Abstractions;
using Flit.Analytics.Application.Dtos;

namespace Flit.Analytics.Application.Queries;

public sealed record GetDetailedProceduresQuery(
    Guid TenantId,
    DateOnly From,
    DateOnly To,
    Guid? TransitOfficeId,
    Guid? ProcedureTypeId,
    string? Category,
    string? Status,
    string? ReferenceNumber,
    string? PersonDocument,
    string? PersonName,
    bool? HasTransformation,
    bool? IsLeasing,
    int Page,
    int PageSize);

/// <summary>Consulta paginada del reporte detallado (HU #10815).</summary>
public sealed class GetDetailedProceduresHandler(IDetailedReportReadRepository repo)
{
    public const int DefaultPageSize = 20;
    public const int MaxPageSize = 100;

    public async Task<(DetailedProceduresPageDto? Result, string? Error)> HandleAsync(
        GetDetailedProceduresQuery query, CancellationToken ct = default)
    {
        if (query.From > query.To)
            return (null, "invalid_range");

        if (!HasMinimumFilters(query))
            return (null, "missing_filters");

        var page = query.Page <= 0 ? 1 : query.Page;
        var pageSize = query.PageSize <= 0 ? DefaultPageSize : Math.Min(query.PageSize, MaxPageSize);
        var filter = new DetailedReportFilter(
            query.TenantId,
            query.From,
            query.To,
            query.TransitOfficeId,
            query.ProcedureTypeId,
            Normalize(query.Category)?.ToLowerInvariant(),
            Normalize(query.Status),
            Normalize(query.ReferenceNumber),
            Normalize(query.PersonDocument),
            Normalize(query.PersonName),
            query.HasTransformation,
            query.IsLeasing);

        var result = await repo.GetProceduresAsync(filter, page, pageSize, ct).ConfigureAwait(false);
        return (result, null);
    }

    internal static bool HasMinimumFilters(GetDetailedProceduresQuery query)
    {
        var hasProcedureType = query.ProcedureTypeId is not null
            || !string.IsNullOrWhiteSpace(query.Category);
        if (!hasProcedureType)
            return false;

        return query.TransitOfficeId is not null
            || !string.IsNullOrWhiteSpace(query.Status)
            || !string.IsNullOrWhiteSpace(query.ReferenceNumber)
            || !string.IsNullOrWhiteSpace(query.PersonDocument)
            || !string.IsNullOrWhiteSpace(query.PersonName)
            || query.HasTransformation is not null
            || query.IsLeasing is not null;
    }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
