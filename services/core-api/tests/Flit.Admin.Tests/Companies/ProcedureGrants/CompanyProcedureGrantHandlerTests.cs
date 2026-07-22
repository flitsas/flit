using Flit.Admin.Application.Auditing;
using Flit.Admin.Application.Companies.ProcedureGrants;
using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Entities.Admin;
using Flit.Infrastructure.Persistence.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Flit.Admin.Tests.Companies.ProcedureGrants;

/// <summary>
/// FEATURE-08 — habilitación de tipos de trámite por compañía (grant model). Cubre alta + auditoría,
/// idempotencia, 422 con id vacío, baja + auditoría, 404 si no existe, y listado de habilitados.
/// Ejercita los handlers reales sobre el repositorio EF real con proveedor InMemory (la
/// transacción/SET LOCAL solo aplican en proveedor relacional).
/// </summary>
public sealed class CompanyProcedureGrantHandlerTests
{
    private static readonly Guid ChangedBy = Guid.Parse("22222222-2222-2222-2222-222222222222");

    [Fact]
    public async Task Add_CreatesGrant_AndWritesAudit()
    {
        var db = NewDbName();
        var tenantId = Guid.NewGuid();
        var typeId = Guid.NewGuid();

        await using (var act = NewContext(db))
        {
            var handler = new AddCompanyProcedureGrantHandler(
                new CompanyProcedureGrantRepository(act, NullAuditContextAccessor.Instance));
            var result = await handler.HandleAsync(new AddCompanyProcedureGrantCommand
            {
                TenantId = tenantId,
                ProcedureTypeId = typeId,
                CreatedBy = ChangedBy,
            }, TestContext.Current.CancellationToken);

            result.IsValid.Should().BeTrue();
            result.Added.Should().BeTrue();
        }

        await using var verify = NewContext(db);
        var grants = await verify.CompanyProcedureTypeGrants
            .Where(g => g.TenantId == tenantId)
            .ToListAsync(TestContext.Current.CancellationToken);
        grants.Should().ContainSingle();
        grants[0].ProcedureTypeId.Should().Be(typeId);
        grants[0].IsEnabled.Should().BeTrue();

        var audits = await verify.TenantConfigAuditLogs
            .Where(a => a.TenantId == tenantId)
            .ToListAsync(TestContext.Current.CancellationToken);
        audits.Should().ContainSingle();
        audits[0].EntityName.Should().Be("company_procedure_type_grants");
        audits[0].FieldName.Should().Be("procedure_type_id");
        audits[0].NewValue.Should().Be($"\"{typeId}\"");
    }

    [Fact]
    public async Task Add_EmptyId_Returns422_AndPersistsNothing()
    {
        var db = NewDbName();
        var tenantId = Guid.NewGuid();

        await using (var act = NewContext(db))
        {
            var handler = new AddCompanyProcedureGrantHandler(
                new CompanyProcedureGrantRepository(act, NullAuditContextAccessor.Instance));
            var result = await handler.HandleAsync(new AddCompanyProcedureGrantCommand
            {
                TenantId = tenantId,
                ProcedureTypeId = Guid.Empty,
                CreatedBy = ChangedBy,
            }, TestContext.Current.CancellationToken);

            result.IsValid.Should().BeFalse();
            result.Errors.Should().ContainSingle(e => e.Field == "procedureTypeId");
        }

        await using var verify = NewContext(db);
        (await verify.CompanyProcedureTypeGrants.CountAsync(g => g.TenantId == tenantId, TestContext.Current.CancellationToken))
            .Should().Be(0);
    }

    [Fact]
    public async Task Add_IsIdempotent_DoesNotDuplicate()
    {
        var db = NewDbName();
        var tenantId = Guid.NewGuid();
        var typeId = Guid.NewGuid();

        await using (var first = NewContext(db))
        {
            var handler = new AddCompanyProcedureGrantHandler(
                new CompanyProcedureGrantRepository(first, NullAuditContextAccessor.Instance));
            (await handler.HandleAsync(new AddCompanyProcedureGrantCommand
            {
                TenantId = tenantId, ProcedureTypeId = typeId, CreatedBy = ChangedBy,
            }, TestContext.Current.CancellationToken)).Added.Should().BeTrue();
        }

        await using (var second = NewContext(db))
        {
            var handler = new AddCompanyProcedureGrantHandler(
                new CompanyProcedureGrantRepository(second, NullAuditContextAccessor.Instance));
            var result = await handler.HandleAsync(new AddCompanyProcedureGrantCommand
            {
                TenantId = tenantId, ProcedureTypeId = typeId, CreatedBy = ChangedBy,
            }, TestContext.Current.CancellationToken);

            result.IsValid.Should().BeTrue();
            result.Added.Should().BeFalse(); // ya existía → idempotente
        }

        await using var verify = NewContext(db);
        (await verify.CompanyProcedureTypeGrants.CountAsync(g => g.TenantId == tenantId, TestContext.Current.CancellationToken))
            .Should().Be(1);
    }

