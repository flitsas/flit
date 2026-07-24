using Flit.Admin.Application.Companies.Deeds;
using Flit.Admin.Application.Companies.Deeds.CreateDeed;
using Flit.Admin.Application.Companies.Deeds.DeleteDeed;
using Flit.Admin.Application.Companies.Deeds.GetDeed;
using Flit.Admin.Application.Companies.Deeds.ListDeeds;
using Flit.Admin.Application.Companies.Deeds.UpdateDeed;
using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Flit.Admin.Tests.Companies.Deeds;

/// <summary>
/// Tests del CRUD de escrituras (HU #10902, ADR-0033) ejercitando los handlers reales sobre
/// <see cref="DbDeedReader"/> + <see cref="DeedRepository"/> (InMemory) y un storage de documento en
/// memoria (sin red). Cubren: alta feliz (registra artefacto + persiste path/hash + puente a
/// compañías), validación del rango de vigencia (422), compañías requeridas (422), documento
/// requerido (422), listado paginado con envelope, edición con y sin reemplazo de PDF, baja lógica
/// idempotente y presigned URL de vista en el detalle. El PDF NUNCA se persiste en BD.
/// </summary>
public sealed class DeedHandlerTests
{
    private static readonly Guid Tenant = Guid.Parse("88888888-0000-4000-8000-000000000001");
    private static readonly Guid CompanyA = Guid.Parse("11111111-0000-4000-8000-000000000001");
    private static readonly Guid CompanyB = Guid.Parse("22222222-0000-4000-8000-000000000002");

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Create_HappyPath_RegistersArtifact_PersistsPathHashAndCompanies()
    {
        await using var ctx = NewContext();
        var (create, list, get, _, _) = Handlers(ctx, out var storage);

        var result = await create.HandleAsync(NewCreate(), Ct);

        result.IsValid.Should().BeTrue();
        result.DeedId.Should().NotBeNull();
        result.Upload.Should().NotBeNull();
        result.Upload!.StoragePath.Should().Be("fm-deed-abc");   // ticket de subida devuelto al cliente.
        storage.CreateUploadCalls.Should().Be(1);                 // el documento se registró en storage.

        var detail = await get.HandleAsync(
            new GetDeedByIdQuery { TenantId = Tenant, Id = result.DeedId!.Value }, Ct);
        detail.Should().NotBeNull();
        detail!.Deed.StoragePath.Should().Be("fm-deed-abc");      // solo referencia al artefacto.
        detail.Deed.StorageSha256.Should().Be("deadbeef");        // integridad aportada por el cliente.
        detail.Deed.RepresentedCompanyIds.Should().BeEquivalentTo([CompanyA, CompanyB]);
        detail.Deed.IsActive.Should().BeTrue();

        var page = await list.HandleAsync(new ListDeedsQuery { TenantId = Tenant }, Ct);
        page.TotalCount.Should().Be(1);
        page.Data.Should().ContainSingle();
    }

