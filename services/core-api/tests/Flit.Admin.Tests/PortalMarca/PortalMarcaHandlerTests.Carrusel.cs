using Flit.Admin.Application.Companies.TransitOffices.PortalMarca;
using Flit.Admin.Domain.Companies.TransitOffices.PortalMarca;
using FluentAssertions;
using Xunit;

namespace Flit.Admin.Tests.PortalMarca;

/// <summary>Mejora de la Épica #14297 — carrusel de imágenes de la columna lateral del portal del organismo (borrador → publicar).</summary>
public sealed partial class PortalMarcaHandlerTests
{
    private async Task<PortalMarcaDto> AgregarAsync(string alt, string? enlace = null, string nombre = "a.png", byte[]? bytes = null)
    {
        using var s = new MemoryStream(bytes ?? ImagenesDePrueba.Png());
        var r = await _imagenes.AgregarCarruselAsync(Organismo, nombre, s, alt, enlace, Actor, Ct);
        r.Error.Should().BeNull();
        return r.Valor!;
    }

    [Fact]
    public async Task Carrusel_LasImagenesSeAgreganAlFinalConSuAltYEnlaceYLaUrlAdministrativa()
    {
        var uno = await AgregarAsync("  Primera  ", "https://tramites.example.test/a");
        var dos = await AgregarAsync("Segunda");

        dos.Carousel.Select(i => i.Alt).Should().Equal("Primera", "Segunda");
        dos.Carousel[0].LinkUrl.Should().Be("https://tramites.example.test/a");
        dos.Carousel[1].LinkUrl.Should().BeNull();
        dos.Carousel[0].ImageUrl.Should().Be($"{PortalMarcaAdminHandler.RutaImagenes}{uno.Carousel[0].ImageId}");
        dos.Draft.Carousel.Select(i => i.ImageId).Should().Equal(dos.Carousel.Select(i => i.ImageId));
        (await _imagenes.BuscarAsync(dos.Carousel[1].ImageId, Ct))!.Kind.Should().Be(PortalMarcaImagen.KindCarousel);
        _auditoria.Entradas.Should().Contain(a => a.EntityName == "portal_marca_carrusel");
    }

    [Fact]
    public async Task Carrusel_ConSeisImagenesLaSeptimaDa409CarruselLlenoYNoSeGuardaElArchivo()
    {
        for (var i = 1; i <= 6; i++)
            await AgregarAsync($"Imagen {i}");
        var archivosAntes = _almacen.Archivos.Count;

        using var s = new MemoryStream(ImagenesDePrueba.Png());
        var r = await _imagenes.AgregarCarruselAsync(Organismo, "x.png", s, "Séptima", null, Actor, Ct);

        r.Error!.Status.Should().Be(409);
        r.Error.Code.Should().Be(PortalMarcaErrores.CarruselLleno);
        _almacen.Archivos.Should().HaveCount(archivosAntes);
        (await _admin.ObtenerAsync(Organismo, Ct)).Carousel.Should().HaveCount(6);
    }

    [Theory]
    [InlineData("logo.gif", 64, "PNG, JPEG o WebP")]
    [InlineData("logo", 64, "PNG, JPEG o WebP")]
    [InlineData("grande.png", 512 * 1024, "512 KB")]
    public async Task Carrusel_UnArchivoInvalidoDa400ArchivoInvalidoYNoSeGuarda(string nombre, int relleno, string fragmento)
    {
        using var s = new MemoryStream(ImagenesDePrueba.Png(relleno));

        var r = await _imagenes.AgregarCarruselAsync(Organismo, nombre, s, "Alt", null, Actor, Ct);

        r.Error!.Status.Should().Be(400);
        r.Error.Code.Should().Be(PortalMarcaErrores.ArchivoInvalido);
        r.Error.Message.Should().Contain(fragmento);
        _almacen.Archivos.Should().BeEmpty();
        (await _admin.ObtenerAsync(Organismo, Ct)).Carousel.Should().BeEmpty();
    }

