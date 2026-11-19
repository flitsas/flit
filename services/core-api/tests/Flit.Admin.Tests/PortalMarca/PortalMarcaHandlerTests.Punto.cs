using Flit.Admin.Application.Companies.TransitOffices.PortalMarca;
using Flit.Admin.Domain.Companies.TransitOffices.PortalMarca;
using FluentAssertions;
using Xunit;
using Microsoft.Extensions.Logging.Abstractions;

namespace Flit.Admin.Tests.PortalMarca;

/// <summary>HU #14378 (Feature #14374) — el portal recibe solo lo publicado, sin caché y con marca FLIT ante falla.</summary>
public sealed partial class PortalMarcaHandlerTests
{
    // HU #14378 — entrega de la marca publicada al portal.
    private PortalMarcaPuntoHandler? _puntoHandler;
    private PortalMarcaPuntoHandler _punto => _puntoHandler ??= new PortalMarcaPuntoHandler(_repo, _almacen, NullLogger<PortalMarcaPuntoHandler>.Instance);

    [Fact]
    public async Task AC4_QuitarLaImagenYPublicarHaceQueElPortalUseLaImagenDeFlitParaEsa()
    {
        await GuardarValidoAsync();
        using var logo = new MemoryStream(ImagenesDePrueba.Png());
        var conLogo = (await _imagenes.SubirAsync(Organismo, PortalMarcaImagen.KindLogo, "logo.png", logo, Actor, Ct)).Valor!;
        var publicada = (await _admin.PublicarAsync(Organismo, conLogo.RowVersion, Actor, Ct)).Valor!;
        (await _punto.ObtenerAsync("OT-A", Ct)).LogoId.Should().NotBeNull();

        var sinLogo = await _imagenes.QuitarAsync(Organismo, PortalMarcaImagen.KindLogo, Actor, Ct);
        sinLogo.Draft.LogoId.Should().BeNull();
        sinLogo.Published!.LogoId.Should().NotBeNull("hasta publicar, el portal sigue con la publicada");
        await _admin.PublicarAsync(Organismo, sinLogo.RowVersion, Actor, Ct);

        var marca = await _punto.ObtenerAsync("OT-A", Ct);
        marca.Origen.Should().Be("organismo");
        marca.LogoId.Should().BeNull("sin logo propio el portal usa el de FLIT");
        marca.Nombre.Should().Be("Tránsito de prueba");
        publicada.Published!.LogoId.Should().NotBeNull();
    }

    [Fact]
    public async Task A4_AC1_ConMarcaPublicadaElPortalRecibeNombreColoresEIdsDeImagenSinBorradoresNiDatosInternos()
    {
        await GuardarValidoAsync("Tránsito publicado");
        using var logo = new MemoryStream(ImagenesDePrueba.Png());
        var conLogo = (await _imagenes.SubirAsync(Organismo, PortalMarcaImagen.KindLogo, "logo.png", logo, Actor, Ct)).Valor!;
        var publicada = (await _admin.PublicarAsync(Organismo, conLogo.RowVersion, Actor, Ct)).Valor!;
        // Un cambio posterior en el borrador (nombre y banner) no se ve hasta publicarlo.
        var editado = (await _admin.GuardarAsync(Organismo, new GuardarPortalMarcaRequest("Solo borrador", Colores(principal: "#000000"), publicada.RowVersion), Actor, Ct)).Valor!;
        using var banner = new MemoryStream(ImagenesDePrueba.Png());
        await _imagenes.SubirAsync(Organismo, PortalMarcaImagen.KindBanner, "b.png", banner, Actor, Ct);

        var marca = await _punto.ObtenerAsync("OT-A", Ct);

        marca.Origen.Should().Be("organismo");
        marca.Nombre.Should().Be("Tránsito publicado");
        marca.Colores.Should().Be(new MarcaPuntoColoresDto("#0B3D91", "#0F5132", "#FFFFFF"));
        marca.LogoId.Should().Be(publicada.Published!.LogoId);
        marca.BannerId.Should().BeNull("el banner del borrador no sale");
        marca.Version.Should().Be(1);
        editado.Draft.PlatformName.Should().Be("Solo borrador");
        typeof(MarcaPuntoDto).GetProperties().Select(p => p.Name).Should()
            .BeEquivalentTo("Origen", "Nombre", "Colores", "LogoId", "BannerId", "Version", "Carrusel", "Banners");
    }

