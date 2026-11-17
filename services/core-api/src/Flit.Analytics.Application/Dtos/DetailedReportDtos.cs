using Flit.Analytics.Application.CompanyQueries;

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
    // Nullable: solo los trámites de la familia TRASPASO tienen clase de traspaso.
    string? TransferType,
    // HU #14319 — canal efectivo (los trámites sin dato, incluido el migrado, llegan como «tramites») y OT del canal.
    string Channel = "tramites",
    Guid? ChannelTransitOfficeId = null,
    string? ChannelTransitOfficeName = null,
    // Validación de identidad por persona (compradores, vendedores, propietarios); null = no cargada.
    IReadOnlyList<ActorValidacionDto>? Actores = null);

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
    DetailedReportSummaryDto Summary,
    // Máximo de actores por rol sobre TODO el filtro (no la página): ancho de las columnas del Excel.
    MaxActoresPorRolDto MaxActoresPorRol = default);

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
    bool? IsLeasing)
{
    /// <summary>HU #13664 — Ver por familia: códigos visibles (<c>ProcedureFamilyCodes</c>); <c>null</c> = sin restricción.</summary>
    public IReadOnlyList<string>? AllowedFamilies { get; init; }

    /// <summary>HU #14319 — código de <c>TramiteCanal</c> ya validado por el endpoint; <c>null</c> = todos los canales.</summary>
    public string? Channel { get; init; }

    /// <summary>HU #14319 — organismo del canal (punto de atención Persona Natural OT); <c>null</c> = sin filtro.</summary>
    public Guid? ChannelTransitOfficeId { get; init; }
}
