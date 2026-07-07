using Flit.Admin.Application.Companies.TransitOffices.ListTransitOfficesOperationalStatus;
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
/// Tests del listado de estado operativo de organismos de tránsito (RF01): catálogo
/// LEFT JOIN perfil OT + tenant. Cubre los tres estados —sin alta, tenant activo,
/// tenant inactivo— más el filtro de oficinas inactivas del catálogo. Proveedor InMemory.
/// </summary>
public sealed class ListTransitOfficesOperationalStatusHandlerTests
{
    [Fact]
    public async Task List_ProjectsThreeStates_SinAlta_Activo_Inactivo()
    {
        var db = NewDbName();
        var officeSinAlta = Guid.NewGuid();
        var officeActivo = Guid.NewGuid();
        var officeInactivo = Guid.NewGuid();
        var tenantActivo = Guid.NewGuid();
        var tenantInactivo = Guid.NewGuid();

        await using (var seed = NewContext(db))
        {
            seed.TransitOffices.AddRange(
                new TransitOffice { Id = officeSinAlta, Code = "99001", Name = "A OT sin alta", DepartmentCode = "99", CityCode = "99001", IsActive = true },
                new TransitOffice { Id = officeActivo, Code = "99002", Name = "B OT activo", DepartmentCode = "99", CityCode = "99002", IsActive = true },
                new TransitOffice { Id = officeInactivo, Code = "99003", Name = "C OT inactivo", DepartmentCode = "99", CityCode = "99003", IsActive = true });

            seed.Tenants.AddRange(
                new Tenant { Id = tenantActivo, Code = "OT-ACT", LegalName = "OT Activo S.A.S.", TaxId = "900111111-1", TenantType = "RENTING", IsActive = true, CreatedAt = DateTimeOffset.UtcNow },
                new Tenant { Id = tenantInactivo, Code = "OT-INA", LegalName = "OT Inactivo S.A.S.", TaxId = "900222222-2", TenantType = "RENTING", IsActive = false, CreatedAt = DateTimeOffset.UtcNow });

            seed.TransitOfficeProfiles.AddRange(
                new TransitOfficeProfile { Id = Guid.NewGuid(), TenantId = tenantActivo, TransitOfficeId = officeActivo, OperationMode = "dashboard", CreatedAt = DateTimeOffset.UtcNow },
                new TransitOfficeProfile { Id = Guid.NewGuid(), TenantId = tenantInactivo, TransitOfficeId = officeInactivo, OperationMode = "quipux", CreatedAt = DateTimeOffset.UtcNow });

            await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var ctx = NewContext(db);
        var handler = new ListTransitOfficesOperationalStatusHandler(
            new DbTransitOfficeOperationalStatusReader(ctx));

        var result = await handler.HandleAsync(
            new ListTransitOfficesOperationalStatusQuery(), TestContext.Current.CancellationToken);

        result.Should().HaveCount(3);

        var sinAlta = result.Single(r => r.Id == officeSinAlta);
        sinAlta.HasTenant.Should().BeFalse();
        sinAlta.TenantId.Should().BeNull();
        sinAlta.EstadoActivo.Should().BeNull();
        sinAlta.OperationMode.Should().BeNull();

        var activo = result.Single(r => r.Id == officeActivo);
        activo.HasTenant.Should().BeTrue();
        activo.TenantId.Should().Be(tenantActivo);
        activo.EstadoActivo.Should().BeTrue();
        activo.OperationMode.Should().Be("dashboard");

        var inactivo = result.Single(r => r.Id == officeInactivo);
        inactivo.HasTenant.Should().BeTrue();
        inactivo.TenantId.Should().Be(tenantInactivo);
        inactivo.EstadoActivo.Should().BeFalse();
        inactivo.OperationMode.Should().Be("quipux");
    }

    [Fact]
    public async Task List_OrdersByName_AndExcludesInactiveCatalogOffices()
    {
        var db = NewDbName();
        var zeta = Guid.NewGuid();
        var alfa = Guid.NewGuid();
        var inactiveOffice = Guid.NewGuid();

        await using (var seed = NewContext(db))
        {
            seed.TransitOffices.AddRange(
                new TransitOffice { Id = zeta, Code = "77001", Name = "Zeta OT", DepartmentCode = "77", CityCode = "77001", IsActive = true },
                new TransitOffice { Id = alfa, Code = "77002", Name = "Alfa OT", DepartmentCode = "77", CityCode = "77002", IsActive = true },
                // Oficina descatalogada — no debe aparecer.
                new TransitOffice { Id = inactiveOffice, Code = "77003", Name = "Oficina Descatalogada", DepartmentCode = "77", CityCode = "77003", IsActive = false });

            await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var ctx = NewContext(db);
        var handler = new ListTransitOfficesOperationalStatusHandler(
            new DbTransitOfficeOperationalStatusReader(ctx));

        var result = await handler.HandleAsync(
            new ListTransitOfficesOperationalStatusQuery(), TestContext.Current.CancellationToken);

        result.Should().HaveCount(2);
        result.Select(r => r.Name).Should().ContainInOrder("Alfa OT", "Zeta OT");
        result.Should().OnlyContain(r => !r.HasTenant);
    }

    // ---------- Helpers ----------

    private static string NewDbName() => $"flit-ot-operational-status-{Guid.NewGuid()}";

    private static FlitDbContext NewContext(string dbName) =>
        new(new DbContextOptionsBuilder<FlitDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options);
}
