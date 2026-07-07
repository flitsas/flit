using Flit.Admin.Application.Companies.TransitOffices.ListTransitOfficeTenants;
using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Entities.Admin;
using Flit.Infrastructure.Persistence.Entities.Catalogs;
using Flit.Infrastructure.Persistence.Entities.Identity;
using Flit.Infrastructure.Persistence.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Flit.Admin.Tests.Companies.TransitOffices;

/// <summary>
/// Tests del listado paginado de tenants OT (join <c>identity.tenants</c> +
/// <c>admin.transit_office_profiles</c> + catálogo). Proveedor InMemory.
/// </summary>
public sealed class ListTransitOfficeTenantsHandlerTests
{
    [Fact]
    public async Task ListAsync_ReturnsOnlyTenantsWithProfile_WithOfficeNameAndCode()
    {
        var db = NewDbName();
        var officeId = Guid.NewGuid();
        var otTenantId = Guid.NewGuid();
        var companyTenantId = Guid.NewGuid();

        await using (var seed = NewContext(db))
        {
            seed.TransitOffices.Add(new TransitOffice
            {
                Id = officeId,
                Code = "99001",
                Name = "OT de prueba",
                DepartmentCode = "99",
                CityCode = "99001",
                IsActive = true,
            });

            seed.Tenants.Add(new Tenant
            {
                Id = otTenantId,
                Code = "OT-LISTADO",
                LegalName = "OT Listado S.A.S.",
                TaxId = "900777777-7",
                TenantType = "RENTING",
                IsActive = true,
                CreatedAt = DateTimeOffset.UtcNow,
            });
            seed.TransitOfficeProfiles.Add(new TransitOfficeProfile
            {
                Id = Guid.NewGuid(),
                TenantId = otTenantId,
                TransitOfficeId = officeId,
                OperationMode = "dashboard",
                CreatedAt = DateTimeOffset.UtcNow,
            });

            // Compañía B2B normal (sin perfil OT) — NO debe aparecer en el listado OT.
            seed.Tenants.Add(new Tenant
            {
                Id = companyTenantId,
                Code = "COMPANIA-NORMAL",
                LegalName = "Compañía Normal S.A.S.",
                TaxId = "900888888-8",
                TenantType = "RENTING",
                IsActive = true,
                CreatedAt = DateTimeOffset.UtcNow,
            });

            await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var ctx = NewContext(db);
        var handler = new ListTransitOfficeTenantsHandler(new TransitOfficeTenantWriteRepository(ctx));

        var result = await handler.HandleAsync(
            new ListTransitOfficeTenantsQuery(), TestContext.Current.CancellationToken);

        result.TotalCount.Should().Be(1);
        result.Data.Should().ContainSingle();
        var item = result.Data.Single();
        item.Id.Should().Be(otTenantId);
        item.TransitOfficeId.Should().Be(officeId);
        item.TransitOfficeName.Should().Be("OT de prueba");
        item.TransitOfficeCode.Should().Be("99001");
    }

    // ---------- Helpers ----------

    private static string NewDbName() => $"flit-list-transit-office-tenants-{Guid.NewGuid()}";

    private static FlitDbContext NewContext(string dbName) =>
        new(new DbContextOptionsBuilder<FlitDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options);
}
