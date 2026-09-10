using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Entities.Admin;
using Flit.Infrastructure.Persistence.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Flit.Infrastructure.Tests.Persistence.Repositories;

/// <summary>
/// HU #12240 (Feature #12236), AC1/AC2/AC3 — filtro de vigencia y borrado logico de
/// <see cref="BannerRepository"/> contra un <c>FlitDbContext</c> InMemory (mismo patron que
/// <c>NotificationDeliveryLogRepositoryTests</c>).
/// </summary>
public sealed class BannerRepositoryTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 9, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ListActiveAsync_BannerSinFechas_ActivoAparece()
    {
        var dbName = Guid.NewGuid().ToString();
        var id = Guid.NewGuid();

        await using (var seed = NewContext(dbName))
        {
            seed.Banners.Add(Row(id, "Sin vigencia programada", isActive: true, validFrom: null, validUntil: null));
            await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var ctx = NewContext(dbName);
        var repo = new BannerRepository(ctx);

        var activos = await repo.ListActiveAsync(Now, TestContext.Current.CancellationToken);

        activos.Should().ContainSingle(b => b.Id == id);
    }

    [Fact]
    public async Task ListActiveAsync_BannerInactivo_NoAparece()
    {
        var dbName = Guid.NewGuid().ToString();
        var id = Guid.NewGuid();

        await using (var seed = NewContext(dbName))
        {
            seed.Banners.Add(Row(id, "Desactivado", isActive: false, validFrom: null, validUntil: null));
            await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var ctx = NewContext(dbName);
        var repo = new BannerRepository(ctx);

        var activos = await repo.ListActiveAsync(Now, TestContext.Current.CancellationToken);

        activos.Should().BeEmpty();
    }

    [Fact]
    public async Task ListActiveAsync_BannerProgramado_ValidFromEnElFuturo_NoAparece()
    {
        var dbName = Guid.NewGuid().ToString();
        var id = Guid.NewGuid();

        await using (var seed = NewContext(dbName))
        {
            seed.Banners.Add(Row(
                id, "Programado", isActive: true,
                validFrom: Now.AddDays(1), validUntil: Now.AddDays(10)));
            await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var ctx = NewContext(dbName);
        var repo = new BannerRepository(ctx);

        var activos = await repo.ListActiveAsync(Now, TestContext.Current.CancellationToken);

        activos.Should().BeEmpty("un banner Programado NO aparece en el endpoint de consumo (AC1)");
    }

    [Fact]
    public async Task ListActiveAsync_BannerExpirado_ValidUntilEnElPasado_NoAparece()
    {
        var dbName = Guid.NewGuid().ToString();
        var id = Guid.NewGuid();

        await using (var seed = NewContext(dbName))
        {
            seed.Banners.Add(Row(
                id, "Expirado", isActive: true,
                validFrom: Now.AddDays(-10), validUntil: Now.AddDays(-1)));
            await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var ctx = NewContext(dbName);
        var repo = new BannerRepository(ctx);

        var activos = await repo.ListActiveAsync(Now, TestContext.Current.CancellationToken);

        activos.Should().BeEmpty("un banner Expirado no debe seguir apareciendo aunque is_active siga en true");
    }

    [Fact]
    public async Task ListActiveAsync_BannerVigenteDentroDelRango_Aparece()
    {
        var dbName = Guid.NewGuid().ToString();
        var id = Guid.NewGuid();

        await using (var seed = NewContext(dbName))
        {
            seed.Banners.Add(Row(
                id, "Vigente", isActive: true,
                validFrom: Now.AddDays(-1), validUntil: Now.AddDays(1)));
            await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var ctx = NewContext(dbName);
        var repo = new BannerRepository(ctx);

        var activos = await repo.ListActiveAsync(Now, TestContext.Current.CancellationToken);

        activos.Should().ContainSingle(b => b.Id == id);
    }

    [Fact]
    public async Task ListActiveAsync_SinBannersActivos_DevuelveListaVacia()
    {
        await using var ctx = NewContext(Guid.NewGuid().ToString());
        var repo = new BannerRepository(ctx);

        var activos = await repo.ListActiveAsync(Now, TestContext.Current.CancellationToken);

        activos.Should().BeEmpty();
    }

    [Fact]
    public async Task ListActiveAsync_BannerEliminadoLogicamente_NoAparece()
    {
        var dbName = Guid.NewGuid().ToString();
        var id = Guid.NewGuid();

        await using (var seed = NewContext(dbName))
        {
            var fila = Row(id, "Borrado", isActive: true, validFrom: null, validUntil: null);
            fila.DeletedAt = Now.AddDays(-1);
            seed.Banners.Add(fila);
            await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var ctx = NewContext(dbName);
        var repo = new BannerRepository(ctx);

        var activos = await repo.ListActiveAsync(Now, TestContext.Current.CancellationToken);

        activos.Should().BeEmpty();
    }

    [Fact]
    public async Task GetImageRefAsync_BannerExistente_DevuelveStoragePathYSha256()
    {
        var dbName = Guid.NewGuid().ToString();
        var id = Guid.NewGuid();

        await using (var seed = NewContext(dbName))
        {
            var fila = Row(id, "Con imagen", isActive: false, validFrom: null, validUntil: null);
            fila.ImageStoragePath = "fm://banners/abc123";
            fila.ImageSha256 = new string('a', 64);
            seed.Banners.Add(fila);
            await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var ctx = NewContext(dbName);
        var repo = new BannerRepository(ctx);

        var imageRef = await repo.GetImageRefAsync(id, TestContext.Current.CancellationToken);

        imageRef.Should().NotBeNull("un banner inactivo aun asi puede servir su imagen (AC3 solo excluye inexistente/eliminado)");
        imageRef!.StoragePath.Should().Be("fm://banners/abc123");
        imageRef.Sha256.Should().Be(new string('a', 64));
    }

    [Fact]
    public async Task GetImageRefAsync_BannerInexistente_DevuelveNull()
    {
        await using var ctx = NewContext(Guid.NewGuid().ToString());
        var repo = new BannerRepository(ctx);

        var imageRef = await repo.GetImageRefAsync(Guid.NewGuid(), TestContext.Current.CancellationToken);

        imageRef.Should().BeNull();
    }

    [Fact]
    public async Task GetImageRefAsync_BannerEliminadoLogicamente_DevuelveNull()
    {
        var dbName = Guid.NewGuid().ToString();
        var id = Guid.NewGuid();

        await using (var seed = NewContext(dbName))
        {
            var fila = Row(id, "Borrado", isActive: true, validFrom: null, validUntil: null);
            fila.DeletedAt = Now.AddDays(-1);
            seed.Banners.Add(fila);
            await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var ctx = NewContext(dbName);
        var repo = new BannerRepository(ctx);

        var imageRef = await repo.GetImageRefAsync(id, TestContext.Current.CancellationToken);

        imageRef.Should().BeNull("mismo 404 que inexistente (AC3): no se distingue el motivo");
    }

    // Helpers ────────────────────────────────────────────────────────────────────

    private static Banner Row(
        Guid id, string name, bool isActive, DateTimeOffset? validFrom, DateTimeOffset? validUntil) => new()
    {
        Id = id,
        Name = name,
        ImageStoragePath = "fm://banners/" + id,
        ImageSha256 = new string('b', 64),
        LinkUrl = null,
        ValidFrom = validFrom,
        ValidUntil = validUntil,
        IsActive = isActive,
        CreatedAt = Now.AddDays(-30),
    };

    private static FlitDbContext NewContext(string dbName) =>
        new(new DbContextOptionsBuilder<FlitDbContext>().UseInMemoryDatabase(dbName).Options);
}
