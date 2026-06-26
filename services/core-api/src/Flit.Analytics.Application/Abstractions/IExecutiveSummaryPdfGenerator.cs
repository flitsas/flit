using Flit.Analytics.Application.Dtos;

namespace Flit.Analytics.Application.Abstractions;

/// <summary>Datos del Resumen Ejecutivo (HU #10246): periodo, totales por categoría y Top 5.</summary>
public sealed record ExecutiveSummaryData(
    Guid TenantId,
    DateOnly From,
    DateOnly To,
    IReadOnlyList<CategoryMetricsDto> Categories,
    IReadOnlyList<TopProducerDto> TopProducers);

/// <summary>Genera el PDF del Resumen Ejecutivo a partir de los agregados ya consultados.</summary>
public interface IExecutiveSummaryPdfGenerator
{
    byte[] Generate(ExecutiveSummaryData data);
}
