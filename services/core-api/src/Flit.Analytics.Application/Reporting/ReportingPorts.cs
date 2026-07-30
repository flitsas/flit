using Flit.Analytics.Application.Reporting;

namespace Flit.Analytics.Application.Reporting;

public sealed record ReportingProceduresFilter(
    Guid TenantId,
    DateOnly From,
    DateOnly To,
    string DateType,
    Guid? TransitOfficeId,
    string? ProcedureType,
    string? Status,
    string? Search,
    string SortBy,
    string SortOrder);

public interface IReportingReadRepository
{
    Task<ReportingProceduresPageDto> GetProceduresAsync(
        ReportingProceduresFilter filter, int page, int pageSize, CancellationToken ct = default);

    Task<ReportingProcedureRowDto?> GetProcedureAsync(
        Guid tenantId, Guid procedureId, CancellationToken ct = default);

    Task<ReportingAuditDto> GetAuditAsync(
        Guid tenantId, Guid procedureId, CancellationToken ct = default);

    Task<ConsolidadoPageDto> GetConsolidadoAsync(
        Guid tenantId, DateOnly from, DateOnly toDate, string groupBy, CancellationToken ct = default);

    Task<ProductivityPageDto> GetProductivityAsync(
        Guid tenantId, DateOnly from, DateOnly toDate, string dimension, CancellationToken ct = default);

    Task<SlaPageDto> GetSlaAsync(
        Guid tenantId, DateOnly from, DateOnly toDate, CancellationToken ct = default);
}

public interface IExportJobRepository
{
    Task<int> CountActiveJobsAsync(Guid ownerUserId, CancellationToken ct = default);
    Task<ExportJobDto> CreateAsync(
        Guid tenantId,
        Guid ownerUserId,
        string reportType,
        string format,
        string filtersJson,
        Guid? correlationId,
        CancellationToken ct = default);
    Task<ExportJobDto?> GetAsync(Guid jobId, CancellationToken ct = default);
    Task<IReadOnlyList<ExportJobDto>> ListByOwnerAsync(Guid ownerUserId, CancellationToken ct = default);
    Task<(string? StoragePath, Guid OwnerUserId, string Status)?> GetDownloadMetaAsync(
        Guid jobId, CancellationToken ct = default);
}

public interface IExportFileStorage
{
    Task<(string StoragePath, string Sha256, long SizeBytes)> SaveExportAsync(
        Guid jobId,
        string format,
        string fileName,
        Stream content,
        CancellationToken ct = default);

    Task<(string Url, DateTimeOffset ExpiresAt)?> GetDownloadUrlAsync(
        string storagePath,
        CancellationToken ct = default);
}

public interface ISavedQueryRepository
{
    Task<IReadOnlyList<SavedQueryDto>> ListAsync(Guid tenantId, Guid userId, CancellationToken ct = default);
    Task<SavedQueryDto> CreateAsync(
        Guid tenantId, Guid userId, string name, string? description, string filtersJson, bool isShared, CancellationToken ct = default);
    Task<SavedQueryDto?> UpdateAsync(
        Guid tenantId, Guid userId, Guid id, string name, string? description, string filtersJson, bool isShared, CancellationToken ct = default);
    Task<bool> DeleteAsync(Guid tenantId, Guid userId, Guid id, CancellationToken ct = default);
}

public interface IDashboardPreferencesRepository
{
    Task<DashboardPreferencesDto> GetAsync(Guid tenantId, Guid userId, CancellationToken ct = default);
    Task<DashboardPreferencesDto> UpsertAsync(Guid tenantId, Guid userId, string configJson, CancellationToken ct = default);
}
