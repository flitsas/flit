using System.Security.Cryptography;
using Flit.Admin.Application.Banners;
using Flit.Admin.Application.Banners.CreateBanner;
using Flit.Admin.Application.Banners.DeleteBanner;
using Flit.Admin.Application.Banners.ListBanners;
using Flit.Admin.Application.Banners.Ports;
using Flit.Admin.Application.Banners.SetBannerActive;
using Flit.Admin.Application.Banners.UpdateBanner;
using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Flit.Admin.Tests.Banners;

public sealed class BannerHandlerTests
{
    private static readonly Guid Actor = Guid.Parse("11111111-1111-1111-1111-111111111111");

    // ---------- AC1: POST crea banner con imagen ----------

    [Fact]
    public async Task AC1_Create_PersistsBanner_SavesImage_AndReturnsIt()
    {
        var db = NewDbName();
        var storage = new FakeBannerImageStorage();

        await using var act = NewContext(db);
        var handler = new CreateBannerHandler(new BannerRepository(act), storage, new FixedTimeProvider(Now));

        var result = await handler.HandleAsync(new CreateBannerCommand
        {
            Name = "Promo verano",
            LinkUrl = "https://flitsas.com/promo",
            ValidFrom = null,
            ValidUntil = null,
            ImageFilename = "banner.png",
            ImageContentType = "image/png",
            ImageSizeBytes = 10,
            ImageContent = NewImageStream(10),
            CreatedBy = Actor,
        }, TestContext.Current.CancellationToken);

        result.IsValid.Should().BeTrue();
        result.Banner.Should().NotBeNull();
        result.Banner!.Name.Should().Be("Promo verano");
        result.Banner.Estado.Should().Be(BannerResponse.EstadoActivo);
        // HU12241 — ImageUrl debe apuntar a la ruta REAL del endpoint publico
        // (montado bajo /api/v1/public/..., ver PublicBannersEndpoints), no a /public/... a secas.
        result.Banner.ImageUrl.Should().Be($"/api/v1/public/banners/{result.Banner.Id}/image");
        storage.SaveCount.Should().Be(1);

        var row = await act.Banners.SingleAsync(b => b.Id == result.Banner.Id, TestContext.Current.CancellationToken);
        row.ImageSha256.Should().NotBeNullOrWhiteSpace();
        row.CreatedBy.Should().Be(Actor);
        row.IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task AC1_Create_MissingName_Returns422_WithoutPersisting()
    {
        var db = NewDbName();
        var storage = new FakeBannerImageStorage();

        await using var act = NewContext(db);
        var handler = new CreateBannerHandler(new BannerRepository(act), storage, new FixedTimeProvider(Now));

        var result = await handler.HandleAsync(new CreateBannerCommand
        {
            Name = " ",
            ImageFilename = "banner.png",
            ImageContentType = "image/png",
            ImageSizeBytes = 10,
            ImageContent = NewImageStream(10),
        }, TestContext.Current.CancellationToken);

        result.IsValid.Should().BeFalse();
        storage.SaveCount.Should().Be(0);
        (await act.Banners.CountAsync(TestContext.Current.CancellationToken)).Should().Be(0);
    }

    [Fact]
    public async Task AC5_Create_ImageTooLarge_Returns422_WithoutPersisting()
    {
        var db = NewDbName();
        var storage = new FakeBannerImageStorage();

        await using var act = NewContext(db);
        var handler = new CreateBannerHandler(new BannerRepository(act), storage, new FixedTimeProvider(Now));

        var result = await handler.HandleAsync(new CreateBannerCommand
        {
            Name = "Promo",
            ImageFilename = "banner.png",
            ImageContentType = "image/png",
            ImageSizeBytes = BannerValidator.MaxImageSizeBytes + 1,
            ImageContent = NewImageStream(10),
        }, TestContext.Current.CancellationToken);

        result.IsValid.Should().BeFalse();
        storage.SaveCount.Should().Be(0);
        (await act.Banners.CountAsync(TestContext.Current.CancellationToken)).Should().Be(0);
    }

    [Fact]
    public async Task AC5_Create_ImageWrongMime_Returns422_WithoutPersisting()
    {
        var db = NewDbName();
        var storage = new FakeBannerImageStorage();

        await using var act = NewContext(db);
        var handler = new CreateBannerHandler(new BannerRepository(act), storage, new FixedTimeProvider(Now));

        var result = await handler.HandleAsync(new CreateBannerCommand
        {
            Name = "Promo",
            ImageFilename = "banner.gif",
            ImageContentType = "image/gif",
            ImageSizeBytes = 10,
            ImageContent = NewImageStream(10),
        }, TestContext.Current.CancellationToken);

        result.IsValid.Should().BeFalse();
        storage.SaveCount.Should().Be(0);
    }

    // ---------- AC2: GET listado con Estado calculado (4 estados) ----------

    [Fact]
    public async Task AC2_List_Estado_Activo_SinFechas()
    {
        var db = NewDbName();
        await SeedAsync(db, NewBanner(isActive: true, validFrom: null, validUntil: null));

        await using var ctx = NewContext(db);
        var handler = new ListBannersHandler(new BannerRepository(ctx), new FixedTimeProvider(Now));
        var result = await handler.HandleAsync(new ListBannersQuery(), TestContext.Current.CancellationToken);

        result.Data.Single().Estado.Should().Be(BannerResponse.EstadoActivo);
    }

    [Fact]
    public async Task AC2_List_Estado_Programado_ValidFromEnElFuturo()
    {
        var db = NewDbName();
        await SeedAsync(db, NewBanner(isActive: true, validFrom: Now.AddDays(1), validUntil: Now.AddDays(10)));

        await using var ctx = NewContext(db);
        var handler = new ListBannersHandler(new BannerRepository(ctx), new FixedTimeProvider(Now));
        var result = await handler.HandleAsync(new ListBannersQuery(), TestContext.Current.CancellationToken);

        result.Data.Single().Estado.Should().Be(BannerResponse.EstadoProgramado);
    }

    [Fact]
    public async Task AC2_List_Estado_Activo_DentroDeVigencia()
    {
        var db = NewDbName();
        await SeedAsync(db, NewBanner(isActive: true, validFrom: Now.AddDays(-1), validUntil: Now.AddDays(1)));

        await using var ctx = NewContext(db);
        var handler = new ListBannersHandler(new BannerRepository(ctx), new FixedTimeProvider(Now));
        var result = await handler.HandleAsync(new ListBannersQuery(), TestContext.Current.CancellationToken);

        result.Data.Single().Estado.Should().Be(BannerResponse.EstadoActivo);
    }

    [Fact]
    public async Task AC2_List_Estado_Expirado_ValidUntilYaPaso()
    {
        var db = NewDbName();
        await SeedAsync(db, NewBanner(isActive: true, validFrom: Now.AddDays(-10), validUntil: Now.AddDays(-1)));

        await using var ctx = NewContext(db);
        var handler = new ListBannersHandler(new BannerRepository(ctx), new FixedTimeProvider(Now));
        var result = await handler.HandleAsync(new ListBannersQuery(), TestContext.Current.CancellationToken);

        result.Data.Single().Estado.Should().Be(BannerResponse.EstadoExpirado);
    }

    [Fact]
    public async Task AC2_List_Estado_Inactivo_CuandoIsActiveFalse()
    {
        var db = NewDbName();
        await SeedAsync(db, NewBanner(isActive: false, validFrom: null, validUntil: null));

        await using var ctx = NewContext(db);
        var handler = new ListBannersHandler(new BannerRepository(ctx), new FixedTimeProvider(Now));
        var result = await handler.HandleAsync(new ListBannersQuery(), TestContext.Current.CancellationToken);

        result.Data.Single().Estado.Should().Be(BannerResponse.EstadoInactivo);
    }

    [Fact]
    public async Task AC2_List_Estado_Inactivo_DominaSobreExpirado_CuandoIsActiveFalse()
    {
        // Decision de precedencia (documentada en BannerEstado): is_active=false domina sobre
        // cualquier estado temporal, incluido un banner cuya vigencia ya expiro.
        var db = NewDbName();
        await SeedAsync(db, NewBanner(isActive: false, validFrom: Now.AddDays(-10), validUntil: Now.AddDays(-1)));

        await using var ctx = NewContext(db);
        var handler = new ListBannersHandler(new BannerRepository(ctx), new FixedTimeProvider(Now));
        var result = await handler.HandleAsync(new ListBannersQuery(), TestContext.Current.CancellationToken);

        result.Data.Single().Estado.Should().Be(BannerResponse.EstadoInactivo);
    }

    // ---------- AC3: PATCH activar/desactivar sin fechas ----------

    [Fact]
    public async Task AC3_SetActive_TogglesFlag_WithoutDates()
    {
        var db = NewDbName();
        var id = Guid.NewGuid();
        await SeedAsync(db, NewBanner(isActive: true, validFrom: null, validUntil: null, id: id));

        await using (var act = NewContext(db))
        {
            var handler = new SetBannerActiveHandler(new BannerRepository(act));
            var result = await handler.HandleAsync(
                new SetBannerActiveCommand { Id = id, IsActive = false, UpdatedBy = Actor },
                TestContext.Current.CancellationToken);

            result.Outcome.Should().Be(SetBannerActiveOutcome.Updated);
        }

        await using var verify = NewContext(db);
        var row = await verify.Banners.SingleAsync(b => b.Id == id, TestContext.Current.CancellationToken);
        row.IsActive.Should().BeFalse();
        row.UpdatedBy.Should().Be(Actor);
    }

    [Fact]
    public async Task AC3_SetActive_NonExistent_ReturnsNotFound()
    {
        await using var ctx = NewContext(NewDbName());
        var handler = new SetBannerActiveHandler(new BannerRepository(ctx));

        var result = await handler.HandleAsync(
            new SetBannerActiveCommand { Id = Guid.NewGuid(), IsActive = true },
            TestContext.Current.CancellationToken);

        result.Outcome.Should().Be(SetBannerActiveOutcome.NotFound);
    }

    // ---------- AC4: DELETE con confirmacion ----------

    [Fact]
    public async Task AC4_Delete_WithoutConfirm_ReturnsConfirmationRequired_WithoutDeleting()
    {
        var db = NewDbName();
        var id = Guid.NewGuid();
        await SeedAsync(db, NewBanner(isActive: true, validFrom: null, validUntil: null, id: id));

        await using var ctx = NewContext(db);
        var handler = new DeleteBannerHandler(new BannerRepository(ctx));
        var result = await handler.HandleAsync(
            new DeleteBannerCommand { Id = id, Confirm = false },
            TestContext.Current.CancellationToken);

        result.Outcome.Should().Be(DeleteBannerOutcome.ConfirmationRequired);
        (await ctx.Banners.SingleAsync(b => b.Id == id, TestContext.Current.CancellationToken)).DeletedAt.Should().BeNull();
    }

    [Fact]
    public async Task AC4_Delete_WithConfirm_SoftDeletes()
    {
        var db = NewDbName();
        var id = Guid.NewGuid();
        await SeedAsync(db, NewBanner(isActive: true, validFrom: null, validUntil: null, id: id));

        await using (var act = NewContext(db))
        {
            var handler = new DeleteBannerHandler(new BannerRepository(act));
            var result = await handler.HandleAsync(
                new DeleteBannerCommand { Id = id, Confirm = true, DeletedBy = Actor },
                TestContext.Current.CancellationToken);

            result.Outcome.Should().Be(DeleteBannerOutcome.Deleted);
        }

        await using var verify = NewContext(db);
        var row = await verify.Banners.SingleAsync(b => b.Id == id, TestContext.Current.CancellationToken);
        row.DeletedAt.Should().NotBeNull();
        row.DeletedBy.Should().Be(Actor);
    }

    [Fact]
    public async Task AC4_Delete_NonExistent_ReturnsNotFound()
    {
        await using var ctx = NewContext(NewDbName());
        var handler = new DeleteBannerHandler(new BannerRepository(ctx));

        var result = await handler.HandleAsync(
            new DeleteBannerCommand { Id = Guid.NewGuid(), Confirm = true },
            TestContext.Current.CancellationToken);

        result.Outcome.Should().Be(DeleteBannerOutcome.NotFound);
    }

    [Fact]
    public async Task AC4_Update_AlreadyDeleted_ReturnsNotFound()
    {
        var db = NewDbName();
        var id = Guid.NewGuid();
        var seeded = NewBanner(isActive: true, validFrom: null, validUntil: null, id: id);
        seeded.DeletedAt = Now;
        await SeedAsync(db, seeded);

        await using var ctx = NewContext(db);
        var storage = new FakeBannerImageStorage();
        var handler = new UpdateBannerHandler(new BannerRepository(ctx), storage, new FixedTimeProvider(Now));

        var result = await handler.HandleAsync(new UpdateBannerCommand
        {
            Id = id,
            Name = "Nuevo nombre",
        }, TestContext.Current.CancellationToken);

        result.Outcome.Should().Be(UpdateBannerOutcome.NotFound);
    }

    // ---------- Update: reemplazo de imagen recalcula el hash ----------

    [Fact]
    public async Task Update_ReplacesImage_RecalculatesSha256()
    {
        var db = NewDbName();
        var id = Guid.NewGuid();
        var seeded = NewBanner(isActive: true, validFrom: null, validUntil: null, id: id);
        seeded.ImageSha256 = "old-hash-placeholder-000000000000000000000000000000000000000000000";
        await SeedAsync(db, seeded);

        var storage = new FakeBannerImageStorage();

        await using (var act = NewContext(db))
        {
            var handler = new UpdateBannerHandler(new BannerRepository(act), storage, new FixedTimeProvider(Now));
            var result = await handler.HandleAsync(new UpdateBannerCommand
            {
                Id = id,
                Name = "Nombre actualizado",
                ImageFilename = "nuevo.png",
                ImageContentType = "image/png",
                ImageSizeBytes = 20,
                ImageContent = NewImageStream(20),
                UpdatedBy = Actor,
            }, TestContext.Current.CancellationToken);

            result.Outcome.Should().Be(UpdateBannerOutcome.Updated);
        }

        storage.SaveCount.Should().Be(1);
        await using var verify = NewContext(db);
        var row = await verify.Banners.SingleAsync(b => b.Id == id, TestContext.Current.CancellationToken);
        row.ImageSha256.Should().NotBe("old-hash-placeholder-000000000000000000000000000000000000000000000");
        row.Name.Should().Be("Nombre actualizado");
    }

    [Fact]
    public async Task Update_WithoutNewImage_KeepsExistingImage()
    {
        var db = NewDbName();
        var id = Guid.NewGuid();
        var seeded = NewBanner(isActive: true, validFrom: null, validUntil: null, id: id);
        await SeedAsync(db, seeded);
        var originalPath = seeded.ImageStoragePath;

        var storage = new FakeBannerImageStorage();
        await using var act = NewContext(db);
        var handler = new UpdateBannerHandler(new BannerRepository(act), storage, new FixedTimeProvider(Now));
        var result = await handler.HandleAsync(new UpdateBannerCommand
        {
            Id = id,
            Name = "Solo cambia el nombre",
        }, TestContext.Current.CancellationToken);

        result.Outcome.Should().Be(UpdateBannerOutcome.Updated);
        storage.SaveCount.Should().Be(0);

        var row = await act.Banners.SingleAsync(b => b.Id == id, TestContext.Current.CancellationToken);
        row.ImageStoragePath.Should().Be(originalPath);
    }

    // ---------- Helpers ----------

    private static readonly DateTimeOffset Now = new(2026, 9, 9, 12, 0, 0, TimeSpan.Zero);

    private static string NewDbName() => string.Concat("flit-banners-", Guid.NewGuid().ToString());

    private static FlitDbContext NewContext(string dbName) =>
        new(new DbContextOptionsBuilder<FlitDbContext>().UseInMemoryDatabase(dbName).Options);

    private static async Task SeedAsync(string dbName, params Flit.Infrastructure.Persistence.Entities.Admin.Banner[] banners)
    {
        await using var ctx = NewContext(dbName);
        ctx.Banners.AddRange(banners);
        await ctx.SaveChangesAsync();
    }

    private static Flit.Infrastructure.Persistence.Entities.Admin.Banner NewBanner(
        bool isActive, DateTimeOffset? validFrom, DateTimeOffset? validUntil, Guid? id = null) => new()
    {
        Id = id ?? Guid.NewGuid(),
        Name = "Banner de prueba",
        ImageStoragePath = "fake-storage/seed.png",
        ImageSha256 = "0000000000000000000000000000000000000000000000000000000000000000",
        IsActive = isActive,
        ValidFrom = validFrom,
        ValidUntil = validUntil,
        CreatedAt = Now,
    };

    private static MemoryStream NewImageStream(int size)
    {
        var bytes = new byte[size];
        RandomNumberGenerator.Fill(bytes);
        return new MemoryStream(bytes);
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _now;

        public FixedTimeProvider(DateTimeOffset now)
        {
            _now = now;
        }

        public override DateTimeOffset GetUtcNow() => _now;
    }

    private sealed class FakeBannerImageStorage : IBannerImageStorage
    {
        public int SaveCount { get; private set; }

        public Task<StoredBannerImage> SaveAsync(string filename, Stream content, CancellationToken cancellationToken = default)
        {
            SaveCount++;

            using var ms = new MemoryStream();
            content.CopyTo(ms);
            var bytes = ms.ToArray();
            var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
            var path = string.Concat("fake-storage/", filename);

            return Task.FromResult(new StoredBannerImage(path, hash, bytes.LongLength));
        }

        public Task<Stream?> OpenReadAsync(string storagePath, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException(
                "Este fake solo cubre el flujo de escritura del CRUD (HU #12239); la lectura del " +
                "endpoint publico (HU #12240) se prueba en GetBannerImageHandlerTests con el fake " +
                "de BannerTestDoubles.cs.");
    }
}
