using Flit.Infrastructure.Workers;
using Xunit;

namespace Flit.Infrastructure.Tests.Workers;

/// <summary>
/// Uso de ejemplo: validar plantillas de email de export jobs (HU #11107 AC1/AC6).
/// </summary>
public sealed class ExportJobEmailComposerTests
{
    [Fact]
    public void BuildCompleted_incluye_jobId_en_asunto_y_cuerpo()
    {
        var jobId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var msg = ExportJobEmailComposer.BuildCompleted(
            "ops@flit.test", "Ops", jobId, "procedures", "excel");

        Assert.Equal("ops@flit.test", msg.ToEmail);
        Assert.Contains("Exportación lista", msg.Subject, StringComparison.Ordinal);
        Assert.Contains(jobId.ToString("D"), msg.HtmlBody, StringComparison.Ordinal);
        Assert.Contains("Completada", msg.HtmlBody, StringComparison.Ordinal);
        Assert.DoesNotContain("{", msg.HtmlBody, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildFailed_escapa_html_en_error_hint()
    {
        var jobId = Guid.CreateVersion7();
        var msg = ExportJobEmailComposer.BuildFailed(
            "ops@flit.test", "Ops", jobId, "consolidado", "csv", "<script>x</script>");

        Assert.Contains("Exportación fallida", msg.Subject, StringComparison.Ordinal);
        Assert.Contains("&lt;script&gt;", msg.HtmlBody, StringComparison.Ordinal);
        Assert.DoesNotContain("<script>", msg.HtmlBody, StringComparison.Ordinal);
        Assert.Contains(jobId.ToString("D"), msg.HtmlBody, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildFailed_sin_hint_usa_mensaje_generico()
    {
        var msg = ExportJobEmailComposer.BuildFailed(
            "a@b.c", "A", Guid.CreateVersion7(), "sla", "pdf", null);

        Assert.Contains("reintentar", msg.HtmlBody, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Fallida", msg.HtmlBody, StringComparison.Ordinal);
    }
}

/// <summary>
/// Uso de ejemplo: reintentos con backoff ante 503 del file-manager (HU #11107 AC4).
/// </summary>
public sealed class ExportStorageRetryTests
{
    [Fact]
    public async Task Succeeds_on_third_attempt()
    {
        var attempts = 0;
        var result = await ExportStorageRetry.ExecuteAsync(
            _ =>
            {
                attempts++;
                if (attempts < 3)
                    throw new HttpRequestException("503");
                return Task.FromResult("ok");
            },
            _ => TimeSpan.Zero,
            TestContext.Current.CancellationToken);

        Assert.Equal("ok", result);
        Assert.Equal(3, attempts);
    }

    [Fact]
    public async Task Throws_after_max_attempts()
    {
        var attempts = 0;
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await ExportStorageRetry.ExecuteAsync<string>(
                _ =>
                {
                    attempts++;
                    throw new InvalidOperationException("down");
                },
                _ => TimeSpan.Zero,
                TestContext.Current.CancellationToken));

        Assert.Equal("down", ex.Message);
        Assert.Equal(ExportStorageRetry.MaxAttempts, attempts);
    }

    [Fact]
    public async Task Returns_immediately_on_first_success()
    {
        var attempts = 0;
        var result = await ExportStorageRetry.ExecuteAsync(
            _ =>
            {
                attempts++;
                return Task.FromResult(42);
            },
            _ => TimeSpan.FromHours(1),
            TestContext.Current.CancellationToken);

        Assert.Equal(42, result);
        Assert.Equal(1, attempts);
    }
}

/// <summary>
/// Uso de ejemplo: wake signal timeout actúa como polling 30 s (HU #11107 AC2).
/// </summary>
public sealed class ExportJobsWakeSignalTests
{
    [Fact]
    public async Task WaitAsync_returns_on_timeout_without_signal()
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        await ExportJobsWakeSignal.WaitAsync(TimeSpan.FromMilliseconds(50), TestContext.Current.CancellationToken);
        sw.Stop();
        Assert.True(sw.ElapsedMilliseconds >= 40, $"elapsed={sw.ElapsedMilliseconds}");
    }

    [Fact]
    public async Task WaitAsync_returns_early_when_signaled()
    {
        ExportJobsWakeSignal.Signal();
        var sw = System.Diagnostics.Stopwatch.StartNew();
        await ExportJobsWakeSignal.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        sw.Stop();
        Assert.True(sw.ElapsedMilliseconds < 2000, $"elapsed={sw.ElapsedMilliseconds}");
    }
}