    [Fact]
    public async Task Create_VigenciaInvertida_Returns422()
    {
        await using var ctx = NewContext();
        var (create, _, _, _, _) = Handlers(ctx, out _);

        var result = await create.HandleAsync(
            NewCreate(desde: new DateOnly(2026, 12, 31), hasta: new DateOnly(2026, 1, 1)), Ct);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Field == "vigenciaHasta" && e.Code == "vigencia_invalida");
    }

    [Fact]
    public async Task Create_SinCompanias_Returns422()
    {
        await using var ctx = NewContext();
        var (create, _, _, _, _) = Handlers(ctx, out _);

        var result = await create.HandleAsync(NewCreate(companies: []), Ct);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Field == "representedCompanyIds" && e.Code == "companias_requeridas");
    }

    [Fact]
    public async Task Create_SinHashDocumento_Returns422_AndDoesNotTouchStorage()
    {
        await using var ctx = NewContext();
        var (create, _, _, _, _) = Handlers(ctx, out var storage);

        var result = await create.HandleAsync(NewCreate(sha256: null), Ct);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Field == "sha256" && e.Code == "documento_requerido");
        storage.CreateUploadCalls.Should().Be(0); // no se registra artefacto si la validación falla.
    }

    [Fact]
    public async Task List_IsPaged_WithEnvelope()
    {
        await using var ctx = NewContext();
        var (create, list, _, _, _) = Handlers(ctx, out _);

        for (var i = 0; i < 3; i++)
        {
            (await create.HandleAsync(NewCreate(), Ct)).IsValid.Should().BeTrue();
        }

        var firstPage = await list.HandleAsync(
            new ListDeedsQuery { TenantId = Tenant, Page = 1, PageSize = 2 }, Ct);

        firstPage.TotalCount.Should().Be(3);     // total antes de paginar.
        firstPage.Page.Should().Be(1);
        firstPage.PageSize.Should().Be(2);
        firstPage.Data.Should().HaveCount(2);    // solo la página solicitada.

        var secondPage = await list.HandleAsync(
            new ListDeedsQuery { TenantId = Tenant, Page = 2, PageSize = 2 }, Ct);
        secondPage.Data.Should().ContainSingle();
    }

    [Fact]
    public async Task Update_ReplacesArtifact_WhenHashProvided()
    {
        await using var ctx = NewContext();
        var (create, _, get, update, _) = Handlers(ctx, out var storage);

        var created = await create.HandleAsync(NewCreate(), Ct);
        var id = created.DeedId!.Value;

        var result = await update.HandleAsync(new UpdateDeedCommand
        {
            TenantId = Tenant,
            Id = id,
            Description = "Escritura actualizada",
            VigenciaDesde = new DateOnly(2027, 1, 1),
            VigenciaHasta = new DateOnly(2027, 12, 31),
            Sha256 = "cafef00d",
            RepresentedCompanyIds = [CompanyA],
        }, Ct);

        result.Outcome.Should().Be(UpdateDeedOutcome.Updated);
        result.Upload.Should().NotBeNull();       // reemplazo → nuevo ticket de subida.
        storage.CreateUploadCalls.Should().Be(2); // alta + reemplazo.

        var detail = await get.HandleAsync(new GetDeedByIdQuery { TenantId = Tenant, Id = id }, Ct);
        detail!.Deed.Description.Should().Be("Escritura actualizada");
        detail.Deed.StorageSha256.Should().Be("cafef00d");
        detail.Deed.RepresentedCompanyIds.Should().BeEquivalentTo([CompanyA]);
    }

    [Fact]
    public async Task Update_KeepsArtifact_WhenNoHash()
    {
        await using var ctx = NewContext();
        var (create, _, get, update, _) = Handlers(ctx, out var storage);

        var created = await create.HandleAsync(NewCreate(), Ct);
        var id = created.DeedId!.Value;

        var result = await update.HandleAsync(new UpdateDeedCommand
        {
            TenantId = Tenant,
            Id = id,
            Description = "Solo metadatos",
            VigenciaDesde = new DateOnly(2026, 1, 1),
            VigenciaHasta = new DateOnly(2026, 6, 30),
            Sha256 = null,
            RepresentedCompanyIds = [CompanyB],
        }, Ct);

        result.Outcome.Should().Be(UpdateDeedOutcome.Updated);
        result.Upload.Should().BeNull();          // sin reemplazo → sin ticket.
        storage.CreateUploadCalls.Should().Be(1); // solo el alta.

        var detail = await get.HandleAsync(new GetDeedByIdQuery { TenantId = Tenant, Id = id }, Ct);
        detail!.Deed.StoragePath.Should().Be("fm-deed-abc"); // conserva el artefacto original.
        detail.Deed.StorageSha256.Should().Be("deadbeef");
    }

    [Fact]
    public async Task Update_UnknownId_ReturnsNotFound()
    {
        await using var ctx = NewContext();
        var (_, _, _, update, _) = Handlers(ctx, out _);

        var result = await update.HandleAsync(new UpdateDeedCommand
        {
            TenantId = Tenant,
            Id = Guid.NewGuid(),
            Description = "x",
            VigenciaDesde = new DateOnly(2026, 1, 1),
            VigenciaHasta = new DateOnly(2026, 12, 31),
            RepresentedCompanyIds = [CompanyA],
        }, Ct);

        result.Outcome.Should().Be(UpdateDeedOutcome.NotFound);
    }

    [Fact]
    public async Task Delete_IsIdempotent_AndFlipsIsActive()
    {
        await using var ctx = NewContext();
        var (create, _, get, _, delete) = Handlers(ctx, out _);

        var created = await create.HandleAsync(NewCreate(), Ct);
        var id = created.DeedId!.Value;

        var first = await delete.HandleAsync(new DeleteDeedCommand { TenantId = Tenant, Id = id }, Ct);
        first.Should().Be(DeleteDeedOutcome.Deleted);

        // Dar de baja de nuevo sigue devolviendo Deleted (idempotente).
        var second = await delete.HandleAsync(new DeleteDeedCommand { TenantId = Tenant, Id = id }, Ct);
        second.Should().Be(DeleteDeedOutcome.Deleted);

        var detail = await get.HandleAsync(new GetDeedByIdQuery { TenantId = Tenant, Id = id }, Ct);
        detail!.Deed.IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task Delete_UnknownId_ReturnsNotFound()
    {
        await using var ctx = NewContext();
        var (_, _, _, _, delete) = Handlers(ctx, out _);

        var outcome = await delete.HandleAsync(
            new DeleteDeedCommand { TenantId = Tenant, Id = Guid.NewGuid() }, Ct);

        outcome.Should().Be(DeleteDeedOutcome.NotFound);
    }

    [Fact]
    public async Task GetById_ReturnsNull_WhenAbsent()
    {
        await using var ctx = NewContext();
        var (_, _, get, _, _) = Handlers(ctx, out _);

        var result = await get.HandleAsync(
            new GetDeedByIdQuery { TenantId = Tenant, Id = Guid.NewGuid() }, Ct);

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetById_AttachesShortLivedViewUrl()
    {
        await using var ctx = NewContext();
        var (create, _, get, _, _) = Handlers(ctx, out _);

        var created = await create.HandleAsync(NewCreate(), Ct);

        var detail = await get.HandleAsync(
            new GetDeedByIdQuery { TenantId = Tenant, Id = created.DeedId!.Value }, Ct);

        detail!.ViewUrl.Should().Be("https://s3.example/deed?inline");
        detail.ViewUrlExpiresAt.Should().NotBeNull();
    }

    // ---------- Helpers ----------

    private static CreateDeedCommand NewCreate(
        string? sha256 = "deadbeef",
        DateOnly? desde = null,
        DateOnly? hasta = null,
        IReadOnlyList<Guid>? companies = null) =>
        new()
        {
            TenantId = Tenant,
            Description = "Poder general de representación",
            VigenciaDesde = desde ?? new DateOnly(2026, 1, 1),
            VigenciaHasta = hasta ?? new DateOnly(2026, 12, 31),
            Sha256 = sha256,
            RepresentedCompanyIds = companies ?? [CompanyA, CompanyB],
        };

    private static (
        CreateDeedHandler Create,
        ListDeedsHandler List,
        GetDeedByIdHandler Get,
        UpdateDeedHandler Update,
        DeleteDeedHandler Delete) Handlers(FlitDbContext ctx, out FakeDeedStorage storage)
    {
        var reader = new DbDeedReader(ctx);
        var repo = new DeedRepository(ctx);
        storage = new FakeDeedStorage();
        return (
            new CreateDeedHandler(storage, repo),
            new ListDeedsHandler(reader),
            new GetDeedByIdHandler(reader, storage),
            new UpdateDeedHandler(storage, repo),
            new DeleteDeedHandler(reader, repo));
    }

    private static FlitDbContext NewContext() =>
        new(new DbContextOptionsBuilder<FlitDbContext>()
            .UseInMemoryDatabase($"flit-deeds-{Guid.NewGuid()}")
            .Options);

    /// <summary>Storage de documento en memoria: no toca red; devuelve un ticket/URL fijos.</summary>
    private sealed class FakeDeedStorage : IDeedDocumentStorage
    {
        public int CreateUploadCalls { get; private set; }

        public Task<DeedUploadTicket> CreateUploadAsync(
            Guid tenantId, CancellationToken cancellationToken = default)
        {
            CreateUploadCalls++;
            return Task.FromResult(new DeedUploadTicket(
                "fm-deed-abc",
                "https://s3.example/upload",
                new Dictionary<string, string> { ["key"] = "deeds/fm-deed-abc" }));
        }

        public Task<DeedDocumentView?> GetViewUrlAsync(
            string storagePath, CancellationToken cancellationToken = default) =>
            Task.FromResult<DeedDocumentView?>(
                new DeedDocumentView("https://s3.example/deed?inline", DateTimeOffset.UtcNow.AddMinutes(10)));
    }
}