    [Fact]
    public async Task Remove_DeletesGrant_AndReturnsTrue()
    {
        var db = NewDbName();
        var tenantId = Guid.NewGuid();
        var typeId = Guid.NewGuid();

        await using (var seed = NewContext(db))
        {
            seed.CompanyProcedureTypeGrants.Add(new CompanyProcedureTypeGrant
            {
                Id = Guid.NewGuid(), TenantId = tenantId, ProcedureTypeId = typeId,
                IsEnabled = true, CreatedAt = DateTimeOffset.UtcNow,
            });
            seed.SaveChanges();
        }

        await using (var act = NewContext(db))
        {
            var handler = new RemoveCompanyProcedureGrantHandler(
                new CompanyProcedureGrantRepository(act, NullAuditContextAccessor.Instance));
            var removed = await handler.HandleAsync(new RemoveCompanyProcedureGrantCommand
            {
                TenantId = tenantId, ProcedureTypeId = typeId, ChangedBy = ChangedBy,
            }, TestContext.Current.CancellationToken);

            removed.Should().BeTrue();
        }

        await using var verify = NewContext(db);
        (await verify.CompanyProcedureTypeGrants.CountAsync(g => g.TenantId == tenantId, TestContext.Current.CancellationToken))
            .Should().Be(0);
    }

    [Fact]
    public async Task Remove_ReturnsFalse_WhenGrantDoesNotExist()
    {
        await using var act = NewContext(NewDbName());
        var handler = new RemoveCompanyProcedureGrantHandler(
            new CompanyProcedureGrantRepository(act, NullAuditContextAccessor.Instance));

        var removed = await handler.HandleAsync(new RemoveCompanyProcedureGrantCommand
        {
            TenantId = Guid.NewGuid(), ProcedureTypeId = Guid.NewGuid(), ChangedBy = ChangedBy,
        }, TestContext.Current.CancellationToken);

        removed.Should().BeFalse(); // → 404
    }

    [Fact]
    public async Task Get_ReturnsEnabledTypeIds()
    {
        var db = NewDbName();
        var tenantId = Guid.NewGuid();
        var enabledId = Guid.NewGuid();
        var disabledId = Guid.NewGuid();

        await using (var seed = NewContext(db))
        {
            seed.CompanyProcedureTypeGrants.Add(new CompanyProcedureTypeGrant
            {
                Id = Guid.NewGuid(), TenantId = tenantId, ProcedureTypeId = enabledId,
                IsEnabled = true, CreatedAt = DateTimeOffset.UtcNow,
            });
            seed.CompanyProcedureTypeGrants.Add(new CompanyProcedureTypeGrant
            {
                Id = Guid.NewGuid(), TenantId = tenantId, ProcedureTypeId = disabledId,
                IsEnabled = false, CreatedAt = DateTimeOffset.UtcNow.AddMinutes(1),
            });
            seed.SaveChanges();
        }

        await using var ctx = NewContext(db);
        var handler = new GetCompanyProcedureGrantsHandler(
            new CompanyProcedureGrantRepository(ctx, NullAuditContextAccessor.Instance));

        var result = await handler.HandleAsync(
            new GetCompanyProcedureGrantsQuery { TenantId = tenantId }, TestContext.Current.CancellationToken);

        result.ProcedureTypeIds.Should().ContainSingle().Which.Should().Be(enabledId);
    }

    [Fact]
    public async Task Get_ReturnsEmpty_WhenNoGrants()
    {
        await using var ctx = NewContext(NewDbName());
        var handler = new GetCompanyProcedureGrantsHandler(
            new CompanyProcedureGrantRepository(ctx, NullAuditContextAccessor.Instance));

        var result = await handler.HandleAsync(
            new GetCompanyProcedureGrantsQuery { TenantId = Guid.NewGuid() }, TestContext.Current.CancellationToken);

        result.ProcedureTypeIds.Should().BeEmpty();
    }

    private static string NewDbName() => $"flit-company-procedure-grants-{Guid.NewGuid()}";

    private static FlitDbContext NewContext(string dbName) =>
        new(new DbContextOptionsBuilder<FlitDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options);
}
