using Flit.Admin.Application.Companies.TransitOffices.PortalMarca;
using Flit.Admin.Domain.Companies.TransitOffices.PortalMarca;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Xunit;
using Microsoft.Extensions.Logging.Abstractions;

namespace Flit.Admin.Tests.PortalMarca;

/// <summary>HU #14378 (Feature #14374) — entrega de la marca al portal contra PostgreSQL real.</summary>
public sealed partial class PortalMarcaPostgresTests
{
    private sealed partial class Escenario
    {
        private PortalMarcaPuntoHandler? _punto;
        public PortalMarcaPuntoHandler Punto => _punto ??= new PortalMarcaPuntoHandler(Repo, Almacen, NullLogger<PortalMarcaPuntoHandler>.Instance);
    }

    [Fact]
    public async Task ElPortalVeLoPublicadoYLoRetiradoEnLaSiguienteLecturaYLaImagenDeBorradorNoSale()
    {
        await using var e = await Escenario.CrearAsync(factory, Ct);
        using var logo = new MemoryStream(ImagenesDePrueba.Png());
        await e.Imagenes.SubirAsync(e.Organismo, PortalMarcaImagen.KindLogo, "logo.png", logo, e.Actor, Ct);
        var actual = await e.Admin.ObtenerAsync(e.Organismo, Ct);
        var guardado = (await e.Admin.GuardarAsync(e.Organismo, new GuardarPortalMarcaRequest("Portal del punto", Colores(), actual.RowVersion), e.Actor, Ct)).Valor!;

        (await e.Punto.ObtenerAsync(e.Codigo, Ct)).Origen.Should().Be("flit", "el borrador no se ve");
        (await e.Punto.ObtenerImagenAsync(guardado.Draft.LogoId!.Value, null, Ct)).Encontrada.Should().BeFalse();

        var publicada = (await e.Admin.PublicarAsync(e.Organismo, guardado.RowVersion, e.Actor, Ct)).Valor!;
        var marca = await e.Punto.ObtenerAsync(e.Codigo.ToLowerInvariant(), Ct);
        marca.Origen.Should().Be("organismo");
        marca.Nombre.Should().Be("Portal del punto");
        marca.Colores.Should().Be(new MarcaPuntoColoresDto("#0B3D91", "#0F5132", "#FFFFFF"));
        marca.LogoId.Should().Be(guardado.Draft.LogoId);
        marca.Version.Should().Be(1);
        var imagen = await e.Punto.ObtenerImagenAsync(guardado.Draft.LogoId!.Value, null, Ct);
        imagen.Encontrada.Should().BeTrue();
        imagen.ContentType.Should().Be("image/png");

        await e.Admin.RetirarAsync(e.Organismo, publicada.RowVersion, e.Actor, Ct);
        (await e.Punto.ObtenerAsync(e.Codigo, Ct)).Should().Be(MarcaPuntoDto.Flit);
        (await e.Punto.ObtenerImagenAsync(guardado.Draft.LogoId!.Value, null, Ct)).Encontrada.Should().BeFalse("retirada deja de servirse");
    }

    [Fact]
    public async Task UnOrganismoInactivoOUnCodigoDesconocidoRecibenLaMarcaFlit()
    {
        await using var e = await Escenario.CrearAsync(factory, Ct);
        var g = (await e.Admin.GuardarAsync(e.Organismo, new GuardarPortalMarcaRequest("Portal", Colores(), 0), e.Actor, Ct)).Valor!;
        await e.Admin.PublicarAsync(e.Organismo, g.RowVersion, e.Actor, Ct);
        (await e.Punto.ObtenerAsync(e.Codigo, Ct)).Origen.Should().Be("organismo");

        await e.Db.Database.ExecuteSqlInterpolatedAsync($"UPDATE catalogs.transit_offices SET is_active = false WHERE id = {e.Organismo}", Ct);

        (await e.Punto.ObtenerAsync(e.Codigo, Ct)).Should().Be(MarcaPuntoDto.Flit);
        (await e.Punto.ObtenerAsync("NO-EXISTE-XYZ", Ct)).Should().Be(MarcaPuntoDto.Flit);
    }
}
