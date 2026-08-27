using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Entities.Catalogs;
using Flit.Infrastructure.Persistence.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Flit.Infrastructure.Tests.Persistence.Repositories;

public sealed class DbVehicleBodyworkCatalogTests
{
    [Fact]
    public async Task ConClase_SoloDevuelveCarroceriasDeEsaClase()
    {
        var dbName = Guid.NewGuid().ToString();
        await using (var seed = NewContext(dbName))
        {
            seed.VehicleBodyworks.Add(Row("9", "SEDAN", "AUTOMOVIL"));
            seed.VehicleBodyworks.Add(Row("1", "ESTACAS", "CAMION"));
            seed.VehicleBodyworks.Add(Row("819", "SIN CARROCERIA", null));
            await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var ctx = NewContext(dbName);
        var catalog = new DbVehicleBodyworkCatalog(ctx);

        var items = await catalog.SearchAsync("AUTOMOVIL", null, 200, TestContext.Current.CancellationToken);

        items.Should().ContainSingle(i => i.Name == "SEDAN");
        items.Should().NotContain(i => i.Name == "ESTACAS");
        items.Should().NotContain(i => i.Name == "SIN CARROCERIA");
    }

    [Fact]
    public async Task SinClase_DevuelveSoloRespaldoSinClassVehicle()
    {
        var dbName = Guid.NewGuid().ToString();
        await SeedThree(dbName);

        await using var ctx = NewContext(dbName);
        var catalog = new DbVehicleBodyworkCatalog(ctx);
        var items = await catalog.SearchAsync(null, null, 200, TestContext.Current.CancellationToken);

        items.Should().ContainSingle(i => i.Name == "SIN CARROCERIA");
        items.Should().NotContain(i => i.Name == "SEDAN");
    }

    [Fact]
    public async Task ClaseDesconocida_ListaVacia()
    {
        var dbName = Guid.NewGuid().ToString();
        await SeedThree(dbName);

        await using var ctx = NewContext(dbName);
        var catalog = new DbVehicleBodyworkCatalog(ctx);

        var items = await catalog.SearchAsync("ANFIBIO", null, 200, TestContext.Current.CancellationToken);

        items.Should().BeEmpty();
    }

    [Fact]
    public async Task PrefijoCamionCisterna_UsaClaseCamion()
    {
        var dbName = Guid.NewGuid().ToString();
        await SeedThree(dbName);

        await using var ctx = NewContext(dbName);
        var catalog = new DbVehicleBodyworkCatalog(ctx);

        var items = await catalog.SearchAsync("CAMION CISTERNA", null, 200, TestContext.Current.CancellationToken);

        items.Should().ContainSingle(i => i.Name == "ESTACAS");
        items.Should().NotContain(i => i.Name == "SEDAN");
    }

    [Fact]
    public async Task Search_FiltraPorNombre()
    {
        var dbName = Guid.NewGuid().ToString();
        await using (var seed = NewContext(dbName))
        {
            seed.VehicleBodyworks.Add(Row("9", "SEDAN", "AUTOMOVIL"));
            seed.VehicleBodyworks.Add(Row("19", "COUPE", "AUTOMOVIL"));
            await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var ctx = NewContext(dbName);
        var catalog = new DbVehicleBodyworkCatalog(ctx);

        var items = await catalog.SearchAsync("AUTOMOVIL", "cou", 200, TestContext.Current.CancellationToken);

        items.Should().ContainSingle(i => i.Name == "COUPE");
    }

    private static async Task SeedThree(string dbName)
    {
        await using var seed = NewContext(dbName);
        seed.VehicleBodyworks.Add(Row("9", "SEDAN", "AUTOMOVIL"));
        seed.VehicleBodyworks.Add(Row("1", "ESTACAS", "CAMION"));
        seed.VehicleBodyworks.Add(Row("819", "SIN CARROCERIA", null));
        await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private static VehicleBodywork Row(string code, string name, string? classVehicle) => new()
    {
        Id = Guid.NewGuid(),
        Code = code,
        Name = name,
        ClassVehicle = classVehicle,
        IsActive = true,
        ExternalRefs = "{}",
        CreatedAt = DateTimeOffset.UtcNow,
    };

    private static FlitDbContext NewContext(string dbName) =>
        new(new DbContextOptionsBuilder<FlitDbContext>().UseInMemoryDatabase(dbName).Options);
}