    [Fact]
    public async Task Carrusel_ElContenidoQueNoCoincideConLaExtensionSeRechaza_Y512KBExactosCaben()
    {
        using var mentira = new MemoryStream(ImagenesDePrueba.Jpeg());
        var r = await _imagenes.AgregarCarruselAsync(Organismo, "x.png", mentira, "Alt", null, Actor, Ct);
        r.Error!.Code.Should().Be(PortalMarcaErrores.ArchivoInvalido);
        r.Error.Message.Should().Contain("no coincide con su extensión");

        using var limite = new MemoryStream(ImagenesDePrueba.Png(512 * 1024 - 8));
        (await _imagenes.AgregarCarruselAsync(Organismo, "x.png", limite, "Alt", null, Actor, Ct)).Error.Should().BeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("con\u0007control")]
    [InlineData("salto\nde linea")]
    public async Task Carrusel_UnAltVacioOConCaracteresDeControlDa400DatosInvalidos(string alt)
    {
        using var s = new MemoryStream(ImagenesDePrueba.Png());

        var r = await _imagenes.AgregarCarruselAsync(Organismo, "a.png", s, alt, null, Actor, Ct);

        r.Error!.Code.Should().Be(PortalMarcaErrores.Datos);
        _almacen.Archivos.Should().BeEmpty();
    }

    [Fact]
    public async Task Carrusel_ElAltAdmiteHasta120CaracteresYNoMas()
    {
        using var ok = new MemoryStream(ImagenesDePrueba.Png());
        (await _imagenes.AgregarCarruselAsync(Organismo, "a.png", ok, new string('a', 120), null, Actor, Ct)).Error.Should().BeNull();
        using var largo = new MemoryStream(ImagenesDePrueba.Png());
        (await _imagenes.AgregarCarruselAsync(Organismo, "a.png", largo, new string('a', 121), null, Actor, Ct)).Error!.Code.Should().Be(PortalMarcaErrores.Datos);
    }

    [Theory]
    [InlineData("ftp://example.test/a")]
    [InlineData("javascript:alert(1)")]
    [InlineData("//example.test/a")]
    [InlineData("/ruta/relativa")]
    [InlineData("https://")]
    [InlineData("https://example.test/con espacio")]
    public async Task Carrusel_UnEnlaceQueNoEsHttpOHttpsValidoDa400(string enlace)
    {
        using var s = new MemoryStream(ImagenesDePrueba.Png());

        var r = await _imagenes.AgregarCarruselAsync(Organismo, "a.png", s, "Alt", enlace, Actor, Ct);

        r.Error!.Code.Should().Be(PortalMarcaErrores.Datos);
    }

    [Fact]
    public async Task Carrusel_UnEnlaceDeMasDe500CaracteresDa400_YUnoVacioSeTomaComoSinEnlace()
    {
        using var s = new MemoryStream(ImagenesDePrueba.Png());
        var largo = "https://example.test/" + new string('a', 480);
        (await _imagenes.AgregarCarruselAsync(Organismo, "a.png", s, "Alt", largo, Actor, Ct)).Error!.Code.Should().Be(PortalMarcaErrores.Datos);

        var sinEnlace = await AgregarAsync("Alt", "   ");
        sinEnlace.Carousel.Single().LinkUrl.Should().BeNull();
    }

    [Fact]
    public async Task Carrusel_EditarCambiaAltYEnlace_Y404SiLaImagenNoEstaEnElCarrusel()
    {
        var marca = await AgregarAsync("Vieja", "https://example.test/a");
        var id = marca.Carousel[0].ImageId;

        var editado = (await _imagenes.ActualizarCarruselAsync(Organismo, id, "Nueva", null, Actor, Ct)).Valor!;
        var invalido = await _imagenes.ActualizarCarruselAsync(Organismo, id, "", null, Actor, Ct);
        var ajeno = await _imagenes.ActualizarCarruselAsync(Organismo, Guid.NewGuid(), "Otra", null, Actor, Ct);

        editado.Carousel.Single().Alt.Should().Be("Nueva");
        editado.Carousel.Single().LinkUrl.Should().BeNull();
        invalido.Error!.Code.Should().Be(PortalMarcaErrores.Datos);
        ajeno.Error!.Status.Should().Be(404);
        ajeno.Error.Code.Should().Be(PortalMarcaErrores.NoEncontrada);
    }

