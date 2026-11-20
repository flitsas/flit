using Flit.Admin.Application.Companies.TransitOffices.PortalMarca;
using Flit.Admin.Domain.Companies.TransitOffices.PortalMarca;
using FluentAssertions;
using Xunit;

namespace Flit.Admin.Tests.PortalMarca;

/// <summary>Mejora de la Épica #14297 — lista de banners (hasta 6) del portal del organismo (borrador → publicar).</summary>
public sealed partial class PortalMarcaHandlerTests
{
    private async Task<PortalMarcaDto> AgregarBannerAsync(string alt, string? enlace = null, string nombre = "b.png", byte[]? bytes = null)
    {
        using var s = new MemoryStream(bytes ?? ImagenesDePrueba.Png());
        var r = await _imagenes.AgregarBannerAsync(Organismo, nombre, s, alt, enlace, Actor, Ct);
        r.Error.Should().BeNull();
        return r.Valor!;
    }

    [Fact]
    public async Task Banners_SeAgreganAlFinalConAltYEnlace_ElPrimeroEsElBannerLegacy_YQuedaAuditado()
    {
        var uno = await AgregarBannerAsync("  Primero  ", "https://tramites.example.test/a");
        var dos = await AgregarBannerAsync("Segundo");

        dos.Banners.Select(i => i.Alt).Should().Equal("Primero", "Segundo");
        dos.Banners[0].LinkUrl.Should().Be("https://tramites.example.test/a");
        dos.Banners[1].LinkUrl.Should().BeNull();
        dos.Banners[0].ImageUrl.Should().Be($"{PortalMarcaAdminHandler.RutaImagenes}{uno.Banners[0].ImageId}");
        dos.Draft.Banners.Select(i => i.ImageId).Should().Equal(dos.Banners.Select(i => i.ImageId));
        dos.Draft.BannerId.Should().Be(dos.Banners[0].ImageId);
        dos.BannerUrl.Should().Be(dos.Banners[0].ImageUrl, "bannerUrl legacy = el primer banner");
        (await _imagenes.BuscarAsync(dos.Banners[1].ImageId, Ct))!.Kind.Should().Be(PortalMarcaImagen.KindBanner);
        _auditoria.Entradas.Should().Contain(a => a.EntityName == "portal_marca_banners");
    }

    [Fact]
    public async Task Banners_ConSeisLaSeptimaDa409BannersLlenoYNoSeGuardaElArchivo()
    {
        for (var i = 1; i <= 6; i++)
            await AgregarBannerAsync($"Banner {i}");
        var archivosAntes = _almacen.Archivos.Count;

        using var s = new MemoryStream(ImagenesDePrueba.Png());
        var r = await _imagenes.AgregarBannerAsync(Organismo, "x.png", s, "Séptimo", null, Actor, Ct);

        r.Error!.Status.Should().Be(409);
        r.Error.Code.Should().Be("BANNERS_LLENO");
        _almacen.Archivos.Should().HaveCount(archivosAntes);
        (await _admin.ObtenerAsync(Organismo, Ct)).Banners.Should().HaveCount(6);
    }

