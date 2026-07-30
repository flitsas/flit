using Flit.Analytics.Application.Reporting;
using Xunit;

namespace Flit.Analytics.Application.Tests.Reporting;

/// <summary>HU #11106 — RequestExport: create+notify, límites 3 jobs / 12 meses / 50k.</summary>
public sealed class RequestExportHandlerTests
{
    [Fact]
    public async Task Ac1_Creates_pending_job_and_notifies_export_jobs_channel()
    {
        var owner = Guid.CreateVersion7();
        var repo = new FakeExportJobRepository { ActiveCount = 0, EstimatedCount = 100 };
        var handler = new RequestExportHandler(repo);

        var (result, error) = await handler.HandleAsync(
            new RequestExportCommand(
                Guid.CreateVersion7(),
                owner,
                "procedures",
                "excel",
                """{"from":"2026-01-01","to":"2026-06-01"}""",
                null),
            TestContext.Current.CancellationToken);

        Assert.Null(error);
        Assert.NotNull(result);
        Assert.Equal("pending", result!.Status);
        Assert.Equal("excel", result.Format);
        Assert.Equal(1, repo.CreateCalls);
        Assert.Equal(1, repo.NotifyCalls);
        Assert.Equal(IExportJobRepository.ExportJobsChannel, repo.LastNotifyChannel);
        Assert.Equal(result.Id, repo.LastNotifyJobId);
        Assert.Equal(owner, repo.LastCreatedBy);
    }

    [Fact]
    public async Task Ac3_Rejects_fourth_pending_job_without_insert_or_notify()
    {
        var repo = new FakeExportJobRepository { ActiveCount = 3, EstimatedCount = 10 };
        var handler = new RequestExportHandler(repo);

        var (result, error) = await handler.HandleAsync(
            new RequestExportCommand(Guid.CreateVersion7(), Guid.CreateVersion7(), "procedures", "excel", "{}", null),
            TestContext.Current.CancellationToken);

        Assert.Null(result);
        Assert.Equal("export_limit_exceeded", error);
        Assert.Equal(0, repo.CreateCalls);
        Assert.Equal(0, repo.NotifyCalls);
    }

    [Fact]
    public async Task Ac4_Rejects_date_range_wider_than_12_months()
    {
        var repo = new FakeExportJobRepository { ActiveCount = 0, EstimatedCount = 10 };
        var handler = new RequestExportHandler(repo);

        var (result, error) = await handler.HandleAsync(
            new RequestExportCommand(
                Guid.CreateVersion7(),
                Guid.CreateVersion7(),
                "procedures",
                "excel",
                """{"from":"2025-01-01","to":"2026-06-01"}""",
                null),
            TestContext.Current.CancellationToken);

        Assert.Null(result);
        Assert.Equal("date_range_too_wide", error);
        Assert.Equal(0, repo.CreateCalls);
        Assert.Equal(0, repo.NotifyCalls);
        Assert.Equal(0, repo.EstimateCalls);
    }

    [Fact]
    public async Task Ac6_Rejects_when_estimated_records_exceed_50000()
    {
        var repo = new FakeExportJobRepository { ActiveCount = 0, EstimatedCount = 60_000 };
        var handler = new RequestExportHandler(repo);

        var (result, error) = await handler.HandleAsync(
            new RequestExportCommand(
                Guid.CreateVersion7(),
                Guid.CreateVersion7(),
                "procedures",
                "excel",
                """{"from":"2026-01-01","to":"2026-06-01"}""",
                null),
            TestContext.Current.CancellationToken);

        Assert.Null(result);
        Assert.Equal("export_limit_exceeded_records", error);
        Assert.Equal(0, repo.CreateCalls);
        Assert.Equal(0, repo.NotifyCalls);
        Assert.Equal(1, repo.EstimateCalls);
    }

    [Fact]
    public async Task Creates_job_when_under_limit()
    {
        var repo = new FakeExportJobRepository { ActiveCount = 1, EstimatedCount = 100 };
        var handler = new RequestExportHandler(repo);

        var (result, error) = await handler.HandleAsync(
            new RequestExportCommand(Guid.CreateVersion7(), Guid.CreateVersion7(), "procedures", "csv", "{}", null),
            TestContext.Current.CancellationToken);

        Assert.Null(error);
        Assert.NotNull(result);
        Assert.Equal("pending", result!.Status);
        Assert.Equal("csv", result.Format);
        Assert.Equal(1, repo.NotifyCalls);
    }