    [Fact]
    public async Task Carrusel_ElOrdenAdmiteSoloUnaPermutacionExactaDeLasActuales()
    {
        await AgregarAsync("A");
        await AgregarAsync("B");
        var tres = await AgregarAsync("C");
        var (a, b, c) = (tres.Carousel[0].ImageId, tres.Carousel[1].ImageId, tres.Carousel[2].ImageId);

        var ok = (await _imagenes.OrdenarCarruselAsync(Organismo, [c, a, b], Actor, Ct)).Valor!;
        ok.Carousel.Select(i => i.Alt).Should().Equal("C", "A", "B");

        foreach (var malo in new IReadOnlyList<Guid>?[] { [a, b], [a, b, c, Guid.NewGuid()], [a, a, b], [a, b, Guid.NewGuid()], null })
        {
            var r = await _imagenes.OrdenarCarruselAsync(Organismo, malo, Actor, Ct);
            r.Error!.Code.Should().Be(PortalMarcaErrores.Datos);
        }

        (await _admin.ObtenerAsync(Organismo, Ct)).Carousel.Select(i => i.Alt).Should().Equal("C", "A", "B");
    }

    [Fact]
    public async Task Carrusel_QuitarLoSacaDelBorradorPeroConservaLaVersionDelBinario()
    {
        await AgregarAsync("A");
        var dos = await AgregarAsync("B");
        var quitada = dos.Carousel[0].ImageId;

        var r = (await _imagenes.QuitarCarruselAsync(Organismo, quitada, Actor, Ct)).Valor!;

        r.Carousel.Select(i => i.Alt).Should().Equal("B");
        (await _imagenes.BuscarAsync(quitada, Ct)).Should().NotBeNull();
        (await _imagenes.QuitarCarruselAsync(Organismo, quitada, Actor, Ct)).Error!.Status.Should().Be(404);
    }

    [Fact]
    public async Task Carrusel_ElAmbitoWebNoLoAdmite()
    {
        using var s = new MemoryStream(ImagenesDePrueba.Png());

        var agregar = await _imagenes.AgregarCarruselAsync(PortalMarcaAmbito.Web, "a.png", s, "Alt", null, Actor, Ct);
        var orden = await _imagenes.OrdenarCarruselAsync(PortalMarcaAmbito.Web, [], Actor, Ct);

        agregar.Error!.Code.Should().Be(PortalMarcaErrores.Datos);
        orden.Error!.Code.Should().Be(PortalMarcaErrores.Datos);
        (await _imagenes.QuitarCarruselAsync(PortalMarcaAmbito.Web, Guid.NewGuid(), Actor, Ct)).Error!.Code.Should().Be(PortalMarcaErrores.Datos);
        (await _imagenes.ActualizarCarruselAsync(PortalMarcaAmbito.Web, Guid.NewGuid(), "x", null, Actor, Ct)).Error!.Code.Should().Be(PortalMarcaErrores.Datos);
    }

    [Fact]
    public async Task Carrusel_PublicarLoCopiaYRetirarLoQuitaDelPortalSinTocarElBorrador()
    {
        await GuardarValidoAsync();
        var borrador = await AgregarAsync("Primera", "https://example.test/a");
        var publicada = (await _admin.PublicarAsync(Organismo, borrador.RowVersion, Actor, Ct)).Valor!;

        publicada.Published!.Carousel.Should().BeEquivalentTo(publicada.Draft.Carousel);
        publicada.HasUnpublishedChanges.Should().BeFalse();
        var marca = await _punto.ObtenerAsync("OT-A", Ct);
        marca.Carrusel.Should().ContainSingle().Which.Should().Be(new PortalMarcaCarruselItem(borrador.Carousel[0].ImageId, "Primera", "https://example.test/a"));

        var retirada = (await _admin.RetirarAsync(Organismo, publicada.RowVersion, Actor, Ct)).Valor!;
        retirada.Published.Should().BeNull();
        retirada.Carousel.Should().HaveCount(1, "el borrador conserva el carrusel");
        (await _punto.ObtenerAsync("OT-A", Ct)).Should().Be(MarcaPuntoDto.Flit);
    }

