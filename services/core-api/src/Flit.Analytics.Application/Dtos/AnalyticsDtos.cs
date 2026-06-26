namespace Flit.Analytics.Application.Dtos;

// Contratos de respuesta del dashboard analítico (HU #10243, Feature #10139).
// Los nombres de campo son contrato con el frontend (#10247/#10248): NO renombrar
// sin coordinar. Categorías normalizadas: "matriculas" | "traspasos" | "otros".

/// <summary>Conteo de trámites en un estado concreto dentro de una categoría.</summary>
public sealed record StatusCountDto(string Status, int Count);

/// <summary>
/// Métricas agregadas de una categoría (matriculas/traspasos/otros): total del periodo
/// y desglose por estado del trámite (draft, submitted, in_review, …).
/// </summary>
public sealed record CategoryMetricsDto(string Category, int Total, IReadOnlyList<StatusCountDto> ByStatus);

/// <summary>
/// Respuesta de GET /api/v1/analytics/overview — métricas por categoría/estado del tenant
/// en el rango [From, To]. <see cref="TenantId"/> refleja el tenant efectivamente consultado
/// (AC1: coincide con el token; AC2: el tenantId que indique el SuperAdmin).
/// </summary>
public sealed record AnalyticsOverviewDto(
    Guid TenantId,
    DateOnly From,
    DateOnly To,
    IReadOnlyList<CategoryMetricsDto> Categories);

/// <summary>
/// Fila del Top 5 de productividad (AC3): un radicador con sus conteos del periodo,
/// ordenado por <see cref="SubmittedCount"/> descendente.
/// </summary>
public sealed record TopProducerDto(
    Guid UserId,
    string DisplayName,
    int SubmittedCount,
    int ApprovedCount,
    int RejectedCount);

/// <summary>
/// Fila de la tabla lateral de detalle de trámites (HU #10244). Columnas obligatorias del RF05;
/// los nombres son contrato con el frontend (#10248): NO renombrar sin coordinar.
/// </summary>
public sealed record ProcedureDetailDto(
    Guid Id,
    string ReferenceNumber,
    string ProcedureTypeName,
    string Category,
    string Status,
    string CreatedByDisplayName,
    DateTimeOffset? SubmittedAt,
    DateTimeOffset? CompletedAt);

/// <summary>
/// Página de detalle de trámites (HU #10244): <see cref="Items"/> de la página y
/// <see cref="TotalCount"/> del universo filtrado (AC1/AC3).
/// </summary>
public sealed record ProcedureDetailsPageDto(
    IReadOnlyList<ProcedureDetailDto> Items,
    int TotalCount,
    int Page,
    int PageSize);