    [Fact]
    public async Task Rejects_invalid_format()
    {
        var handler = new RequestExportHandler(new FakeExportJobRepository());
        var (_, error) = await handler.HandleAsync(
            new RequestExportCommand(Guid.CreateVersion7(), Guid.CreateVersion7(), "procedures", "xml", "{}", null),
            TestContext.Current.CancellationToken);
        Assert.Equal("invalid_format", error);
    }
}

public sealed class ReportingDateRangeTests
{
    [Fact]
    public void Rejects_range_wider_than_12_months()
    {
        var to = new DateOnly(2026, 7, 30);
        var from = to.AddMonths(-13);
        var (_, _, error) = ReportingDateRange.Normalize(from, to);
        Assert.Equal("date_range_too_wide", error);
    }

    [Fact]
    public void Accepts_default_30_day_window()
    {
        var (_, _, error) = ReportingDateRange.Normalize(null, null);
        Assert.Null(error);
    }
}

public sealed class ExportFilterParserTests
{
    [Fact]
    public void Parses_from_and_to_iso_dates()
    {
        var (from, to) = ExportFilterParser.TryParseDates("""{"from":"2025-01-01","to":"2026-06-01"}""");
        Assert.Equal(new DateOnly(2025, 1, 1), from);
        Assert.Equal(new DateOnly(2026, 6, 1), to);
    }
}

internal sealed class FakeExportJobRepository : IExportJobRepository
{
    public int ActiveCount { get; set; }
    public long EstimatedCount { get; set; }
    public int CreateCalls { get; private set; }
    public int NotifyCalls { get; private set; }
    public int EstimateCalls { get; private set; }
    public string? LastNotifyChannel { get; private set; }
    public Guid? LastNotifyJobId { get; private set; }
    public Guid? LastCreatedBy { get; private set; }

    public (Guid JobId, string? StoragePath, Guid OwnerUserId, string Status)? DownloadMeta { get; set; }

    public Dictionary<Guid, IReadOnlyList<ExportJobDto>> JobsByOwner { get; } = new();

    public Task<int> CountActiveJobsAsync(Guid ownerUserId, CancellationToken ct = default) =>
        Task.FromResult(ActiveCount);

    public Task<ExportJobDto> CreateAsync(
        Guid tenantId, Guid ownerUserId, string reportType, string format, string filtersJson, Guid? correlationId, CancellationToken ct = default)
    {
        CreateCalls++;
        LastCreatedBy = ownerUserId;
        return Task.FromResult(new ExportJobDto(Guid.CreateVersion7(), "pending", reportType, format, 0, DateTimeOffset.UtcNow, null, null));
    }

    public Task NotifyChannelAsync(string channel, Guid jobId, CancellationToken ct = default)
    {
        NotifyCalls++;
        LastNotifyChannel = channel;
        LastNotifyJobId = jobId;
        return Task.CompletedTask;
    }

    public Task<long> EstimateRecordCountAsync(
        Guid tenantId, string reportType, string filtersJson, CancellationToken ct = default)
    {
        EstimateCalls++;
        return Task.FromResult(EstimatedCount);
    }

    public Task<ExportJobDto?> GetAsync(Guid jobId, CancellationToken ct = default) =>
        Task.FromResult<ExportJobDto?>(null);

    public Task<IReadOnlyList<ExportJobDto>> ListByOwnerAsync(Guid ownerUserId, CancellationToken ct = default) =>
        Task.FromResult(JobsByOwner.TryGetValue(ownerUserId, out var items) ? items : []);

    public Task<(string? StoragePath, Guid OwnerUserId, string Status)?> GetDownloadMetaAsync(Guid jobId, CancellationToken ct = default)
    {
        if (DownloadMeta is null || DownloadMeta.Value.JobId != jobId)
            return Task.FromResult<(string?, Guid, string)?>(null);
        var m = DownloadMeta.Value;
        return Task.FromResult<(string?, Guid, string)?>((m.StoragePath, m.OwnerUserId, m.Status));
    }
}
