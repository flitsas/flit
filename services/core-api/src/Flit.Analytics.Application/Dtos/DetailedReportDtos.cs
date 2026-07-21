namespace Flit.Analytics.Application.Dtos;

/// <summary>Fila del reporte detallado de trámites (Feature #10813).</summary>
public sealed record DetailedProcedureRowDto(
    Guid Id,
    string ReferenceNumber,
    string ProcedureTypeName,
    string Category,
    string Status,
    string CreatedByDisplayName,
    DateTimeOffset? SubmittedAt,
    DateTimeOffset? CompletedAt,
    string PersonDocument,
    string PersonFullName,
    bool HasTransformation,
    string? TransformationDetail,
    bool IsLeasing,
    string PaymentType,
    string TransferType);

/// <summary>Totales agregados para KPIs del reporte detallado (HU #10819).</summary>
public sealed record DetailedReportSummaryDto(
    int TotalCount,
    IReadOnlyList<StatusCountDto> ByStatus,
    IReadOnlyList<LabelCountDto> ByCategory,
    IReadOnlyList<LabelCountDto> ByProcedureType);

/// <summary>Conteo genérico por etiqueta (categoría, tipo de trámite, compañía…).</summary>
public sealed record LabelCountDto(string Label, int Count);

/// <summary>Página paginada del reporte detallado con resumen KPI.</summary>
public sealed record DetailedProceduresPageDto(
    IReadOnlyList<DetailedProcedureRowDto> Items,
    int TotalCount,
    int Page,
    int PageSize,
    DetailedReportSummaryDto Summary);

/// <summary>Filtros del reporte detallado (HU #10815).</summary>
public sealed record DetailedReportFilter(
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
