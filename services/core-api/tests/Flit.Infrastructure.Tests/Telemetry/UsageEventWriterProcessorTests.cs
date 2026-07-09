using FluentAssertions;
using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Entities.Analytics;
using Flit.Infrastructure.Telemetry;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Flit.Infrastructure.Tests.Telemetry;

/// <summary>
/// Reportes2 HU-A — writer asíncrono de telemetría: drena la cola en lotes (hasta 200 por flush)
/// e inserta en <c>analytics.app_usage_events</c>; la retención borra los eventos más viejos que
/// la ventana configurada sin tocar los recientes.
/// </summary>
public sealed class UsageEventWriterProcessorTests
{
    private static readonly Guid TenantId = Guid.NewGuid();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Flush_drena_la_cola_e_inserta_los_eventos()
    {
        var dbName = NewDbName();
        var queue = new ChannelUsageEventQueue();
        var processor = NewProcessor(dbName, queue);

        queue.TryEnqueue(NewRecord("wizard_step_view", stepKey: "comprador")).Should().BeTrue();
        queue.TryEnqueue(NewRecord("module_view", module: "reportes")).Should().BeTrue();

        var written = await processor.FlushPendingAsync(Ct);

        written.Should().Be(2);
        await using var verify = NewContext(dbName);
        var rows = await verify.Set<AppUsageEvent>().OrderBy(e => e.EventType).ToListAsync(Ct);
        rows.Should().HaveCount(2);
        rows[0].EventType.Should().Be("module_view");
        rows[0].Module.Should().Be("reportes");
        rows[0].TenantId.Should().Be(TenantId);
        rows[1].EventType.Should().Be("wizard_step_view");
        rows[1].StepKey.Should().Be("comprador");
        rows[1].Metadata.Should().Be("{}");
    }

    [Fact]
    public async Task Flush_respeta_el_tope_de_200_por_lote()
    {
        var dbName = NewDbName();
        var queue = new ChannelUsageEventQueue();
        var processor = NewProcessor(dbName, queue);

        for (var i = 0; i < 250; i++)
            queue.TryEnqueue(NewRecord("api_module_access", module: "tramites"));

        var first = await processor.FlushPendingAsync(Ct);
        var second = await processor.FlushPendingAsync(Ct);

        first.Should().Be(UsageEventWriterProcessor.MaxBatchSize);
        second.Should().Be(50);
        await using var verify = NewContext(dbName);
        (await verify.Set<AppUsageEvent>().CountAsync(Ct)).Should().Be(250);
    }

    [Fact]
    public async Task Flush_sin_eventos_no_escribe_nada()
    {
        var dbName = NewDbName();
        var processor = NewProcessor(dbName, new ChannelUsageEventQueue());

        var written = await processor.FlushPendingAsync(Ct);

        written.Should().Be(0);
        await using var verify = NewContext(dbName);
        (await verify.Set<AppUsageEvent>().CountAsync(Ct)).Should().Be(0);
    }

    [Fact]
    public async Task Retencion_borra_los_eventos_viejos_y_conserva_los_recientes()
    {
        var dbName = NewDbName();
        var processor = NewProcessor(dbName, new ChannelUsageEventQueue(), retentionDays: 90);

        await using (var seed = NewContext(dbName))
        {
            seed.Set<AppUsageEvent>().AddRange(
                NewEntity(occurredAt: DateTimeOffset.UtcNow.AddDays(-120)),
                NewEntity(occurredAt: DateTimeOffset.UtcNow.AddDays(-91)),
                NewEntity(occurredAt: DateTimeOffset.UtcNow.AddDays(-10)),
                NewEntity(occurredAt: DateTimeOffset.UtcNow));
            await seed.SaveChangesAsync(Ct);
        }

        var purged = await processor.CleanupRetentionAsync(Ct);

        purged.Should().Be(2);
        await using var verify = NewContext(dbName);
        var remaining = await verify.Set<AppUsageEvent>().ToListAsync(Ct);
        remaining.Should().HaveCount(2);
        remaining.Should().OnlyContain(e => e.OccurredAt > DateTimeOffset.UtcNow.AddDays(-90));
    }

    [Fact]
    public void La_cola_llena_descarta_sin_bloquear()
    {
        var queue = new ChannelUsageEventQueue();
        for (var i = 0; i < ChannelUsageEventQueue.Capacity + 5; i++)
            queue.TryEnqueue(NewRecord("module_view", module: "tramites"));

        // Cola al tope (bounded 10 000, DropWrite): los excedentes se DESCARTAN sin bloquear —
        // al drenar solo quedan los primeros 10 000 eventos.
        var drained = 0;
        while (queue.TryDequeue(out _))
            drained++;
        drained.Should().Be(ChannelUsageEventQueue.Capacity);
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    private static string NewDbName() => $"flit-hu-a-telemetry-{Guid.NewGuid()}";

    private static FlitDbContext NewContext(string dbName) => new(
        new DbContextOptionsBuilder<FlitDbContext>().UseInMemoryDatabase(dbName).Options);

    private static UsageEventWriterProcessor NewProcessor(
        string dbName, ChannelUsageEventQueue queue, int retentionDays = 90)
    {
        var services = new ServiceCollection();
        services.AddScoped(_ => NewContext(dbName));
        var provider = services.BuildServiceProvider();
        return new UsageEventWriterProcessor(
            queue,
            provider.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new AnalyticsTelemetryOptions { RetentionDays = retentionDays }),
            NullLogger<UsageEventWriterProcessor>.Instance);
    }

    private static UsageEventRecord NewRecord(
        string eventType, string? module = null, string? stepKey = null) => new(
        TenantId,
        Guid.NewGuid(),
        eventType,
        module ?? (stepKey is null ? null : "tramites"),
        stepKey,
        ProcedureInstanceId: null,
        DurationMs: null,
        MetadataJson: "{}",
        OccurredAt: DateTimeOffset.UtcNow);

    private static AppUsageEvent NewEntity(DateTimeOffset occurredAt) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = TenantId,
        EventType = "module_view",
        Module = "tramites",
        Metadata = "{}",
        OccurredAt = occurredAt,
        CreatedAt = DateTimeOffset.UtcNow,
    };
}
