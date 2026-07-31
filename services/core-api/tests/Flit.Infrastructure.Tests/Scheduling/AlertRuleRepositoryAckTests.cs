using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Entities.Analytics;
using Flit.Infrastructure.Persistence.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Flit.Infrastructure.Tests.Scheduling;

/// <summary>
/// HU5 / E1 — acknowledge de disparos (<c>AlertRuleRepository.AckEventAsync</c>): set-once
/// idempotente y aislamiento por tenant (id ajeno → null → 404). InMemory: la operación no usa
/// SQL crudo (FirstOrDefault + SaveChanges).
/// </summary>
public sealed class AlertRuleRepositoryAckTests
{
    private static readonly Guid TenantId = Guid.NewGuid();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Ack_es_set_once_y_conserva_el_primer_reconocimiento()
    {
        var dbName = NewDbName();
        var eventId = await SeedEventAsync(dbName);
        var user1 = Guid.NewGuid();
        var user2 = Guid.NewGuid();

        await using var db = NewContext(dbName);
        var repo = new AlertRuleRepository(db);

        var first = await repo.AckEventAsync(TenantId, eventId, user1, Ct);
        first.Should().NotBeNull();
        first!.AcknowledgedAt.Should().NotBeNull();
        first.AcknowledgedBy.Should().Be(user1);

        // Segundo ack de OTRO usuario: set-once → conserva el primer reconocimiento.
        var second = await repo.AckEventAsync(TenantId, eventId, user2, Ct);
        second!.AcknowledgedBy.Should().Be(user1);
        second.AcknowledgedAt.Should().Be(first.AcknowledgedAt);
    }

    [Fact]
    public async Task Ack_de_otro_tenant_no_existe()
    {
        var dbName = NewDbName();
        var eventId = await SeedEventAsync(dbName);

        await using var db = NewContext(dbName);
        var repo = new AlertRuleRepository(db);

        var result = await repo.AckEventAsync(Guid.NewGuid(), eventId, Guid.NewGuid(), Ct);
        result.Should().BeNull();
    }

    private static string NewDbName() => $"flit-hu5-ack-{Guid.NewGuid()}";

    private static FlitDbContext NewContext(string dbName) =>
        new(new DbContextOptionsBuilder<FlitDbContext>().UseInMemoryDatabase(dbName).Options);

    private static async Task<Guid> SeedEventAsync(string dbName)
    {
        var ruleId = Guid.NewGuid();
        var eventId = Guid.NewGuid();
        await using var db = NewContext(dbName);
        db.Set<AlertRule>().Add(new AlertRule
        {
            Id = ruleId,
            TenantId = TenantId,
            Name = "Regla ICT",
            Metric = "ict_jobs_out_of_sla",
            Operator = "gt",
            Threshold = 5m,
            WindowMinutes = 1440,
            CooldownMinutes = 240,
            Recipients = ["ops@empresa.co"],
            IsActive = true,
            CreatedAt = DateTimeOffset.UnixEpoch,
        });
        db.Set<AlertEvent>().Add(new AlertEvent
        {
            Id = eventId,
            TenantId = TenantId,
            AlertRuleId = ruleId,
            TriggeredAt = DateTimeOffset.UnixEpoch,
            MetricValue = 12m,
            Threshold = 5m,
            Notified = true,
            Recipients = ["ops@empresa.co"],
            Message = "ICT: jobs fuera de SLA",
        });
        await db.SaveChangesAsync(Ct);
        return eventId;
    }
}
