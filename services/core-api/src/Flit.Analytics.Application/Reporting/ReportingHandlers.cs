using System.Text.Json;
using Flit.Analytics.Application.Reporting;

namespace Flit.Analytics.Application.Reporting;

public static class ReportingDateRange
{
    public const int DefaultDays = 30;
    public const int MaxMonths = 12;

    public static (DateOnly From, DateOnly To, string? Error) Normalize(DateOnly? from, DateOnly? to)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var effectiveTo = to ?? today;
        var effectiveFrom = from ?? effectiveTo.AddDays(-(DefaultDays - 1));
        if (effectiveFrom > effectiveTo)
            return (default, default, "invalid_range");
        var maxFrom = effectiveTo.AddMonths(-MaxMonths);
        if (effectiveFrom < maxFrom)
            return (default, default, "date_range_too_wide");
        return (effectiveFrom, effectiveTo, null);
    }
}

public sealed class GetReportingProceduresHandler(IReportingReadRepository repo)
{
    public const int DefaultPageSize = 50;
    public const int MaxPageSize = 200;

    private static readonly HashSet<string> DateTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "created_at", "updated_at", "completed_at"
    };

    private static readonly HashSet<string> SortBy = new(StringComparer.OrdinalIgnoreCase)
    {
        "created_at", "status", "procedure_type", "elapsed_hours"
    };

    public async Task<(ReportingProceduresPageDto? Result, string? Error)> HandleAsync(
        Guid tenantId,
        DateOnly? from,
        DateOnly? to,
        string? dateType,
        Guid? transitOfficeId,
        string? procedureType,
        string? status,
        string? search,
        string? sortBy,
        string? sortOrder,
        int? page,
        int? pageSize,
        CancellationToken ct = default)
    {
        var (f, t, err) = ReportingDateRange.Normalize(from, to);
        if (err is not null) return (null, err);

        var dt = string.IsNullOrWhiteSpace(dateType) ? "created_at" : dateType.Trim();
        if (!DateTypes.Contains(dt)) return (null, "invalid_date_type");

        var sb = string.IsNullOrWhiteSpace(sortBy) ? "created_at" : sortBy.Trim();
        if (!SortBy.Contains(sb)) return (null, "invalid_sort");

        var so = string.Equals(sortOrder, "asc", StringComparison.OrdinalIgnoreCase) ? "asc" : "desc";
        var p = page is null or <= 0 ? 1 : page.Value;
        var ps = pageSize is null or <= 0 ? DefaultPageSize : Math.Min(pageSize.Value, MaxPageSize);

        var filter = new ReportingProceduresFilter(
            tenantId, f, t, dt.ToLowerInvariant(), transitOfficeId,
            Normalize(procedureType), Normalize(status), Normalize(search),
            sb.ToLowerInvariant(), so);

        var result = await repo.GetProceduresAsync(filter, p, ps, ct).ConfigureAwait(false);
        return (result, null);
    }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public sealed class GetReportingAuditHandler(IReportingReadRepository repo)
{
    public Task<ReportingAuditDto> HandleAsync(Guid tenantId, Guid procedureId, CancellationToken ct = default) =>
        repo.GetAuditAsync(tenantId, procedureId, ct);
}

public sealed class GetConsolidadoHandler(IReportingReadRepository repo)
{
    private static readonly HashSet<string> Groups = new(StringComparer.OrdinalIgnoreCase)
    {
        "ot", "empresa", "gestor", "estado", "tipo", "mes"
    };

    public async Task<(ConsolidadoPageDto? Result, string? Error)> HandleAsync(
        Guid tenantId, DateOnly? from, DateOnly? to, string? groupBy, CancellationToken ct = default)
    {
        var (f, t, err) = ReportingDateRange.Normalize(from, to);
        if (err is not null) return (null, err);
        var g = string.IsNullOrWhiteSpace(groupBy) ? "estado" : groupBy.Trim();
        if (!Groups.Contains(g)) return (null, "invalid_group");
        var result = await repo.GetConsolidadoAsync(tenantId, f, t, g.ToLowerInvariant(), ct).ConfigureAwait(false);
        return (result, null);
    }
}

public sealed class GetProductivityReportHandler(IReportingReadRepository repo)
{
    private static readonly HashSet<string> Dimensions = new(StringComparer.OrdinalIgnoreCase)
    {
        "ot", "empresa", "gestor", "usuario"
    };

    public async Task<(ProductivityPageDto? Result, string? Error)> HandleAsync(
        Guid tenantId, DateOnly? from, DateOnly? to, string? dimension, CancellationToken ct = default)
    {
        var (f, t, err) = ReportingDateRange.Normalize(from, to);
        if (err is not null) return (null, err);
        var d = string.IsNullOrWhiteSpace(dimension) ? "usuario" : dimension.Trim();
        if (!Dimensions.Contains(d)) return (null, "invalid_dimension");
        var result = await repo.GetProductivityAsync(tenantId, f, t, d.ToLowerInvariant(), ct).ConfigureAwait(false);
        return (result, null);
    }
}

public sealed class GetSlaReportHandler(IReportingReadRepository repo)
{
    public async Task<(SlaPageDto? Result, string? Error)> HandleAsync(
        Guid tenantId, DateOnly? from, DateOnly? to, CancellationToken ct = default)
    {
        var (f, t, err) = ReportingDateRange.Normalize(from, to);
        if (err is not null) return (null, err);
        var result = await repo.GetSlaAsync(tenantId, f, t, ct).ConfigureAwait(false);
        return (result, null);
    }
}

