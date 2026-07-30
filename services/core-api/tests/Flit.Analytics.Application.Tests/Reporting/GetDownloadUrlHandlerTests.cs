using Flit.Analytics.Application.Reporting;
using Xunit;

namespace Flit.Analytics.Application.Tests.Reporting;

/// <summary>HU #11106 — GetDownloadUrlHandler: ownership, TTL ≤15 min, IDOR.</summary>
public sealed class GetDownloadUrlHandlerTests
{
    [Fact]
    public async Task Ac5_Returns_forbidden_when_job_belongs_to_another_user()
    {
        var owner = Guid.CreateVersion7();
        var caller = Guid.CreateVersion7();
        var jobId = Guid.CreateVersion7();
        var repo = new FakeExportJobRepository
        {
            DownloadMeta = (jobId, "store/path", owner, "completed"),
        };
        var storage = new FakeExportFileStorage();
        var handler = new GetDownloadUrlHandler(repo, storage);

        var (result, error) = await handler.HandleAsync(jobId, caller, TestContext.Current.CancellationToken);

        Assert.Null(result);
        Assert.Equal("forbidden", error);
        Assert.Equal(0, storage.Calls);
    }

    [Fact]
    public async Task Ac2_Returns_download_url_with_ttl_at_most_15_minutes()
    {
        var owner = Guid.CreateVersion7();
        var jobId = Guid.CreateVersion7();
        var repo = new FakeExportJobRepository
        {
            DownloadMeta = (jobId, "store/path", owner, "completed"),
        };
        var tooLong = DateTimeOffset.UtcNow.AddMinutes(60);
        var storage = new FakeExportFileStorage { Url = ("https://files.test/x", tooLong) };
        var handler = new GetDownloadUrlHandler(repo, storage);

        var before = DateTimeOffset.UtcNow;
        var (result, error) = await handler.HandleAsync(jobId, owner, TestContext.Current.CancellationToken);

        Assert.Null(error);
        Assert.NotNull(result);
        Assert.Equal("https://files.test/x", result!.DownloadUrl);
        Assert.True(result.ExpiresAt <= before.AddMinutes(15).AddSeconds(2));
        Assert.True(result.ExpiresAt < tooLong);
        Assert.Equal(1, storage.Calls);
    }

    [Fact]
    public async Task Returns_not_ready_when_still_processing()
    {
        var owner = Guid.CreateVersion7();
        var jobId = Guid.CreateVersion7();
        var repo = new FakeExportJobRepository
        {
            DownloadMeta = (jobId, null, owner, "processing"),
        };
        var handler = new GetDownloadUrlHandler(repo, new FakeExportFileStorage());

        var (_, error) = await handler.HandleAsync(jobId, owner, TestContext.Current.CancellationToken);
        Assert.Equal("not_ready", error);
    }
}

/// <summary>Listado solo del owner (HU #11108).</summary>
public sealed class GetExportJobHandlerListTests
{
    [Fact]
    public async Task ListAsync_returns_only_repo_owner_jobs()
    {
        var owner = Guid.CreateVersion7();
        var other = Guid.CreateVersion7();
        var repo = new FakeExportJobRepository
        {
            JobsByOwner =
            {
                [owner] =
                [
                    new ExportJobDto(Guid.CreateVersion7(), "completed", "procedures", "excel", 100, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, null),
                    new ExportJobDto(Guid.CreateVersion7(), "completed", "sla", "csv", 100, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, null),
                ],
                [other] =
                [
                    new ExportJobDto(Guid.CreateVersion7(), "completed", "procedures", "pdf", 100, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, null),
                ],
            },
        };

        var items = await new GetExportJobHandler(repo).ListAsync(owner, TestContext.Current.CancellationToken);
        Assert.Equal(2, items.Count);
    }
}

internal sealed class FakeExportFileStorage : IExportFileStorage
{
    public (string Url, DateTimeOffset ExpiresAt)? Url { get; set; }
    public int Calls { get; private set; }

    public Task<(string StoragePath, string Sha256, long SizeBytes)> SaveExportAsync(
        Guid jobId, string format, string fileName, Stream content, CancellationToken ct = default) =>
        throw new NotSupportedException();

    public Task<(string Url, DateTimeOffset ExpiresAt)?> GetDownloadUrlAsync(
        string storagePath, CancellationToken ct = default)
    {
        Calls++;
        return Task.FromResult(Url);
    }
}
