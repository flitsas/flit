using Flit.Analytics.Application.Abstractions;

namespace Flit.Analytics.Application.Queries;

/// <summary>Parámetros del Resumen Ejecutivo PDF (HU #10246).</summary>
public sealed record ExportExecutivePdfQuery(Guid TenantId, DateOnly From, DateOnly To);

/// <summary>
/// Ensambla el Resumen Ejecutivo: valida el rango, consulta totales por categoría (overview) y
/// el Top 5 de productividad, y delega la generación del PDF. Error <c>invalid_range</c> si From &gt; To.
/// </summary>
public sealed class ExportExecutivePdfHandler(IAnalyticsReadRepository repo, IExecutiveSummaryPdfGenerator pdf)
{
    /// <summary>El resumen incluye el Top 5 (AC1).</summary>
    public const int TopLimit = 5;

    public async Task<(byte[]? Pdf, string? Error)> HandleAsync(
        ExportExecutivePdfQuery query, CancellationToken ct = default)
    {
        if (query.From > query.To)
            return (null, "invalid_range");

        var categories = await repo.GetOverviewAsync(query.TenantId, query.From, query.To, ct);
        var top = await repo.GetTopProducersAsync(query.TenantId, query.From, query.To, TopLimit, ct);

        var bytes = pdf.Generate(new ExecutiveSummaryData(query.TenantId, query.From, query.To, categories, top));
        return (bytes, null);
    }
}
