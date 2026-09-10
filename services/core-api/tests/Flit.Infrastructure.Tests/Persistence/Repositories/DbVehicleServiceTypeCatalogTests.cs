using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Entities.Catalogs;
using Flit.Infrastructure.Persistence.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Flit.Infrastructure.Tests.Persistence.Repositories;

/// <summary>
/// Catálogo global (sin tenant_id) de tipos de servicio del vehículo — sección 18 del FUR
/// (<c>catalogs.vehicle_service_types</c>, ADR-0019). Contrato: 6 códigos cerrados que consume
/// <c>FurFieldMapper.MarkServicio</c>, listados en el orden normativo <c>sort_order</c> 1-6.
/// </summary>
public sealed class DbVehicleServiceTypeCatalogTests
{
    [Fact]
    public async Task ListActiveAsync_DevuelveLosActivosOrdenadosPorSortOrder()
    {
        var dbName = Guid.NewGuid().ToString();

        await using (var seed = NewContext(dbName))
        {
            // Se siembran fuera de orden a propósito: el repositorio, no el orden de inserción,
            // debe garantizar sort_order ascendente.
            seed.VehicleServiceTypes.Add(Row("OFICIAL", "Oficial", 4));
            seed.VehicleServiceTypes.Add(Row("PARTICULAR", "Particular", 1));
            seed.VehicleServiceTypes.Add(Row("OTROS", "Otros", 6));
            seed.VehicleServiceTypes.Add(Row("PUBLICO", "Público", 2));
            seed.VehicleServiceTypes.Add(Row("ESPECIAL", "Especial", 5));
            seed.VehicleServiceTypes.Add(Row("DIPLOMATICO", "Diplomático", 3));
            await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var ctx = NewContext(dbName);
        var catalog = new DbVehicleServiceTypeCatalog(ctx);

        var items = await catalog.ListActiveAsync(TestContext.Current.CancellationToken);

        items.Select(i => i.Code).Should().Equal(
            "PARTICULAR", "PUBLICO", "DIPLOMATICO", "OFICIAL", "ESPECIAL", "OTROS");
        items.Select(i => i.SortOrder).Should().Equal(1, 2, 3, 4, 5, 6);
    }

    [Fact]
    public async Task ListActiveAsync_ExcluyeInactivos()
    {
        var dbName = Guid.NewGuid().ToString();

        await using (var seed = NewContext(dbName))
        {
            seed.VehicleServiceTypes.Add(Row("PARTICULAR", "Particular", 1));
            var inactivo = Row("OTROS", "Otros", 6);
            inactivo.IsActive = false;
            seed.VehicleServiceTypes.Add(inactivo);
            await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var ctx = NewContext(dbName);
        var catalog = new DbVehicleServiceTypeCatalog(ctx);

        var items = await catalog.ListActiveAsync(TestContext.Current.CancellationToken);

        items.Should().ContainSingle(i => i.Code == "PARTICULAR");
        items.Should().NotContain(i => i.Code == "OTROS");
    }

    [Fact]
    public async Task ListActiveAsync_ExcluyeBorradosLogicamente()
    {
        var dbName = Guid.NewGuid().ToString();

        await using (var seed = NewContext(dbName))
        {
            seed.VehicleServiceTypes.Add(Row("PARTICULAR", "Particular", 1));
            var borrado = Row("OTROS", "Otros", 6);
            borrado.DeletedAt = DateTimeOffset.UtcNow;
            seed.VehicleServiceTypes.Add(borrado);
            await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var ctx = NewContext(dbName);
        var catalog = new DbVehicleServiceTypeCatalog(ctx);

        var items = await catalog.ListActiveAsync(TestContext.Current.CancellationToken);

        items.Should().ContainSingle(i => i.Code == "PARTICULAR");
        items.Should().NotContain(i => i.Code == "OTROS");
    }

    [Fact]
    public async Task ListActiveAsync_SinFilas_DevuelveListaVacia()
    {
        var dbName = Guid.NewGuid().ToString();

        await using var ctx = NewContext(dbName);
        var catalog = new DbVehicleServiceTypeCatalog(ctx);

        var items = await catalog.ListActiveAsync(TestContext.Current.CancellationToken);

        items.Should().BeEmpty();
    }

    private static VehicleServiceType Row(string code, string name, int sortOrder) => new()
    {
        Id = Guid.NewGuid(),
        Code = code,
        Name = name,
        SortOrder = sortOrder,
        IsActive = true,
        ExternalRefs = "{}",
        CreatedAt = DateTimeOffset.UtcNow,
    };

    private static FlitDbContext NewContext(string dbName) =>
        new(new DbContextOptionsBuilder<FlitDbContext>().UseInMemoryDatabase(dbName).Options);
}