    [Theory]
    [InlineData("NO-EXISTE")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("CODIGO-DEMASIADO-LARGO-PARA-SER-UN-CODIGO")]
    public async Task A4_AC2_ConUnCodigoInvalidoOSinMarcaRecibeLaMarcaFlitSinError(string codigo)
    {
        (await _punto.ObtenerAsync(codigo, Ct)).Should().Be(MarcaPuntoDto.Flit);
    }

    [Fact]
    public async Task A4_AC2_UnOrganismoSinMarcaPublicadaRecibeLaMarcaFlit()
    {
        await GuardarValidoAsync();

        (await _punto.ObtenerAsync("OT-A", Ct)).Should().Be(MarcaPuntoDto.Flit, "un borrador no se entrega");
    }

    [Fact]
    public async Task A4_AC2_AnteUnaFallaAlLeerRecibeLaMarcaFlitYNoUnError()
    {
        _repo.FallarAlLeer = true;

        (await _punto.ObtenerAsync("OT-A", Ct)).Should().Be(MarcaPuntoDto.Flit);
    }

    [Fact]
    public async Task A4_AC3_LoRecienPublicadoORetiradoSeVeEnLaSiguienteLecturaSinEsperaNiCache()
    {
        var borrador = await GuardarValidoAsync("Primera");
        (await _punto.ObtenerAsync("OT-A", Ct)).Origen.Should().Be("flit");

        var publicada = (await _admin.PublicarAsync(Organismo, borrador.RowVersion, Actor, Ct)).Valor!;
        (await _punto.ObtenerAsync("OT-A", Ct)).Nombre.Should().Be("Primera");

        var cambio = (await _admin.GuardarAsync(Organismo, new GuardarPortalMarcaRequest("Segunda", Colores(), publicada.RowVersion), Actor, Ct)).Valor!;
        var repu = (await _admin.PublicarAsync(Organismo, cambio.RowVersion, Actor, Ct)).Valor!;
        var marca = await _punto.ObtenerAsync("OT-A", Ct);
        marca.Nombre.Should().Be("Segunda");
        marca.Version.Should().Be(2);

        await _admin.RetirarAsync(Organismo, repu.RowVersion, Actor, Ct);
        (await _punto.ObtenerAsync("OT-A", Ct)).Origen.Should().Be("flit");
    }

    [Fact]
    public async Task A4_LaImagenSoloSeEntregaSiEstaPublicadaYElBorradorNuncaSale()
    {
        await GuardarValidoAsync();
        using var logo = new MemoryStream(ImagenesDePrueba.Png());
        var conLogo = (await _imagenes.SubirAsync(Organismo, PortalMarcaImagen.KindLogo, "logo.png", logo, Actor, Ct)).Valor!;
        var logoId = conLogo.Draft.LogoId!.Value;

        (await _punto.ObtenerImagenAsync(logoId, null, Ct)).Encontrada.Should().BeFalse("es de borrador");

        var publicada = (await _admin.PublicarAsync(Organismo, conLogo.RowVersion, Actor, Ct)).Valor!;
        var visible = await _punto.ObtenerImagenAsync(logoId, null, Ct);
        visible.Encontrada.Should().BeTrue();
        visible.ContentType.Should().Be("image/png");
        visible.Contenido.Should().NotBeNull();
        (await _punto.ObtenerImagenAsync(logoId, visible.Sha256, Ct)).NoModificada.Should().BeTrue();

        // Una versión nueva reemplaza a la publicada al publicar: la URL anterior deja de servirse y la nueva es otra.
        using var otra = new MemoryStream(ImagenesDePrueba.Jpeg());
        var nuevo = (await _imagenes.SubirAsync(Organismo, PortalMarcaImagen.KindLogo, "otro.jpg", otra, Actor, Ct)).Valor!;
        (await _punto.ObtenerImagenAsync(nuevo.Draft.LogoId!.Value, null, Ct)).Encontrada.Should().BeFalse();
        await _admin.PublicarAsync(Organismo, nuevo.RowVersion, Actor, Ct);
        (await _punto.ObtenerImagenAsync(nuevo.Draft.LogoId!.Value, null, Ct)).Encontrada.Should().BeTrue();
        (await _punto.ObtenerImagenAsync(logoId, null, Ct)).Encontrada.Should().BeFalse();
        publicada.Published!.LogoId.Should().Be(logoId);
    }

    [Fact]
    public async Task A4_UnaImagenInexistenteNoSeEncuentra()
    {
        (await _punto.ObtenerImagenAsync(Guid.NewGuid(), null, Ct)).Encontrada.Should().BeFalse();
    }
}
