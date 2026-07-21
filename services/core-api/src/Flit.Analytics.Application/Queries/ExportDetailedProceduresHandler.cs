using Flit.Analytics.Application.Abstractions;
using Flit.Analytics.Application.Dtos;

namespace Flit.Analytics.Application.Queries;

public sealed record ExportDetailedProceduresQuery(
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
    bool? IsLeasing);

/// <summary>Valida filtros del export del reporte detallado (HU #10816).</summary>
public static class ExportDetailedProceduresHandler
{
    public static (DetailedReportFilter? Filter, string? Error) Validate(ExportDetailedProceduresQuery query)
    {
        if (query.From > query.To)
            return (null, "invalid_range");

        var getQuery = new GetDetailedProceduresQuery(
            query.TenantId, query.From, query.To,
            query.TransitOfficeId, query.ProcedureTypeId, query.Category, query.Status,
            query.ReferenceNumber, query.PersonDocument, query.PersonName,
            query.HasTransformation, query.IsLeasing, 1, 1);

        if (!GetDetailedProceduresHandler.HasMinimumFilters(getQuery))
            return (null, "missing_filters");

        return (new DetailedReportFilter(
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
            query.IsLeasing), null);
    }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public interface IDetailedReportExcelExporter
{
    Task ExportAsync(Stream output, DetailedReportFilter filter, CancellationToken ct = default);
}