public sealed record RequestExportCommand(
    Guid TenantId,
    Guid OwnerUserId,
    string ReportType,
    string Format,
    string FiltersJson,
    Guid? CorrelationId);

public sealed class RequestExportHandler(IExportJobRepository repo)
{
    private static readonly HashSet<string> Types = new(StringComparer.OrdinalIgnoreCase)
    {
        "procedures", "consolidado", "productivity", "sla"
    };

    private static readonly HashSet<string> Formats = new(StringComparer.OrdinalIgnoreCase)
    {
        "excel", "csv", "pdf"
    };

    public const int MaxPendingJobs = 3;
    public const int MaxExportRecords = 50_000;

    public async Task<(ExportJobDto? Result, string? Error)> HandleAsync(
        RequestExportCommand cmd, CancellationToken ct = default)
    {
        if (!Types.Contains(cmd.ReportType)) return (null, "invalid_report_type");
        if (!Formats.Contains(cmd.Format)) return (null, "invalid_format");

        var filters = string.IsNullOrWhiteSpace(cmd.FiltersJson) ? "{}" : cmd.FiltersJson;
        try { using var _ = JsonDocument.Parse(filters); }
        catch (JsonException) { return (null, "invalid_filters"); }

        var (from, to) = ExportFilterParser.TryParseDates(filters);
        var (_, _, rangeError) = ReportingDateRange.Normalize(from, to);
        if (rangeError is not null) return (null, rangeError);

        var active = await repo.CountActiveJobsAsync(cmd.OwnerUserId, ct).ConfigureAwait(false);
        if (active >= MaxPendingJobs) return (null, "export_limit_exceeded");

        var estimated = await repo.EstimateRecordCountAsync(
            cmd.TenantId, cmd.ReportType.ToLowerInvariant(), filters, ct).ConfigureAwait(false);
        if (estimated > MaxExportRecords) return (null, "export_limit_exceeded_records");

        var reportType = cmd.ReportType.ToLowerInvariant();
        var format = cmd.Format.ToLowerInvariant();
        var job = await repo.CreateAsync(
            cmd.TenantId,
            cmd.OwnerUserId,
            reportType,
            format,
            filters,
            cmd.CorrelationId,
            ct).ConfigureAwait(false);

        await repo.NotifyChannelAsync(IExportJobRepository.ExportJobsChannel, job.Id, ct)
            .ConfigureAwait(false);

        return (job, null);
    }
}

/// <summary>Extrae from/to del JSON de filtros de exportación (FE envía <c>from</c>/<c>to</c>).</summary>
public static class ExportFilterParser
{
    public static (DateOnly? From, DateOnly? To) TryParseDates(string filtersJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(filtersJson) ? "{}" : filtersJson);
            var root = doc.RootElement;
            return (TryDate(root, "from") ?? TryDate(root, "dateFrom"),
                    TryDate(root, "to") ?? TryDate(root, "dateTo"));
        }
        catch (JsonException)
        {
            return (null, null);
        }
    }

    private static DateOnly? TryDate(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var el) || el.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            return null;
        var raw = el.ValueKind == JsonValueKind.String ? el.GetString() : el.ToString();
        if (string.IsNullOrWhiteSpace(raw)) return null;
        if (DateOnly.TryParse(raw, out var d)) return d;
        if (DateTimeOffset.TryParse(raw, out var dto)) return DateOnly.FromDateTime(dto.UtcDateTime);
        return null;
    }
}

public sealed class GetExportJobHandler(IExportJobRepository repo)
{
    public Task<ExportJobDto?> HandleAsync(Guid jobId, CancellationToken ct = default) =>
        repo.GetAsync(jobId, ct);

    public async Task<(ExportJobDto? Result, string? Error)> HandleForOwnerAsync(
        Guid jobId, Guid callerUserId, CancellationToken ct = default)
    {
        var meta = await repo.GetDownloadMetaAsync(jobId, ct).ConfigureAwait(false);
        if (meta is null) return (null, "not_found");
        if (meta.Value.OwnerUserId != callerUserId) return (null, "forbidden");
        var job = await repo.GetAsync(jobId, ct).ConfigureAwait(false);
        return (job, null);
    }

    public Task<IReadOnlyList<ExportJobDto>> ListAsync(Guid ownerUserId, CancellationToken ct = default) =>
        repo.ListByOwnerAsync(ownerUserId, ct);
}

public sealed class GetDownloadUrlHandler(IExportJobRepository repo, IExportFileStorage storage)
{
    public const int MaxDownloadTtlMinutes = 15;

    public async Task<(DownloadUrlDto? Result, string? Error)> HandleAsync(
        Guid jobId, Guid callerUserId, CancellationToken ct = default)
    {
        var meta = await repo.GetDownloadMetaAsync(jobId, ct).ConfigureAwait(false);
        if (meta is null) return (null, "not_found");
        if (meta.Value.OwnerUserId != callerUserId) return (null, "forbidden");
        if (!string.Equals(meta.Value.Status, "completed", StringComparison.OrdinalIgnoreCase))
            return (null, "not_ready");
        if (string.IsNullOrWhiteSpace(meta.Value.StoragePath)) return (null, "not_ready");

        var url = await storage.GetDownloadUrlAsync(meta.Value.StoragePath, ct).ConfigureAwait(false);
        if (url is null) return (null, "storage_unavailable");

        var maxExpiry = DateTimeOffset.UtcNow.AddMinutes(MaxDownloadTtlMinutes);
        var expiresAt = url.Value.ExpiresAt > maxExpiry ? maxExpiry : url.Value.ExpiresAt;
        return (new DownloadUrlDto(url.Value.Url, expiresAt), null);
    }
}
