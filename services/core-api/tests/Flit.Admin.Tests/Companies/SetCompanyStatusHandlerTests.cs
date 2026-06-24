using Flit.Admin.Application.Companies.SetCompanyStatus;
using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Entities.Identity;
using Flit.Infrastructure.Persistence.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Flit.Admin.Tests.Companies;

/// <summary>
/// Tests del activar/desactivar compañía (#10118). Ejercitan el handler real sobre el
/// repositorio EF real con proveedor InMemory: cambio de estado persistido, activación,
/// idempotencia y 404 cuando la compañía no existe.
/// </summary>
public sealed class SetCompanyStatusHandlerTests
{
    private static readonly Guid Actor = Guid.Parse("11111111-1111-1111-1111-111111111111");

    [Fact]
    public async Task Deactivate_ActiveCompany_SetsInactive_AndPersists()
    {
        var db = NewDbName();
        var tenantId = await SeedTenantAsync(db, isActive: true);

        await using (var ctx = NewContext(db))
        {
            var handler = new SetCompanyStatusHandler(new CompanyWriteRepository(ctx));
            var result = await handler.HandleAsync(new SetCompanyStatusCommand
            {
                TenantId = tenantId,
                EstadoActivo = false,
                ChangedBy = Actor,
            }, TestContext.Current.CancellationToken);

            result.Outcome.Should().Be(SetCompanyStatusOutcome.Updated);
            result.Company.Should().NotBeNull();
            result.Company!.EstadoActivo.Should().BeFalse();
        }

        await using var verify = NewContext(db);
        var tenant = await verify.Tenants.SingleAsync(t => t.Id == tenantId, cancellationToken: TestContext.Current.CancellationToken);
        tenant.IsActive.Should().BeFalse();
        tenant.UpdatedBy.Should().Be(Actor);
    }

    [Fact]
    public async Task Activate_InactiveCompany_SetsActive()
    {
        var db = NewDbName();
        var tenantId = await SeedTenantAsync(db, isActive: false);

        await using var ctx = NewContext(db);
        var handler = new SetCompanyStatusHandler(new CompanyWriteRepository(ctx));

        var result = await handler.HandleAsync(new SetCompanyStatusCommand
        {
            TenantId = tenantId,
            EstadoActivo = true,
            ChangedBy = Actor,
        }, TestContext.Current.CancellationToken);

        result.Outcome.Should().Be(SetCompanyStatusOutcome.Updated);
        result.Company!.EstadoActivo.Should().BeTrue();
    }

    [Fact]
    public async Task SameState_IsIdempotent_ReturnsUpdated()
    {
        var db = NewDbName();
        var tenantId = await SeedTenantAsync(db, isActive: true);

        await using var ctx = NewContext(db);
        var handler = new SetCompanyStatusHandler(new CompanyWriteRepository(ctx));

        var result = await handler.HandleAsync(new SetCompanyStatusCommand
        {
            TenantId = tenantId,
            EstadoActivo = true,
            ChangedBy = Actor,
        }, TestContext.Current.CancellationToken);

        result.Outcome.Should().Be(SetCompanyStatusOutcome.Updated);
        result.Company!.EstadoActivo.Should().BeTrue();
    }

    [Fact]
    public async Task UnknownCompany_ReturnsNotFound()
    {
        await using var ctx = NewContext(NewDbName());
        var handler = new SetCompanyStatusHandler(new CompanyWriteRepository(ctx));

        var result = await handler.HandleAsync(new SetCompanyStatusCommand
        {
            TenantId = Guid.NewGuid(),
            EstadoActivo = false,
            ChangedBy = Actor,
        }, TestContext.Current.CancellationToken);

        result.Outcome.Should().Be(SetCompanyStatusOutcome.NotFound);
        result.Company.Should().BeNull();
    }

    private static async Task<Guid> SeedTenantAsync(string db, bool isActive)
    {
        var id = Guid.NewGuid();
        await using var ctx = NewContext(db);
        ctx.Tenants.Add(new Tenant
        {
            Id = id,
            Code = "ACME" + id.ToString("N")[..6],
            LegalName = "ACME S.A.S.",
            TaxId = "900111222-3",
            TenantType = "RENTING",
            IsActive = isActive,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await ctx.SaveChangesAsync();
        return id;
    }

    private static string NewDbName() => $"flit-company-status-{Guid.NewGuid()}";

    private static FlitDbContext NewContext(string dbName) =>
        new(new DbContextOptionsBuilder<FlitDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options);
}