    [Fact]
    public async Task Carrusel_UnCarruselVacioEsValidoParaPublicar_Y_ElPortalLoRecibeVacio()
    {
        var borrador = await GuardarValidoAsync();

        (await _admin.PublicarAsync(Organismo, borrador.RowVersion, Actor, Ct)).Error.Should().BeNull();

        (await _punto.ObtenerAsync("OT-A", Ct)).Carrusel.Should().BeEmpty();
    }

    [Fact]
    public async Task Carrusel_ElPortalSoloVeLoPublicadoNoLosCambiosDelBorrador()
    {
        await GuardarValidoAsync();
        var uno = await AgregarAsync("Publicada");
        var publicada = (await _admin.PublicarAsync(Organismo, uno.RowVersion, Actor, Ct)).Valor!;
        await AgregarAsync("Solo borrador");

        var marca = await _punto.ObtenerAsync("OT-A", Ct);

        marca.Carrusel.Select(i => i.Alt).Should().Equal("Publicada");
        publicada.Published!.Carousel.Should().HaveCount(1);
    }

    [Fact]
    public async Task Carrusel_UnaImagenDeCarruselPublicadaSeSirveYUnaSoloDeBorradorONoPublicadaNo()
    {
        await GuardarValidoAsync();
        var uno = await AgregarAsync("Publicada");
        await _admin.PublicarAsync(Organismo, uno.RowVersion, Actor, Ct);
        var dos = await AgregarAsync("Borrador");
        var publicadaId = uno.Carousel[0].ImageId;
        var borradorId = dos.Carousel[1].ImageId;

        (await _punto.ObtenerImagenAsync(publicadaId, null, Ct)).Encontrada.Should().BeTrue();
        (await _punto.ObtenerImagenAsync(borradorId, null, Ct)).Encontrada.Should().BeFalse();

        var tras = await _admin.ObtenerAsync(Organismo, Ct);
        await _admin.RetirarAsync(Organismo, tras.RowVersion, Actor, Ct);
        (await _punto.ObtenerImagenAsync(publicadaId, null, Ct)).Encontrada.Should().BeFalse("retirada deja de servirse");
    }

    [Fact]
    public async Task Carrusel_LaComparacionDeCambiosSinPublicarEsEstructuralNoPorReferencia()
    {
        await GuardarValidoAsync();
        var borrador = await AgregarAsync("A", "https://example.test/a");
        var publicada = (await _admin.PublicarAsync(Organismo, borrador.RowVersion, Actor, Ct)).Valor!;
        var id = borrador.Carousel[0].ImageId;
        publicada.HasUnpublishedChanges.Should().BeFalse();

        var editado = (await _imagenes.ActualizarCarruselAsync(Organismo, id, "Otro", "https://example.test/a", Actor, Ct)).Valor!;
        editado.HasUnpublishedChanges.Should().BeTrue();

        // Volver al mismo contenido con una lista distinta (otra referencia) deja de contar como cambio.
        var devuelto = (await _imagenes.ActualizarCarruselAsync(Organismo, id, "A", "https://example.test/a", Actor, Ct)).Valor!;
        devuelto.HasUnpublishedChanges.Should().BeFalse();

        await AgregarAsync("B");
        (await _admin.ObtenerAsync(Organismo, Ct)).HasUnpublishedChanges.Should().BeTrue();
    }

    [Fact]
    public void Carrusel_ElSnapshotComparaElCarruselPorElementosYUnoAusenteEsVacio()
    {
        var id = Guid.NewGuid();
        var a = PortalMarcaSnapshot.Empty with { Carousel = new List<PortalMarcaCarruselItem> { new(id, "A", null) } };
        var b = PortalMarcaSnapshot.Empty with { Carousel = new[] { new PortalMarcaCarruselItem(id, "A", null) } };
        var c = PortalMarcaSnapshot.Empty with { Carousel = new[] { new PortalMarcaCarruselItem(id, "B", null) } };

        (a == b).Should().BeTrue();
        a.GetHashCode().Should().Be(b.GetHashCode());
        (a == c).Should().BeFalse();
        (PortalMarcaSnapshot.Empty with { Carousel = [] }).Should().Be(PortalMarcaSnapshot.Empty);
        (PortalMarcaSnapshot.Empty with { Carousel = null! }).Carousel.Should().BeEmpty();
    }
}
