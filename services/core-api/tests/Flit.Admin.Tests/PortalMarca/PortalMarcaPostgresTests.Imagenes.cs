using Flit.Admin.Application.Companies.TransitOffices.PortalMarca;
using Flit.Admin.Domain.Companies.TransitOffices.PortalMarca;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Flit.Admin.Tests.PortalMarca;

/// <summary>HU #14376 (Feature #14374) — versionado de imágenes contra PostgreSQL real.</summary>
public sealed partial class PortalMarcaPostgresTests
{
    private sealed partial class Escenario
    {
        private PortalMarcaImagenHandler? _imagenes;
        public PortalMarcaImagenHandler Imagenes => _imagenes ??= new PortalMarcaImagenHandler(Repo, Almacen, Admin);
    }

    [Fact]
    public async Task LasImagenesSeVersionanYGuardarElNombreNoPisaLaImagenNiAlReves()
    {
        await using var e = await Escenario.CrearAsync(factory, Ct);

        using var uno = new MemoryStream(ImagenesDePrueba.Png());
        var conLogo = (await e.Imagenes.SubirAsync(e.Organismo, PortalMarcaImagen.KindLogo, "logo.png", uno, e.Actor, Ct)).Valor!;
        conLogo.Draft.LogoId.Should().NotBeNull();

        var conNombre = (await e.Admin.GuardarAsync(e.Organismo, new GuardarPortalMarcaRequest("Con logo", Colores(), conLogo.RowVersion), e.Actor, Ct)).Valor!;
        conNombre.Draft.LogoId.Should().Be(conLogo.Draft.LogoId, "guardar nombre y colores no toca el logo");

        using var dos = new MemoryStream(ImagenesDePrueba.Jpeg());
        var conOtro = (await e.Imagenes.SubirAsync(e.Organismo, PortalMarcaImagen.KindLogo, "otro.jpg", dos, e.Actor, Ct)).Valor!;
        conOtro.Draft.PlatformName.Should().Be("Con logo", "cargar una imagen no toca el nombre");
        conOtro.Draft.LogoId!.Value.Should().NotBe(conLogo.Draft.LogoId!.Value);

        (await e.Repo.ObtenerImagenAsync(conLogo.Draft.LogoId!.Value, Ct))!.Version.Should().Be(1);
        (await e.Repo.ObtenerImagenAsync(conOtro.Draft.LogoId!.Value, Ct))!.Version.Should().Be(2);

        using var banner = new MemoryStream(ImagenesDePrueba.Webp());
        var conBanner = (await e.Imagenes.SubirAsync(e.Organismo, PortalMarcaImagen.KindBanner, "b.webp", banner, e.Actor, Ct)).Valor!;
        (await e.Repo.ObtenerImagenAsync(conBanner.Draft.BannerId!.Value, Ct))!.Version.Should().Be(1, "el banner lleva su propia numeración");

        var sinLogo = await e.Imagenes.QuitarAsync(e.Organismo, PortalMarcaImagen.KindLogo, e.Actor, Ct);
        sinLogo.Draft.LogoId.Should().BeNull();
        sinLogo.Draft.BannerId.Should().NotBeNull();
        (await e.Repo.ObtenerImagenAsync(conOtro.Draft.LogoId.Value, Ct)).Should().NotBeNull("quitar no borra la versión");
    }
}
