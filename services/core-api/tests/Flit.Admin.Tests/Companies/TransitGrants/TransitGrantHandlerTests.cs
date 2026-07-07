using Flit.Admin.Application.Companies.TransitOffices.AddTransitGrant;
using Flit.Admin.Application.Companies.TransitOffices.GetTransitGrants;
using Flit.Admin.Application.Companies.TransitOffices.RemoveTransitGrant;
using Flit.Admin.Domain.Companies.TransitOffices;
using Flit.Admin.Tests.TestDoubles;
using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Entities.Admin;
using Flit.Infrastructure.Persistence.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Flit.Admin.Tests.Companies.TransitGrants;

/// <summary>
/// API de grants de organismos de tránsito (HU #10192) — AC2 (alta + audit, 422 si el
/// OT no existe, idempotencia), AC3 (baja + audit, 404 si no existe) y AC5 (listado de
/// habilitados). Ejercita los handlers reales sobre el repositorio EF real con
/// proveedor InMemory (la transacción/SET LOCAL aplican solo en proveedor relacional).
///
/// Uso de ejemplo:
/// <code>
/// var handler = new AddTransitGrantHandler(new StaticTransitOfficeCatalog(), new TransitGrantRepository(ctx));
/// var result = await handler.HandleAsync(command);
/// </code>
/// </summary>
public sealed class TransitGrantHandlerTests
{
    private static readonly Guid ChangedBy = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly StaticTransitOfficeCatalog Catalog = new StaticTransitOfficeCatalog();
    private static Guid KnownOfficeId => Catalog.All[0].Id;

    /// <summary>Reader que reporta cualquier OT como operativo (tenant activo) — HU #10518.</summary>
    private static StubTransitOfficeOperationalStatusReader OperableReader =>
        new() { DefaultOperable = true };

    // ---------- AC2: alta + auditoría ----------

    [Fact]
    public async Task AC2_AddsGrant_AndWritesAudit()
    {
        var db = NewDbName();
        var tenantId = Guid.NewGuid();

        await using (var act = NewContext(db))
        {
            var handler = new AddTransitGrantHandler(Catalog, OperableReader, new TransitGrantRepository(act));
            var result = await handler.HandleAsync(new AddTransitGrantCommand
            {
                TenantId = tenantId,
                TransitOfficeId = KnownOfficeId,
                CreatedBy = ChangedBy,
            }, TestContext.Current.CancellationToken);

            result.IsValid.Should().BeTrue();
            result.Added.Should().BeTrue();
        }

        await using var verify = NewContext(db);

        var grants = await verify.TenantTransitOfficeGrants.Where(g => g.TenantId == tenantId).ToListAsync(cancellationToken: TestContext.Current.CancellationToken);
        grants.Should().ContainSingle();
        grants[0].TransitOfficeId.Should().Be(KnownOfficeId);
        grants[0].IsEnabled.Should().BeTrue();
        grants[0].CreatedBy.Should().Be(ChangedBy);

        var audits = await verify.TenantConfigAuditLogs.Where(a => a.TenantId == tenantId).ToListAsync(cancellationToken: TestContext.Current.CancellationToken);
        audits.Should().ContainSingle();
        audits[0].EntityName.Should().Be("tenant_transit_office_grants");
        audits[0].FieldName.Should().Be("transit_office_id");
        audits[0].OldValue.Should().BeNull();
        audits[0].NewValue.Should().Be($"\"{KnownOfficeId}\"");
        audits[0].ChangedBy.Should().Be(ChangedBy);
    }

    [Fact]
    public async Task AC2_InvalidOfficeId_Returns422_AndPersistsNothing()
    {
        var db = NewDbName();
        var tenantId = Guid.NewGuid();

        await using (var act = NewContext(db))
        {
            var handler = new AddTransitGrantHandler(Catalog, OperableReader, new TransitGrantRepository(act));
            var result = await handler.HandleAsync(new AddTransitGrantCommand
            {
                TenantId = tenantId,
                TransitOfficeId = Guid.NewGuid(), // no existe en el catálogo
                CreatedBy = ChangedBy,
            }, TestContext.Current.CancellationToken);

            result.IsValid.Should().BeFalse();
            result.Errors.Should().ContainSingle(e => e.Field == "transitOfficeId");
        }

        await using var verify = NewContext(db);
        (await verify.TenantTransitOfficeGrants.CountAsync(g => g.TenantId == tenantId, cancellationToken: TestContext.Current.CancellationToken)).Should().Be(0);
        (await verify.TenantConfigAuditLogs.CountAsync(a => a.TenantId == tenantId, cancellationToken: TestContext.Current.CancellationToken)).Should().Be(0);
    }

