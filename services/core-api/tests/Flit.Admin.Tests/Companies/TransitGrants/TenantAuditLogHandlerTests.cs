using Flit.Admin.Application.Companies.TransitOffices.GetTenantAuditLog;
using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Entities.Admin;
using Flit.Infrastructure.Persistence.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Flit.Admin.Tests.Companies.TransitGrants;

/// <summary>
/// Consulta paginada del historial de auditoría de gobernanza (HU #10192, AC4) — orden
/// por <c>changed_at</c> descendente, paginación y normalización de page/pageSize.
/// Ejercita el handler real sobre el repositorio EF real con proveedor InMemory.
///
/// Uso de ejemplo:
/// <code>
/// var handler = new GetTenantAuditLogHandler(new TenantAuditLogRepository(ctx));
/// var page = await handler.HandleAsync(new GetTenantAuditLogQuery { TenantId = id, Page = 1, PageSize = 20 });
/// </code>
/// </summary>
public sealed class TenantAuditLogHandlerTests
{
    [Fact]
    public async Task AC4_ReturnsNewestFirst_AndPaginates()
    {
        var db = NewDbName();
        var tenantId = Guid.NewGuid();
        var baseTime = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        await using (var seed = NewContext(db))
        {
            for (var i = 0; i < 25; i++)
            {
                seed.TenantConfigAuditLogs.Add(new TenantConfigAuditLog
                {
                    Id = Guid.NewGuid(),
                    TenantId = tenantId,
                    EntityName = "tenant_transit_office_grants",
                    FieldName = "transit_office_id",
                    NewValue = $"\"office-{i}\"",
                    ChangedAt = baseTime.AddMinutes(i), // i creciente ⇒ más reciente
                    ChangedBy = Guid.NewGuid(),
                });
            }

            seed.SaveChanges();
        }

        await using var ctx = NewContext(db);
        var handler = new GetTenantAuditLogHandler(new TenantAuditLogRepository(ctx));

        var page1 = await handler.HandleAsync(new GetTenantAuditLogQuery
        {
            TenantId = tenantId,
            Page = 1,
            PageSize = 20,
        }, TestContext.Current.CancellationToken);

        page1.TotalCount.Should().Be(25);
        page1.Page.Should().Be(1);
        page1.PageSize.Should().Be(20);
        page1.Data.Should().HaveCount(20);
        // Más reciente primero: el último insertado (i=24).
        page1.Data[0].NewValue.Should().Be("\"office-24\"");
        page1.Data.Should().BeInDescendingOrder(e => e.ChangedAt);

        var page2 = await handler.HandleAsync(new GetTenantAuditLogQuery
        {
            TenantId = tenantId,
            Page = 2,
            PageSize = 20,
        }, TestContext.Current.CancellationToken);

        page2.Data.Should().HaveCount(5);
        page2.Data[0].NewValue.Should().Be("\"office-4\"");
    }

    [Fact]
    public async Task AC4_NormalizesInvalidPaging_ToDefaults()
    {
        var db = NewDbName();
        var tenantId = Guid.NewGuid();

        await using (var seed = NewContext(db))
        {
            seed.TenantConfigAuditLogs.Add(new TenantConfigAuditLog
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                EntityName = "tenant_transit_office_grants",
                FieldName = "transit_office_id",
                ChangedAt = DateTimeOffset.UtcNow,
            });
            seed.SaveChanges();
        }

        await using var ctx = NewContext(db);
        var handler = new GetTenantAuditLogHandler(new TenantAuditLogRepository(ctx));

        var result = await handler.HandleAsync(new GetTenantAuditLogQuery
        {
            TenantId = tenantId,
            Page = -5,
            PageSize = 0,
        }, TestContext.Current.CancellationToken);

        result.Page.Should().Be(GetTenantAuditLogHandler.DefaultPage);
        result.PageSize.Should().Be(GetTenantAuditLogHandler.DefaultPageSize);
        result.Data.Should().ContainSingle();
    }

    [Fact]
    public async Task AC4_ReturnsEmpty_WhenNoAuditEntries()
    {
        await using var ctx = NewContext(NewDbName());
        var handler = new GetTenantAuditLogHandler(new TenantAuditLogRepository(ctx));

        var result = await handler.HandleAsync(new GetTenantAuditLogQuery { TenantId = Guid.NewGuid() }, TestContext.Current.CancellationToken);

        result.TotalCount.Should().Be(0);
        result.Data.Should().BeEmpty();
    }

    private static string NewDbName() => $"flit-audit-log-{Guid.NewGuid()}";

    private static FlitDbContext NewContext(string dbName) =>
        new(new DbContextOptionsBuilder<FlitDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options);
}
