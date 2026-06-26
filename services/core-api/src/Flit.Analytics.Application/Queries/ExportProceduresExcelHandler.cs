using Flit.Analytics.Application.Abstractions;

namespace Flit.Analytics.Application.Queries;

/// <summary>Parámetros del export Excel de detalle de trámites (HU #10245).</summary>
public sealed record ExportProceduresExcelQuery(
    Guid TenantId, DateOnly From, DateOnly To, string? Category, string? Status);

/// <summary>
/// Valida el rango y normaliza los filtros del export (sin tocar BD: la escritura del archivo
/// la hace <see cref="IProcedureExcelExporter"/> en streaming). Error <c>invalid_range</c> cuando
/// From &gt; To → el endpoint responde 400 antes de empezar a escribir el cuerpo.
/// </summary>
public static class ExportProceduresExcelHandler
{
    public static (ProcedureExportFilter? Filter, string? Error) Validate(ExportProceduresExcelQuery query)
    {
        if (query.From > query.To)
            return (null, "invalid_range");

        var category = string.IsNullOrWhiteSpace(query.Category) ? null : query.Category.Trim().ToLowerInvariant();
        var status = string.IsNullOrWhiteSpace(query.Status) ? null : query.Status.Trim();
        return (new ProcedureExportFilter(query.TenantId, query.From, query.To, category, status), null);
    }
}
