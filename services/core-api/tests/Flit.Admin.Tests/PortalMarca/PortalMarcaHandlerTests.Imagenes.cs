using Flit.Admin.Application.Companies.TransitOffices.PortalMarca;
using Flit.Admin.Domain.Companies.TransitOffices.PortalMarca;
using FluentAssertions;
using Xunit;

namespace Flit.Admin.Tests.PortalMarca;

/// <summary>HU #14376 (Feature #14374) — logo y banner del organismo: formato, tamaño, contenido real, versiones y quitar.</summary>
public sealed partial class PortalMarcaHandlerTests
{
    // HU #14376 — cargar logo y banner.
    private PortalMarcaImagenHandler? _imagenesHandler;
    private PortalMarcaImagenHandler _imagenes => _imagenesHandler ??= new PortalMarcaImagenHandler(_repo, _almacen, _admin);

    [Fact]
    public async Task AC1_ElLogoValidoQuedaEnElBorradorConVersionNuevaYLaAnteriorSeConserva()
    {
        using var uno = new MemoryStream(ImagenesDePrueba.Png());
        var primero = (await _imagenes.SubirAsync(Organismo, PortalMarcaImagen.KindLogo, "logo.png", uno, Actor, Ct)).Valor!;
        using var dos = new MemoryStream(ImagenesDePrueba.Jpeg());
        var segundo = (await _imagenes.SubirAsync(Organismo, PortalMarcaImagen.KindLogo, "nuevo.jpg", dos, Actor, Ct)).Valor!;

        segundo.Draft.LogoId!.Value.Should().NotBe(primero.Draft.LogoId!.Value, "cada carga tiene id propio");
        segundo.LogoUrl.Should().EndWith(segundo.Draft.LogoId!.Value.ToString());
        (await _imagenes.BuscarAsync(primero.Draft.LogoId!.Value, Ct)).Should().NotBeNull("la versión anterior se conserva");
        (await _imagenes.BuscarAsync(segundo.Draft.LogoId!.Value, Ct))!.Version.Should().Be(2);
    }

    [Fact]
    public async Task AC1_ElBannerAdmiteHasta2MBYElLogoSoloHasta512KB()
    {
        using var banner = new MemoryStream(ImagenesDePrueba.Webp(2 * 1024 * 1024 - 12));
        (await _imagenes.SubirAsync(Organismo, PortalMarcaImagen.KindBanner, "b.webp", banner, Actor, Ct)).Error.Should().BeNull("2 MB exactos caben");

        using var logoLimite = new MemoryStream(ImagenesDePrueba.Png(512 * 1024 - 8));
        (await _imagenes.SubirAsync(Organismo, PortalMarcaImagen.KindLogo, "l.png", logoLimite, Actor, Ct)).Error.Should().BeNull("512 KB exactos caben");

        using var logoGrande = new MemoryStream(ImagenesDePrueba.Png(512 * 1024));
        var r = await _imagenes.SubirAsync(Organismo, PortalMarcaImagen.KindLogo, "l.png", logoGrande, Actor, Ct);
        r.Error!.Code.Should().Be(PortalMarcaErrores.ArchivoInvalido);
        r.Error.Message.Should().Contain("512 KB");

        using var bannerGrande = new MemoryStream(ImagenesDePrueba.Png(2 * 1024 * 1024));
        (await _imagenes.SubirAsync(Organismo, PortalMarcaImagen.KindBanner, "b.png", bannerGrande, Actor, Ct)).Error!.Message.Should().Contain("2 MB");
    }

    [Theory]
    [InlineData("logo.gif")]
    [InlineData("logo.svg")]
    [InlineData("logo")]
    public async Task AC2_UnArchivoConOtroFormatoNoSeGuardaYDiceElMotivo(string nombre)
    {
        using var s = new MemoryStream(ImagenesDePrueba.Png());

        var r = await _imagenes.SubirAsync(Organismo, PortalMarcaImagen.KindLogo, nombre, s, Actor, Ct);

        r.Error!.Code.Should().Be(PortalMarcaErrores.ArchivoInvalido);
        r.Error.Message.Should().Contain("PNG, JPEG o WebP");
        _almacen.Archivos.Should().BeEmpty("lo rechazado no llega al almacenamiento");
        (await _admin.ObtenerAsync(Organismo, Ct)).Draft.LogoId.Should().BeNull();
    }

    [Fact]
    public async Task AC2_UnArchivoVacioSeRechaza()
    {
        using var s = new MemoryStream();

        (await _imagenes.SubirAsync(Organismo, PortalMarcaImagen.KindLogo, "logo.png", s, Actor, Ct)).Error!.Message.Should().Contain("vacío");
    }

    [Fact]
    public async Task AC3_UnArchivoCuyaExtensionNoCoincideConSuContenidoSeRechaza()
    {
        using var jpegComoPng = new MemoryStream(ImagenesDePrueba.Jpeg());
        var r = await _imagenes.SubirAsync(Organismo, PortalMarcaImagen.KindLogo, "logo.png", jpegComoPng, Actor, Ct);
        r.Error!.Code.Should().Be(PortalMarcaErrores.ArchivoInvalido);
        r.Error.Message.Should().Contain("no coincide con su extensión").And.Contain("JPEG").And.Contain("PNG");

        using var textoComoPng = new MemoryStream("<svg></svg>"u8.ToArray());
        (await _imagenes.SubirAsync(Organismo, PortalMarcaImagen.KindLogo, "logo.png", textoComoPng, Actor, Ct)).Error!.Message
            .Should().Contain("no es una imagen");
        _almacen.Archivos.Should().BeEmpty();
    }
}
