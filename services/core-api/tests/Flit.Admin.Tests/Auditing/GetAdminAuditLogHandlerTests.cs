using Flit.Admin.Application.Auditing.GetAdminAuditLog;
using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Entities.Admin;
using Flit.Infrastructure.Persistence.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Flit.Admin.Tests.Auditing;

/// <summary>
/// Consulta global (cross-tenant) del rastro unificado de auditoría administrativo/
/// seguridad, exclusiva de SuperAdmin (HU #10679). Ejercita el handler real sobre el
/// repositorio EF real (<see cref="AdminAuditLogRepository"/>) con proveedor InMemory —
/// mismo patrón que <c>TenantAuditLogHandlerTests</c> (HU #10192). Cubre AC2 (filtro por
/// usuario actor o afectado), AC3 (filtro por compañía/OT y tipo de tenant), AC4 (rango
/// de fechas) y AC5 (paginación, orden y normalización).
/// </summary>
public sealed class GetAdminAuditLogHandlerTests
{
    // ── AC2: filtro por usuario actor o afectado ───────────────────────────────────

    [Fact]
    public async Task AC2_UserId_MatchesActorOrAffected_ButNotUnrelatedRows()
    {
        var db = NewDbName();
        var targetUser = Guid.NewGuid();
        var otherUser = Guid.NewGuid();
        var actorRowId = Guid.NewGuid();
        var affectedRowId = Guid.NewGuid();

        await using (var seed = NewContext(db))
        {
            // targetUser es el ACTOR (changed_by) de esta fila.
            seed.TenantConfigAuditLogs.Add(new TenantConfigAuditLog
            {
                Id = actorRowId,
                EntityName = "users",
                ChangedBy = targetUser,
                TargetEntityId = otherUser,
                Module = "users",
                Operation = "update",
                Result = "success",
                ChangedAt = new DateTimeOffset(2026, 1, 1, 10, 0, 0, TimeSpan.Zero),
            });

            // targetUser es el AFECTADO (target_entity_id) de esta fila.
            seed.TenantConfigAuditLogs.Add(new TenantConfigAuditLog
            {
                Id = affectedRowId,
                EntityName = "roles",
                ChangedBy = otherUser,
                TargetEntityId = targetUser,
                Module = "roles",
                Operation = "update",
                Result = "success",
                ChangedAt = new DateTimeOffset(2026, 1, 1, 11, 0, 0, TimeSpan.Zero),
            });

            // Fila sin relación con targetUser: no debe aparecer.
            seed.TenantConfigAuditLogs.Add(new TenantConfigAuditLog
            {
                Id = Guid.NewGuid(),
                EntityName = "users",
                ChangedBy = otherUser,
                TargetEntityId = Guid.NewGuid(),
                Module = "users",
                Operation = "update",
                Result = "success",
                ChangedAt = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero),
            });

            seed.SaveChanges();
        }

        await using var ctx = NewContext(db);
        var handler = new GetAdminAuditLogHandler(new AdminAuditLogRepository(ctx));

        var result = await handler.HandleAsync(
            new GetAdminAuditLogQuery(targetUser, null, null, null, null, null, null, null, null, null),
            TestContext.Current.CancellationToken);

