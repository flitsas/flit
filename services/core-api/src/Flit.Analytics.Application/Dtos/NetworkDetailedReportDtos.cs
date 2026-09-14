namespace Flit.Analytics.Application.Dtos;

// HU #12360 (Feature #12257, épica #12235) — reporte detallado de la RED de una cabeza de grupo. Las
// columnas del trámite son EXACTAMENTE las de <see cref="DetailedProcedureRowDto"/> (mismo vocabulario que
// /api/v1/detailed-report/procedures) más el cliente dueño de cada registro (AC1). Ningún campo de
// documentos generados ni anexos: ni contenido, ni enlaces, ni identificadores de adjuntos (AC6).

/// <summary>
/// Fila del reporte detallado de la red: la fila de siempre + el cliente al que pertenece
/// (<see cref="TenantId"/>, <see cref="TenantName"/> = razón social del dueño).
/// </summary>
public sealed record NetworkDetailedProcedureRowDto(
    Guid TenantId,
    string TenantName,
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
    string? TransferType)
{
    /// <summary>La misma fila en la forma de la ruta de siempre (sin el cliente dueño).</summary>
    public DetailedProcedureRowDto ToRow() => new(
        Id, ReferenceNumber, ProcedureTypeName, Category, Status, CreatedByDisplayName, SubmittedAt, CompletedAt,
        PersonDocument, PersonFullName, HasTransformation, TransformationDetail, IsLeasing, PaymentType, TransferType);
}

/// <summary>Conteo por cliente de la red (AC1/AC2: los totales dicen de quién son).</summary>
public sealed record TenantCountDto(Guid TenantId, string TenantName, int Count);

/// <summary>
/// Totales del reporte de red: los de siempre (<see cref="DetailedReportSummaryDto"/>) más el desglose
/// por cliente. Con <c>childTenantId</c> el desglose tiene una sola entrada y los totales son solo de
/// ese hijo (AC2).
/// </summary>
public sealed record NetworkDetailedReportSummaryDto(
    int TotalCount,
    IReadOnlyList<StatusCountDto> ByStatus,
    IReadOnlyList<LabelCountDto> ByCategory,
    IReadOnlyList<LabelCountDto> ByProcedureType,
    IReadOnlyList<TenantCountDto> ByTenant);

/// <summary>Página del reporte de red con totales y el alcance efectivamente consultado.</summary>
public sealed record NetworkDetailedProceduresPageDto(
    IReadOnlyList<NetworkDetailedProcedureRowDto> Items,
    int TotalCount,
    int Page,
    int PageSize,
    NetworkDetailedReportSummaryDto Summary,
    NetworkScopeDto Scope);

/// <summary>
/// Filtro RESUELTO del reporte de red: el conjunto de clientes ya es la intersección del
/// <c>childTenantId</c> con el alcance del servidor (AC4) y los demás filtros son los mismos de
/// <see cref="DetailedReportFilter"/>. Es la ÚNICA entrada del repositorio de red, tanto para el listado
/// como para la exportación (AC5: la exportación usa el mismo filtro resuelto que la consulta).
/// </summary>
public sealed record NetworkDetailedReportFilter(
    IReadOnlySet<Guid> TenantIds,
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
