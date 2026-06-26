namespace Flit.Analytics.Application.Abstractions;

/// <summary>Filtro normalizado para el export de detalle de trámites (HU #10245).</summary>
public sealed record ProcedureExportFilter(
    Guid TenantId, DateOnly From, DateOnly To, string? Category, string? Status);

/// <summary>
/// Genera el Excel (.xlsx) del detalle de trámites escribiéndolo de forma incremental
/// (streaming) en <paramref name="output"/>, sin materializar todas las filas en memoria (AC2).
/// </summary>
public interface IProcedureExcelExporter
{
    Task ExportAsync(Stream output, ProcedureExportFilter filter, CancellationToken ct = default);
}