    [Fact]
    public async Task AC2_IsIdempotent_DoesNotDuplicateGrantNorAudit()
    {
        var db = NewDbName();
        var tenantId = Guid.NewGuid();

        await using (var first = NewContext(db))
        {
            var handler = new AddTransitGrantHandler(Catalog, OperableReader, new TransitGrantRepository(first));
            (await handler.HandleAsync(new AddTransitGrantCommand
            {
                TenantId = tenantId,
                TransitOfficeId = KnownOfficeId,
                CreatedBy = ChangedBy,
            }, TestContext.Current.CancellationToken)).Added.Should().BeTrue();
        }

        await using (var second = NewContext(db))
        {
            var handler = new AddTransitGrantHandler(Catalog, OperableReader, new TransitGrantRepository(second));
            var result = await handler.HandleAsync(new AddTransitGrantCommand
            {
                TenantId = tenantId,
                TransitOfficeId = KnownOfficeId,
                CreatedBy = ChangedBy,
            }, TestContext.Current.CancellationToken);

            result.IsValid.Should().BeTrue();
            result.Added.Should().BeFalse(); // ya existía → idempotente
        }

        await using var verify = NewContext(db);
        (await verify.TenantTransitOfficeGrants.CountAsync(g => g.TenantId == tenantId, cancellationToken: TestContext.Current.CancellationToken)).Should().Be(1);
        (await verify.TenantConfigAuditLogs.CountAsync(a => a.TenantId == tenantId, cancellationToken: TestContext.Current.CancellationToken)).Should().Be(1);
    }

    // ---------- HU #10518: bloqueo de grant si el OT no es operativo ----------

    [Fact]
    public async Task HU10518_OtSinAlta_ReturnsInvalid_AndPersistsNothing()
    {
        var db = NewDbName();
        var tenantId = Guid.NewGuid();
        // OT en catálogo pero sin tenant OT dado de alta.
        var reader = new StubTransitOfficeOperationalStatusReader().Set(KnownOfficeId, hasTenant: false, estadoActivo: null);

        await using (var act = NewContext(db))
        {
            var handler = new AddTransitGrantHandler(Catalog, reader, new TransitGrantRepository(act));
            var result = await handler.HandleAsync(new AddTransitGrantCommand
            {
                TenantId = tenantId,
                TransitOfficeId = KnownOfficeId,
                CreatedBy = ChangedBy,
            }, TestContext.Current.CancellationToken);

            result.IsValid.Should().BeFalse();
            result.Errors.Should().ContainSingle(e =>
                e.Field == "transitOfficeId" && e.Message == AddTransitGrantHandler.SinAltaMessage);
        }

        await using var verify = NewContext(db);
        (await verify.TenantTransitOfficeGrants.CountAsync(g => g.TenantId == tenantId, cancellationToken: TestContext.Current.CancellationToken)).Should().Be(0);
        (await verify.TenantConfigAuditLogs.CountAsync(a => a.TenantId == tenantId, cancellationToken: TestContext.Current.CancellationToken)).Should().Be(0);
    }

    [Fact]
    public async Task HU10518_OtInactivo_ReturnsInvalid_AndPersistsNothing()
    {
        var db = NewDbName();
        var tenantId = Guid.NewGuid();
        // OT con tenant OT pero inactivo (is_active=false).
        var reader = new StubTransitOfficeOperationalStatusReader().Set(KnownOfficeId, hasTenant: true, estadoActivo: false);

        await using (var act = NewContext(db))
        {
            var handler = new AddTransitGrantHandler(Catalog, reader, new TransitGrantRepository(act));
            var result = await handler.HandleAsync(new AddTransitGrantCommand
            {
                TenantId = tenantId,
                TransitOfficeId = KnownOfficeId,
                CreatedBy = ChangedBy,
            }, TestContext.Current.CancellationToken);

            result.IsValid.Should().BeFalse();
            result.Errors.Should().ContainSingle(e =>
                e.Field == "transitOfficeId" && e.Message == AddTransitGrantHandler.InactivoMessage);
        }

        await using var verify = NewContext(db);
        (await verify.TenantTransitOfficeGrants.CountAsync(g => g.TenantId == tenantId, cancellationToken: TestContext.Current.CancellationToken)).Should().Be(0);
        (await verify.TenantConfigAuditLogs.CountAsync(a => a.TenantId == tenantId, cancellationToken: TestContext.Current.CancellationToken)).Should().Be(0);
    }

    [Fact]
    public async Task HU10518_OtActivo_AddsGrant_LikeBefore()
    {
        var db = NewDbName();
        var tenantId = Guid.NewGuid();
        var reader = new StubTransitOfficeOperationalStatusReader().Set(KnownOfficeId, hasTenant: true, estadoActivo: true);

        await using (var act = NewContext(db))
        {
            var handler = new AddTransitGrantHandler(Catalog, reader, new TransitGrantRepository(act));
            var result = await handler.HandleAsync(new AddTransitGrantCommand
            {
                TenantId = tenantId,
                TransitOfficeId = KnownOfficeId,
                CreatedBy = ChangedBy,
            }, TestContext.Current.CancellationToken);

            result.IsValid.Should().BeTrue();
            result.Added.Should().BeTrue();
        }

        await using var verify = NewContext(db);
        (await verify.TenantTransitOfficeGrants.CountAsync(g => g.TenantId == tenantId, cancellationToken: TestContext.Current.CancellationToken)).Should().Be(1);
    }

