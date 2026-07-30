using Flit.Analytics.Application.Reporting;
using Xunit;

namespace Flit.Analytics.Application.Tests.Reporting;

public sealed class RequestExportHandlerTests
{
    [Fact]
    public async Task Rejects_fourth_pending_job()
    {
        var repo = new FakeExportJobRepository { ActiveCount = 3 };
        var handler = new RequestExportHandler(repo);

        var (result, error) = await handler.HandleAsync(
            new RequestExportCommand(Guid.CreateVersion7(), Guid.CreateVersion7(), "procedures", "excel", "{}", null),
            TestContext.Current.CancellationToken);

        Assert.Null(result);
        Assert.Equal("export_limit_exceeded", error);
    }

    [Fact]
    public async Task Creates_job_when_under_limit()
    {
        var repo = new FakeExportJobRepository { ActiveCount = 1 };
        var handler = new RequestExportHandler(repo);

        var (result, error) = await handler.HandleAsync(
            new RequestExportCommand(Guid.CreateVersion7(), Guid.CreateVersion7(), "procedures", "csv", "{}", null),
            TestContext.Current.CancellationToken);

        Assert.Null(error);
        Assert.NotNull(result);
        Assert.Equal("pending", result!.Status);
        Assert.Equal("csv", result.Format);
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

internal sealed class FakeExportJobRepository : IExportJobRepository
{
    public int ActiveCount { get; set; }

    public (Guid JobId, string? StoragePath, Guid OwnerUserId, string Status)? DownloadMeta { get; set; }

    public Dictionary<Guid, IReadOnlyList<ExportJobDto>> JobsByOwner { get; } = new();

    public Task<int> CountActiveJobsAsync(Guid ownerUserId, CancellationToken ct = default) =>
        Task.FromResult(ActiveCount);

    public Task<ExportJobDto> CreateAsync(
        Guid tenantId, Guid ownerUserId, string reportType, string format, string filtersJson, Guid? correlationId, CancellationToken ct = default) =>
        Task.FromResult(new ExportJobDto(Guid.CreateVersion7(), "pending", reportType, format, 0, DateTimeOffset.UtcNow, null, null));

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