    [Theory]
    [InlineData("b.gif", 64, "PNG, JPEG o WebP")]
    [InlineData("grande.png", 2 * 1024 * 1024, "2 MB")]
    public async Task Banners_UnArchivoInvalidoDa400ArchivoInvalidoYNoSeGuarda(string nombre, int relleno, string fragmento)
    {
        using var s = new MemoryStream(ImagenesDePrueba.Png(relleno));

        var r = await _imagenes.AgregarBannerAsync(Organismo, nombre, s, "Alt", null, Actor, Ct);

        r.Error!.Status.Should().Be(400);
        r.Error.Code.Should().Be(PortalMarcaErrores.ArchivoInvalido);
        r.Error.Message.Should().Contain(fragmento);
        _almacen.Archivos.Should().BeEmpty();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Banners_ElTextoAlternativoEsObligatorio(string alt)
    {
        using var s = new MemoryStream(ImagenesDePrueba.Png());
        var r = await _imagenes.AgregarBannerAsync(Organismo, "b.png", s, alt, null, Actor, Ct);
        r.Error!.Code.Should().Be(PortalMarcaErrores.Datos);
        _almacen.Archivos.Should().BeEmpty();
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("ftp://example.test/a")]
    [InlineData("no es un enlace")]
    public async Task Banners_ElEnlaceDebeSerHttpOHttps(string enlace)
    {
        using var s = new MemoryStream(ImagenesDePrueba.Png());
        var r = await _imagenes.AgregarBannerAsync(Organismo, "b.png", s, "Alt", enlace, Actor, Ct);
        r.Error!.Code.Should().Be(PortalMarcaErrores.Datos);
    }

    [Fact]
    public async Task Banners_EditarReordenarYQuitar_YElPrimeroSiempreEsElBannerId()
    {
        var a = (await AgregarBannerAsync("A")).Banners[0].ImageId;
        var b = (await AgregarBannerAsync("B")).Banners[1].ImageId;

        var editado = (await _imagenes.ActualizarBannerAsync(Organismo, b, " B2 ", "http://example.test/b", Actor, Ct)).Valor!;
        editado.Banners[1].Alt.Should().Be("B2");
        editado.Banners[1].LinkUrl.Should().Be("http://example.test/b");

        var orden = (await _imagenes.OrdenarBannersAsync(Organismo, [b, a], Actor, Ct)).Valor!;
        orden.Banners.Select(i => i.ImageId).Should().Equal(b, a);
        orden.Draft.BannerId.Should().Be(b);
        (await _imagenes.OrdenarBannersAsync(Organismo, [a], Actor, Ct)).Error!.Code.Should().Be(PortalMarcaErrores.Datos);
        (await _imagenes.OrdenarBannersAsync(Organismo, [a, a], Actor, Ct)).Error!.Code.Should().Be(PortalMarcaErrores.Datos);

        var quitado = (await _imagenes.QuitarBannerAsync(Organismo, b, Actor, Ct)).Valor!;
        quitado.Banners.Select(i => i.ImageId).Should().Equal(a);
        quitado.Draft.BannerId.Should().Be(a);
        (await _imagenes.QuitarBannerAsync(Organismo, b, Actor, Ct)).Error!.Status.Should().Be(404);
        (await _imagenes.ActualizarBannerAsync(Organismo, Guid.NewGuid(), "x", null, Actor, Ct)).Error!.Code.Should().Be(PortalMarcaErrores.NoEncontrada);
    }

    [Fact]
    public async Task Banners_ElAmbitoWebNoLosAdmite()
    {
        using var s = new MemoryStream(ImagenesDePrueba.Png());
        (await _imagenes.AgregarBannerAsync(PortalMarcaAmbito.Web, "b.png", s, "Alt", null, Actor, Ct)).Error!.Code.Should().Be(PortalMarcaErrores.Datos);
        (await _imagenes.QuitarBannerAsync(PortalMarcaAmbito.Web, Guid.NewGuid(), Actor, Ct)).Error!.Code.Should().Be(PortalMarcaErrores.Datos);
    }

    [Fact]
    public async Task Banners_PublicarLosCopia_ElPortalSoloVeLosPublicados_YCambiarlosCuentaComoCambioSinPublicar()
    {
        await GuardarValidoAsync();
        var conBanner = await AgregarBannerAsync("Publicado", "https://example.test/p");
        var publicada = (await _admin.PublicarAsync(Organismo, conBanner.RowVersion, Actor, Ct)).Valor!;
        var enPublicado = publicada.Banners[0].ImageId;
        publicada.Published!.Banners.Should().ContainSingle().Which
            .Should().Be(new PortalMarcaCarruselSnapshotItemDto(enPublicado, "Publicado", "https://example.test/p"));
        publicada.HasUnpublishedChanges.Should().BeFalse();

        var soloBorrador = (await AgregarBannerAsync("Borrador")).Banners[1].ImageId;
        (await _admin.ObtenerAsync(Organismo, Ct)).HasUnpublishedChanges.Should().BeTrue();

        var marca = await _punto.ObtenerAsync("OT-A", Ct);
        marca.Banners.Should().ContainSingle().Which.ImageId.Should().Be(enPublicado);
        marca.BannerId.Should().Be(enPublicado, "bannerId = el primero");
        (await _punto.ObtenerImagenAsync(enPublicado, null, Ct)).Encontrada.Should().BeTrue();
        (await _punto.ObtenerImagenAsync(soloBorrador, null, Ct)).Encontrada.Should().BeFalse("una imagen de borrador no se sirve");
    }

    [Fact]
    public async Task Banners_PublicarUnCambioDeBannersSubeLaVersionPublicada()
    {
        await GuardarValidoAsync();
        var v1 = (await _admin.PublicarAsync(Organismo, (await AgregarBannerAsync("Uno")).RowVersion, Actor, Ct)).Valor!;
        var dos = await AgregarBannerAsync("Dos");
        dos.HasUnpublishedChanges.Should().BeTrue();

        var v2 = (await _admin.PublicarAsync(Organismo, dos.RowVersion, Actor, Ct)).Valor!;

        v2.PublishedVersion.Should().Be(v1.PublishedVersion + 1);
        (await _punto.ObtenerAsync("OT-A", Ct)).Version.Should().Be(v2.PublishedVersion);
        (await _punto.ObtenerAsync("OT-A", Ct)).Banners.Should().HaveCount(2);
    }

    [Fact]
    public async Task Banners_ElEndpointLegacyDelBannerUnicoOperaSobreElPrimero()
    {
        var a = (await AgregarBannerAsync("A", "https://example.test/a")).Banners[0].ImageId;
        await AgregarBannerAsync("B");

        using var s = new MemoryStream(ImagenesDePrueba.Png());
        var reemplazo = (await _imagenes.SubirAsync(Organismo, PortalMarcaImagen.KindBanner, "n.png", s, Actor, Ct)).Valor!;
        reemplazo.Banners.Should().HaveCount(2);
        reemplazo.Banners[0].ImageId.Should().NotBe(a);
        reemplazo.Banners[0].Alt.Should().Be("A", "conserva su texto alternativo y enlace");
        reemplazo.Banners[0].LinkUrl.Should().Be("https://example.test/a");

        var sinPrimero = await _imagenes.QuitarAsync(Organismo, PortalMarcaImagen.KindBanner, Actor, Ct);
        sinPrimero.Banners.Select(i => i.Alt).Should().Equal("B");
    }
}
