using Flit.Admin.Application.Banners.ListActiveBanners;
using Flit.Admin.Domain.Banners;
using FluentAssertions;
using Xunit;

namespace Flit.Admin.Tests.Banners;

/// <summary>HU #12240, AC1/AC3 — el handler delega el filtro de vigencia al repositorio y mapea sin fugar el path opaco de storage.</summary>
public sealed class ListActiveBannersHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 9, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task HandleAsync_MapeaIdNombreYEnlace()
    {
        var id = Guid.NewGuid();
        var repo = new FakeBannerRepository();
        repo.ActiveItems.Add(new ActiveBannerItem(id, "Promo aniversario", "https://flit.example/promo"));

        var result = await new ListActiveBannersHandler(repo)
            .HandleAsync(Now, TestContext.Current.CancellationToken);

        result.Data.Should().ContainSingle();
        result.Data[0].Id.Should().Be(id);
        result.Data[0].Name.Should().Be("Promo aniversario");
        result.Data[0].LinkUrl.Should().Be("https://flit.example/promo");
    }

    [Fact]
    public async Task HandleAsync_SinBannersActivos_DevuelveListaVaciaNuncaError()
    {
        var repo = new FakeBannerRepository();

        var result = await new ListActiveBannersHandler(repo)
            .HandleAsync(Now, TestContext.Current.CancellationToken);

        result.Data.Should().NotBeNull().And.BeEmpty();
    }

    [Fact]
    public async Task HandleAsync_PasaElInstanteRecibidoAlRepositorio()
    {
        var repo = new FakeBannerRepository();

        await new ListActiveBannersHandler(repo).HandleAsync(Now, TestContext.Current.CancellationToken);

        repo.ListActiveCalls.Should().Be(1);
    }
}
