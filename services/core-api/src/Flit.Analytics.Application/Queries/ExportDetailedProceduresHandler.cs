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

        // Solo el rango de fechas es obligatorio (alineado con la consulta): sin más filtros
        // se exportan todos los trámites de la compañía en el rango.
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

    /// <summary>
    /// HU #12360 (Feature #12257) — sobrecarga de RED: el mismo generador OpenXml y las mismas columnas
    /// que <see cref="ExportAsync"/> precedidas por «Compañía» (razón social del cliente dueño de cada
    /// fila, AC1), sobre el filtro resuelto del listado de red (AC5). Sin filas escribe solo la cabecera
    /// (un reporte sin registros, AC4). Devuelve los clientes distintos con filas en el archivo
    /// (auditoría HU #12361). La salida de <see cref="ExportAsync"/> no cambia (AC7).
    /// </summary>
    Task<IReadOnlyList<Guid>> ExportNetworkAsync(Stream output, NetworkDetailedReportFilter filter, CancellationToken ct = default);
}
