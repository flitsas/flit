using System.Text.Json;
using Flit.Admin.Application.Companies.TransitOffices.SetTransitOfficeTenantStatus;
using Flit.Admin.Domain.Companies.TransitOffices;
using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Entities.Admin;
using Flit.Infrastructure.Persistence.Entities.Identity;
using Flit.Infrastructure.Persistence.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Flit.Admin.Tests.Companies.TransitOffices;

/// <summary>
/// Auditoría del ciclo de vida OT (HU #10518, RF06): activar/desactivar el tenant OT deja
/// una fila en <c>admin.tenant_config_audit_logs</c> en la misma operación, de forma
/// idempotente (sin duplicar cuando el estado no cambia). Proveedor InMemory.
/// </summary>
public sealed class SetTransitOfficeTenantStatusHandlerTests
{
    [Fact]
    public async Task Deactivate_WritesAuditLog_AndIsConsultable()
    {
        var db = NewDbName();
        var tenantId = Guid.NewGuid();
        var changedBy = Guid.NewGuid();
        await SeedOtTenant(db, tenantId, isActive: true);

        await using var ctx = NewContext(db);
        var handler = new SetTransitOfficeTenantStatusHandler(new TransitOfficeTenantWriteRepository(ctx));

        var result = await handler.HandleAsync(
            new SetTransitOfficeTenantStatusCommand
            {
                TenantId = tenantId,
                EstadoActivo = false,
                ChangedBy = changedBy,
            },
            TestContext.Current.CancellationToken);

        result.Outcome.Should().Be(SetTransitOfficeTenantStatusOutcome.Updated);
        result.Changed.Should().BeTrue();
        result.EstadoActivo.Should().BeFalse();

        // El tenant quedó inactivo.
        (await ctx.Tenants.AsNoTracking().FirstAsync(t => t.Id == tenantId, TestContext.Current.CancellationToken))
            .IsActive.Should().BeFalse();

        // Se registró exactamente una fila de auditoría con el cambio de is_active.
        var audits = await ctx.TenantConfigAuditLogs.AsNoTracking()
            .Where(a => a.TenantId == tenantId)
            .ToListAsync(TestContext.Current.CancellationToken);
        audits.Should().ContainSingle();
        var audit = audits.Single();
        audit.EntityName.Should().Be("transit_office_tenant");
        audit.FieldName.Should().Be("is_active");
        audit.OldValue.Should().Be(JsonSerializer.Serialize(true));
        audit.NewValue.Should().Be(JsonSerializer.Serialize(false));
        audit.ChangedBy.Should().Be(changedBy);

        // Consultable vía el repositorio de historial de gobernanza del tenant.
        var readRepo = new TenantAuditLogRepository(ctx);
        var page = await readRepo.ListPagedAsync(tenantId, 1, 20, TestContext.Current.CancellationToken);
        page.TotalCount.Should().Be(1);
        page.Items.Single().FieldName.Should().Be("is_active");
    }

    [Fact]
    public async Task IdenticalSecondCall_IsIdempotent_NoDuplicateAudit()
    {
        var db = NewDbName();
        var tenantId = Guid.NewGuid();
        await SeedOtTenant(db, tenantId, isActive: true);

        await using var ctx = NewContext(db);
        var handler = new SetTransitOfficeTenantStatusHandler(new TransitOfficeTenantWriteRepository(ctx));

        var first = await handler.HandleAsync(
            new SetTransitOfficeTenantStatusCommand { TenantId = tenantId, EstadoActivo = false },
            TestContext.Current.CancellationToken);
        var second = await handler.HandleAsync(
            new SetTransitOfficeTenantStatusCommand { TenantId = tenantId, EstadoActivo = false },
            TestContext.Current.CancellationToken);

        first.Changed.Should().BeTrue();
        second.Outcome.Should().Be(SetTransitOfficeTenantStatusOutcome.Updated);
        second.Changed.Should().BeFalse(); // no-op idempotente

        var auditCount = await ctx.TenantConfigAuditLogs.AsNoTracking()
            .CountAsync(a => a.TenantId == tenantId, TestContext.Current.CancellationToken);
        auditCount.Should().Be(1); // no se duplica
    }

    [Fact]
    public async Task Reactivate_WritesAuditWithReversedValues()
    {
        var db = NewDbName();
        var tenantId = Guid.NewGuid();
        await SeedOtTenant(db, tenantId, isActive: false);

        await using var ctx = NewContext(db);
        var handler = new SetTransitOfficeTenantStatusHandler(new TransitOfficeTenantWriteRepository(ctx));

        var result = await handler.HandleAsync(
            new SetTransitOfficeTenantStatusCommand { TenantId = tenantId, EstadoActivo = true },
            TestContext.Current.CancellationToken);

        result.Changed.Should().BeTrue();
        var audit = await ctx.TenantConfigAuditLogs.AsNoTracking()
            .SingleAsync(a => a.TenantId == tenantId, TestContext.Current.CancellationToken);
        audit.OldValue.Should().Be(JsonSerializer.Serialize(false));
        audit.NewValue.Should().Be(JsonSerializer.Serialize(true));
    }

    [Fact]
    public async Task UnknownTenant_ReturnsNotFound_WithoutAudit()
    {
        var db = NewDbName();
        await using var ctx = NewContext(db);
        var handler = new SetTransitOfficeTenantStatusHandler(new TransitOfficeTenantWriteRepository(ctx));

        var result = await handler.HandleAsync(
            new SetTransitOfficeTenantStatusCommand { TenantId = Guid.NewGuid(), EstadoActivo = false },
            TestContext.Current.CancellationToken);

        result.Outcome.Should().Be(SetTransitOfficeTenantStatusOutcome.NotFound);
        (await ctx.TenantConfigAuditLogs.AsNoTracking().AnyAsync(TestContext.Current.CancellationToken))
            .Should().BeFalse();
    }

    // ---------- Helpers ----------

    private static async Task SeedOtTenant(string db, Guid tenantId, bool isActive)
    {
        await using var seed = NewContext(db);
        seed.Tenants.Add(new Tenant
        {
            Id = tenantId,
            Code = $"OT-{tenantId:N}"[..12],
            LegalName = "OT Auditoría S.A.S.",
            TaxId = "900999999-9",
            TenantType = "RENTING",
            IsActive = isActive,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        seed.TransitOfficeProfiles.Add(new TransitOfficeProfile
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            TransitOfficeId = Guid.NewGuid(),
            OperationMode = "dashboard",
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private static string NewDbName() => $"flit-ot-status-audit-{Guid.NewGuid()}";

    private static FlitDbContext NewContext(string dbName) =>
        new(new DbContextOptionsBuilder<FlitDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options);
}