    // ---------- AC3: baja + auditoría / 404 ----------

    [Fact]
    public async Task AC3_RemovesGrant_AndWritesAudit()
    {
        var db = NewDbName();
        var tenantId = Guid.NewGuid();

        await using (var seed = NewContext(db))
        {
            seed.TenantTransitOfficeGrants.Add(new TenantTransitOfficeGrant
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                TransitOfficeId = KnownOfficeId,
                IsEnabled = true,
                CreatedAt = DateTimeOffset.UtcNow,
            });
            seed.SaveChanges();
        }

        await using (var act = NewContext(db))
        {
            var handler = new RemoveTransitGrantHandler(new TransitGrantRepository(act));
            var removed = await handler.HandleAsync(new RemoveTransitGrantCommand
            {
                TenantId = tenantId,
                TransitOfficeId = KnownOfficeId,
                ChangedBy = ChangedBy,
            }, TestContext.Current.CancellationToken);

            removed.Should().BeTrue();
        }

        await using var verify = NewContext(db);
        (await verify.TenantTransitOfficeGrants.CountAsync(g => g.TenantId == tenantId, cancellationToken: TestContext.Current.CancellationToken)).Should().Be(0);

        var audits = await verify.TenantConfigAuditLogs.Where(a => a.TenantId == tenantId).ToListAsync(cancellationToken: TestContext.Current.CancellationToken);
        audits.Should().ContainSingle();
        audits[0].OldValue.Should().Be($"\"{KnownOfficeId}\"");
        audits[0].NewValue.Should().BeNull();
        audits[0].ChangedBy.Should().Be(ChangedBy);
    }

    [Fact]
    public async Task AC3_ReturnsFalse_WhenGrantDoesNotExist()
    {
        var db = NewDbName();
        var tenantId = Guid.NewGuid();

        await using var act = NewContext(db);
        var handler = new RemoveTransitGrantHandler(new TransitGrantRepository(act));

        var removed = await handler.HandleAsync(new RemoveTransitGrantCommand
        {
            TenantId = tenantId,
            TransitOfficeId = KnownOfficeId,
            ChangedBy = ChangedBy,
        }, TestContext.Current.CancellationToken);

        removed.Should().BeFalse(); // → 404, sin auditoría
        (await act.TenantConfigAuditLogs.CountAsync(a => a.TenantId == tenantId, cancellationToken: TestContext.Current.CancellationToken)).Should().Be(0);
    }

    // ---------- AC5: listado de habilitados ----------

    [Fact]
    public async Task AC5_Get_ReturnsEnabledOfficeIds()
    {
        var db = NewDbName();
        var tenantId = Guid.NewGuid();
        var enabledId = Catalog.All[0].Id;
        var disabledId = Catalog.All[1].Id;

        await using (var seed = NewContext(db))
        {
            seed.TenantTransitOfficeGrants.Add(new TenantTransitOfficeGrant
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                TransitOfficeId = enabledId,
                IsEnabled = true,
                CreatedAt = DateTimeOffset.UtcNow,
            });
            seed.TenantTransitOfficeGrants.Add(new TenantTransitOfficeGrant
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                TransitOfficeId = disabledId,
                IsEnabled = false,
                CreatedAt = DateTimeOffset.UtcNow.AddMinutes(1),
            });
            seed.SaveChanges();
        }

        await using var ctx = NewContext(db);
        var handler = new GetTransitGrantsHandler(new TransitGrantRepository(ctx));

        var result = await handler.HandleAsync(new GetTransitGrantsQuery { TenantId = tenantId }, TestContext.Current.CancellationToken);

        result.TransitOfficeIds.Should().ContainSingle().Which.Should().Be(enabledId);
    }

    [Fact]
    public async Task AC5_Get_ReturnsEmpty_WhenNoGrants()
    {
        await using var ctx = NewContext(NewDbName());
        var handler = new GetTransitGrantsHandler(new TransitGrantRepository(ctx));

        var result = await handler.HandleAsync(new GetTransitGrantsQuery { TenantId = Guid.NewGuid() }, TestContext.Current.CancellationToken);

        result.TransitOfficeIds.Should().BeEmpty();
    }

    // ---------- Helpers ----------

    private static string NewDbName() => $"flit-transit-grants-{Guid.NewGuid()}";

    private static FlitDbContext NewContext(string dbName) =>
        new(new DbContextOptionsBuilder<FlitDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options);
}