        result.TotalCount.Should().Be(2);
        result.Data.Select(e => e.Id).Should().BeEquivalentTo([actorRowId, affectedRowId]);
    }

    // ── AC3: filtro por compañía/OT (tenantId) y tipo de tenant ────────────────────

    [Fact]
    public async Task AC3_TenantId_ReturnsOnlyRowsOfThatTenant()
    {
        var db = NewDbName();
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();

        await using (var seed = NewContext(db))
        {
            seed.TenantConfigAuditLogs.Add(NewRow(tenantId: tenantA, tenantType: "COMPANY"));
            seed.TenantConfigAuditLogs.Add(NewRow(tenantId: tenantA, tenantType: "COMPANY"));
            seed.TenantConfigAuditLogs.Add(NewRow(tenantId: tenantB, tenantType: "COMPANY"));
            seed.SaveChanges();
        }

        await using var ctx = NewContext(db);
        var handler = new GetAdminAuditLogHandler(new AdminAuditLogRepository(ctx));

        var result = await handler.HandleAsync(
            new GetAdminAuditLogQuery(null, tenantA, null, null, null, null, null, null, null, null),
            TestContext.Current.CancellationToken);

        result.TotalCount.Should().Be(2);
        result.Data.Should().OnlyContain(e => e.TenantId == tenantA);
    }

    [Fact]
    public async Task AC3_TenantType_ReturnsOnlyThatType()
    {
        var db = NewDbName();

        await using (var seed = NewContext(db))
        {
            seed.TenantConfigAuditLogs.Add(NewRow(tenantId: Guid.NewGuid(), tenantType: "COMPANY"));
            seed.TenantConfigAuditLogs.Add(NewRow(tenantId: Guid.NewGuid(), tenantType: "TRANSIT_OFFICE"));
            seed.TenantConfigAuditLogs.Add(NewRow(tenantId: Guid.NewGuid(), tenantType: "TRANSIT_OFFICE"));
            seed.SaveChanges();
        }

        await using var ctx = NewContext(db);
        var handler = new GetAdminAuditLogHandler(new AdminAuditLogRepository(ctx));

        var result = await handler.HandleAsync(
            new GetAdminAuditLogQuery(null, null, "TRANSIT_OFFICE", null, null, null, null, null, null, null),
            TestContext.Current.CancellationToken);

        result.TotalCount.Should().Be(2);
        result.Data.Should().OnlyContain(e => e.TenantType == "TRANSIT_OFFICE");
    }

    // ── AC4: rango de fechas ────────────────────────────────────────────────────────

    [Fact]
    public async Task AC4_DateRange_ReturnsOnlyRowsWithinRange()
    {
        var db = NewDbName();
        var inRangeId1 = Guid.NewGuid();
        var inRangeId2 = Guid.NewGuid();

        await using (var seed = NewContext(db))
        {
            // Fuera del rango: antes de DateFrom.
            seed.TenantConfigAuditLogs.Add(NewRow(changedAt: new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero)));

            // Dentro del rango (bordes inclusive).
            seed.TenantConfigAuditLogs.Add(NewRow(id: inRangeId1, changedAt: new DateTimeOffset(2026, 2, 1, 0, 0, 0, TimeSpan.Zero)));
            seed.TenantConfigAuditLogs.Add(NewRow(id: inRangeId2, changedAt: new DateTimeOffset(2026, 2, 15, 0, 0, 0, TimeSpan.Zero)));

            // Fuera del rango: después de DateTo.
            seed.TenantConfigAuditLogs.Add(NewRow(changedAt: new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero)));

            seed.SaveChanges();
        }

        await using var ctx = NewContext(db);
        var handler = new GetAdminAuditLogHandler(new AdminAuditLogRepository(ctx));

        var result = await handler.HandleAsync(
            new GetAdminAuditLogQuery(
                null, null, null, null, null, null,
                DateFrom: new DateTimeOffset(2026, 2, 1, 0, 0, 0, TimeSpan.Zero),
                DateTo: new DateTimeOffset(2026, 2, 28, 0, 0, 0, TimeSpan.Zero),
                Page: null, PageSize: null),
            TestContext.Current.CancellationToken);

        result.TotalCount.Should().Be(2);
        result.Data.Select(e => e.Id).Should().BeEquivalentTo([inRangeId1, inRangeId2]);
    }

    // ── AC5: paginación, orden y normalización ──────────────────────────────────────

    [Fact]
    public async Task AC5_ReturnsNewestFirst_AndPaginates()
    {
        var db = NewDbName();
        var baseTime = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        await using (var seed = NewContext(db))
        {
            for (var i = 0; i < 25; i++)
            {
                seed.TenantConfigAuditLogs.Add(NewRow(changedAt: baseTime.AddMinutes(i), operation: $"op-{i}"));
            }

            seed.SaveChanges();
        }

        await using var ctx = NewContext(db);
        var handler = new GetAdminAuditLogHandler(new AdminAuditLogRepository(ctx));

        var page1 = await handler.HandleAsync(
            new GetAdminAuditLogQuery(null, null, null, null, null, null, null, null, Page: 1, PageSize: 20),
            TestContext.Current.CancellationToken);

        page1.TotalCount.Should().Be(25);
        page1.Page.Should().Be(1);
        page1.PageSize.Should().Be(20);
        page1.Data.Should().HaveCount(20);
        page1.Data[0].Operation.Should().Be("op-24"); // último insertado (más reciente).
        page1.Data.Should().BeInDescendingOrder(e => e.ChangedAt);

        var page2 = await handler.HandleAsync(
            new GetAdminAuditLogQuery(null, null, null, null, null, null, null, null, Page: 2, PageSize: 20),
            TestContext.Current.CancellationToken);

        page2.Data.Should().HaveCount(5);
        page2.Data[0].Operation.Should().Be("op-4");
    }

    [Fact]
    public async Task AC5_PageSizeAboveMax_IsCappedTo100()
    {
        var db = NewDbName();

        await using (var seed = NewContext(db))
        {
            seed.TenantConfigAuditLogs.Add(NewRow());
            seed.SaveChanges();
        }

        await using var ctx = NewContext(db);
        var handler = new GetAdminAuditLogHandler(new AdminAuditLogRepository(ctx));

        var result = await handler.HandleAsync(
            new GetAdminAuditLogQuery(null, null, null, null, null, null, null, null, Page: 1, PageSize: 500),
            TestContext.Current.CancellationToken);

        result.PageSize.Should().Be(GetAdminAuditLogHandler.MaxPageSize);
        result.PageSize.Should().Be(100);
    }

    [Fact]
    public async Task AC5_InvalidPaging_NormalizesToDefaults()
    {
        var db = NewDbName();

        await using (var seed = NewContext(db))
        {
            seed.TenantConfigAuditLogs.Add(NewRow());
            seed.SaveChanges();
        }

        await using var ctx = NewContext(db);
        var handler = new GetAdminAuditLogHandler(new AdminAuditLogRepository(ctx));

        var result = await handler.HandleAsync(
            new GetAdminAuditLogQuery(null, null, null, null, null, null, null, null, Page: -5, PageSize: 0),
            TestContext.Current.CancellationToken);

        result.Page.Should().Be(GetAdminAuditLogHandler.DefaultPage);
        result.PageSize.Should().Be(GetAdminAuditLogHandler.DefaultPageSize);
        result.Data.Should().ContainSingle();
    }

    [Fact]
    public async Task AC5_ReturnsEmpty_WhenNoAuditEntries()
    {
        await using var ctx = NewContext(NewDbName());
        var handler = new GetAdminAuditLogHandler(new AdminAuditLogRepository(ctx));

        var result = await handler.HandleAsync(
            new GetAdminAuditLogQuery(null, null, null, null, null, null, null, null, null, null),
            TestContext.Current.CancellationToken);

        result.TotalCount.Should().Be(0);
        result.Data.Should().BeEmpty();
    }

    private static TenantConfigAuditLog NewRow(
        Guid? id = null,
        Guid? tenantId = null,
        string? tenantType = null,
        DateTimeOffset? changedAt = null,
        string? operation = null) => new()
    {
        Id = id ?? Guid.NewGuid(),
        TenantId = tenantId,
        TenantType = tenantType,
        EntityName = "users",
        Module = "users",
        Operation = operation ?? "update",
        Result = "success",
        ChangedBy = Guid.NewGuid(),
        ChangedAt = changedAt ?? DateTimeOffset.UtcNow,
    };

    private static string NewDbName() => $"flit-admin-audit-log-{Guid.NewGuid()}";

    private static FlitDbContext NewContext(string dbName) =>
        new(new DbContextOptionsBuilder<FlitDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options);
}
