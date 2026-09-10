using Flit.Admin.Application.Auditing;
using Flit.Admin.Application.Companies.Settings.GetActiveModules;
using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Entities.Admin;
using Flit.Infrastructure.Persistence.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Flit.Admin.Tests.Companies.Settings;

/// <summary>
/// Tests del handler de lectura de módulos activos del dashboard (HU #12251, Feature #12249).
/// AC1 (los 3 flags exactos) y AC3 (defaults cuando no hay fila, nunca null).
/// </summary>
public sealed class GetActiveModulesHandlerTests
{
    [Fact]
    public async Task AC1_ReturnsThePersistedThreeFlags()
    {
        var db = NewDbName();
        var tenantId = Guid.NewGuid();

        await using (var seed = NewContext(db))
        {
            seed.TenantOperationalPolicies.Add(new TenantOperationalPolicy
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                AllowInitialRegistration = true,
                AllowMiscNewVehicles = true,
                OnlyOwnVehicles = false,
                SignatureVaultEnabled = false,
                NotificationChannel = "flit_smtp",
                NotificationTarget = "RADICADOR",
                PaymentMethods = "[]",
                TramitesModuleEnabled = false,
                ComparendosModuleEnabled = true,
                ResolucionesModuleEnabled = true,
                CreatedAt = DateTimeOffset.UtcNow,
            });
            await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var ctx = NewContext(db);
        var handler = new GetActiveModulesHandler(new TenantSettingsRepository(ctx, NullAuditContextAccessor.Instance));

        var result = await handler.HandleAsync(
            new GetActiveModulesQuery { TenantId = tenantId }, TestContext.Current.CancellationToken);

        result.TramitesModuleEnabled.Should().BeFalse();
        result.ComparendosModuleEnabled.Should().BeTrue();
        result.ResolucionesModuleEnabled.Should().BeTrue();
    }

    [Fact]
    public async Task AC3_ReturnsDefaults_WhenNoConfigurationExists()
    {
        // Sin fila en tenant_operational_policies → defaults de TenantSettings.Default,
        // NUNCA null (a diferencia de GetTenantSettingsHandler, que traduce a 404).
        await using var ctx = NewContext(NewDbName());
        var handler = new GetActiveModulesHandler(new TenantSettingsRepository(ctx, NullAuditContextAccessor.Instance));

        var result = await handler.HandleAsync(
            new GetActiveModulesQuery { TenantId = Guid.NewGuid() }, TestContext.Current.CancellationToken);

        result.Should().NotBeNull();
        result.TramitesModuleEnabled.Should().BeTrue();
        result.ComparendosModuleEnabled.Should().BeFalse();
        result.ResolucionesModuleEnabled.Should().BeFalse();
    }

    private static string NewDbName() => $"flit-active-modules-{Guid.NewGuid()}";

    private static FlitDbContext NewContext(string dbName) =>
        new(new DbContextOptionsBuilder<FlitDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options);
}
