namespace Flit.Analytics.Application.Reporting;

public sealed record ReportingProceduresPageDto(
    IReadOnlyList<ReportingProcedureRowDto> Items,
    int TotalCount,
    int Page,
    int PageSize,
    ReportingKpisDto Kpis);

public sealed record ReportingProcedureRowDto(
    Guid Id,
    string? ReferenceNumber,
    string? ProcedureType,
    string? Status,
    string? Plate,
    string? Vin,
    string? TransitOfficeName,
    string? CompanyName,
    string? PersonDocument,
    string? PersonName,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt,
    double? ElapsedHoursTotal);

public sealed record ReportingKpisDto(
    int Total,
    int Approved,
    int Rejected,
    int InProgress,
    double? AvgElapsedHours);

public sealed record ReportingAuditEntryDto(
    DateTimeOffset ChangedAt,
    string? FromStatus,
    string? ToStatus,
    Guid? ChangedByUserId,
    string? ChangedByDisplayName,
    Guid? RoleIdAtTime,
    Guid? OrganizationIdAtTime,
    string? OrganizationTypeAtTime,
    string? Reason,
    bool HistoryAvailable);

public sealed record ReportingAuditDto(
    Guid ProcedureId,
    bool HistoryAvailable,
    IReadOnlyList<ReportingAuditEntryDto> Entries);

public sealed record ConsolidadoRowDto(
    string Dimension,
    string Key,
    string Label,
    int Total,
    int Approved,
    int Rejected,
    int InProgress,
    double? AvgElapsedHours);

public sealed record ConsolidadoPageDto(IReadOnlyList<ConsolidadoRowDto> Items, int TotalGroups);

public sealed record ProductivityRowDto(
    Guid? ActorId,
    string ActorLabel,
    string Dimension,
    int Total,
    int Approved,
    int Rejected,
    int InProgress,
    double? AvgHours,
    double? MinHours,
    double? MaxHours);

public sealed record ProductivityPageDto(IReadOnlyList<ProductivityRowDto> Items);

public sealed record SlaRowDto(
    string ProcedureType,
    string? TransitOfficeName,
    int SlaHours,
    int Total,
    int WithinSla,
    int OutsideSla,
    double? AvgBusinessHours,
    double CompliancePct);

public sealed record SlaPageDto(IReadOnlyList<SlaRowDto> Items);

public sealed record ExportJobDto(
    Guid Id,
    string Status,
    string ReportType,
    string Format,
    short ProgressPct,
    DateTimeOffset CreatedAt,
    DateTimeOffset? CompletedAt,
    string? ErrorMessage);

public sealed record ExportJobsListDto(IReadOnlyList<ExportJobDto> Items);

public sealed record DownloadUrlDto(string DownloadUrl, DateTimeOffset ExpiresAt);

public sealed record SavedQueryDto(
    Guid Id,
    string Name,
    string? Description,
    object FiltersJson,
    bool IsShared,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt);

public sealed record DashboardPreferencesDto(object ConfigJson);
