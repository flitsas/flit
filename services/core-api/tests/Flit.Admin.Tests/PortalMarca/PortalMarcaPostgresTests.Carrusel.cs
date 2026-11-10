using Flit.Admin.Application.Companies.TransitOffices.PortalMarca;
using Flit.Admin.Domain.Companies.TransitOffices.PortalMarca;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Flit.Admin.Tests.PortalMarca;

/// <summary>Mejora de la Épica #14297 — carrusel de la marca contra PostgreSQL real (CHECK de kind, jsonb, bloqueo de fila, publicado).</summary>
public sealed partial class PortalMarcaPostgresTests
{
    private static async Task<PortalMarcaDto> AgregarCarruselAsync(Escenario e, string alt, string? enlace = null, string nombre = "c.png")
    {
        using var s = new MemoryStream(ImagenesDePrueba.Png());
        var r = await e.Imagenes.AgregarCarruselAsync(e.Organismo, nombre, s, alt, enlace, e.Actor, Ct);
        r.Error.Should().BeNull();
        return r.Valor!;
    }

    [Fact]
    public async Task Carrusel_DDL_ElCheckDeKindAdmiteLogoBannerYCarouselYNadaMas()
    {
        await using var e = await Escenario.CrearAsync(factory, Ct);
        var def = (await e.Db.Database.SqlQuery<string>($"""
            SELECT pg_get_constraintdef(oid) AS "Value" FROM pg_constraint
             WHERE conname = 'ck_transit_office_portal_brand_images_kind'
            """).ToListAsync(Ct)).Single();

        def.Should().Contain("logo").And.Contain("banner").And.Contain("carousel");
        var act = () => e.Db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO admin.transit_office_portal_brand_images (id, transit_office_id, kind, version, content_type, storage_path, storage_sha256, size_bytes)
            VALUES ({Guid.NewGuid()}, {e.Organismo}, 'otro', 1, 'image/png', 'x', {new string('a', 64)}, 1)
            """, Ct);
        await act.Should().ThrowAsync<Exception>("un tipo desconocido sigue rechazado");
    }

    [Fact]
    public async Task Carrusel_AgregarOrdenarEditarYQuitarPersistenSinPisarNombreNiColores()
    {
        await using var e = await Escenario.CrearAsync(factory, Ct);
        var inicial = (await e.Admin.GuardarAsync(e.Organismo, new GuardarPortalMarcaRequest("Con carrusel", Colores(), 0), e.Actor, Ct)).Valor!;

        // Sin fila previa también funciona (se crea la marca con la primera imagen), y aquí ya existe.
        await AgregarCarruselAsync(e, "Primera", "https://example.test/a");
        var dos = await AgregarCarruselAsync(e, "Segunda");
        dos.Draft.PlatformName.Should().Be("Con carrusel");
        dos.Carousel.Select(i => i.Alt).Should().Equal("Primera", "Segunda");
        (await e.Repo.ObtenerImagenAsync(dos.Carousel[1].ImageId, Ct))!.Kind.Should().Be("carousel");
        (await e.Repo.ObtenerImagenAsync(dos.Carousel[0].ImageId, Ct))!.Version.Should().Be(1);
        (await e.Repo.ObtenerImagenAsync(dos.Carousel[1].ImageId, Ct))!.Version.Should().Be(2, "numeración propia del tipo");
        dos.RowVersion.Should().BeGreaterThan(inicial.RowVersion);

        var (a, b) = (dos.Carousel[0].ImageId, dos.Carousel[1].ImageId);
        var orden = (await e.Imagenes.OrdenarCarruselAsync(e.Organismo, [b, a], e.Actor, Ct)).Valor!;
        orden.Carousel.Select(i => i.Alt).Should().Equal("Segunda", "Primera");
        (await e.Imagenes.OrdenarCarruselAsync(e.Organismo, [a], e.Actor, Ct)).Error!.Code.Should().Be(PortalMarcaErrores.Datos);

        var editado = (await e.Imagenes.ActualizarCarruselAsync(e.Organismo, a, "Editada", null, e.Actor, Ct)).Valor!;
        editado.Carousel[1].Alt.Should().Be("Editada");
        editado.Carousel[1].LinkUrl.Should().BeNull();

        var quitado = (await e.Imagenes.QuitarCarruselAsync(e.Organismo, b, e.Actor, Ct)).Valor!;
        quitado.Carousel.Select(i => i.Alt).Should().Equal("Editada");
        (await e.Repo.ObtenerImagenAsync(b, Ct)).Should().NotBeNull("el binario y su fila se conservan");
        quitado.Draft.Colors.Should().Be(new PortalMarcaColoresDto("#0B3D91", "#0F5132", "#FFFFFF"), "el carrusel no toca los colores");
    }

    [Fact]
    public async Task Carrusel_ElLimiteDeSeisSeHaceCumplirEnLaBase_Y_UnaSegundaTandaNoLoSupera()
    {
        await using var e = await Escenario.CrearAsync(factory, Ct);
        for (var i = 1; i <= 6; i++)
            await AgregarCarruselAsync(e, $"Imagen {i}");

        using var s = new MemoryStream(ImagenesDePrueba.Png());
        var (imagen, marca) = await e.Repo.AgregarCarruselAsync(
            e.Organismo, await e.Almacen.GuardarAsync(e.Organismo, "image/png", s, Ct), "Séptima", null, PortalMarcaSnapshot.CarruselMaximo, e.Actor, Ct);

        imagen.Should().BeNull();
        marca.Should().BeNull();
        (await e.Repo.ObtenerAsync(e.Organismo, Ct)).Draft.Carousel.Should().HaveCount(6);
        (await e.Db.Database.SqlQuery<long>($"""
            SELECT count(*) AS "Value" FROM admin.transit_office_portal_brand_images WHERE transit_office_id = {e.Organismo} AND kind = 'carousel'
            """).ToListAsync(Ct)).Single().Should().Be(6, "la imagen rechazada no se registra");
    }

    [Fact]
    public async Task Carrusel_AgregarEnParaleloNoPierdeNingunaImagen()
    {
        await using var e = await Escenario.CrearAsync(factory, Ct);
        await AgregarCarruselAsync(e, "Base");
        var tareas = Enumerable.Range(1, 4).Select(async n =>
        {
            // Cada tarea usa su propio contexto: así se prueba el bloqueo de fila y no la serialización de un solo DbContext.
            using var alcance = factory.Services.CreateScope();
            await using var otro = new EscenarioSecundario(alcance);
            using var s = new MemoryStream(ImagenesDePrueba.Png());
            var r = await otro.Imagenes.AgregarCarruselAsync(e.Organismo, "p.png", s, $"Paralela {n}", null, e.Actor, Ct);
            r.Error.Should().BeNull();
        }).ToArray();

        await Task.WhenAll(tareas);

        var final = await e.Repo.ObtenerAsync(e.Organismo, Ct);
        final.Draft.Carousel.Should().HaveCount(5);
        final.Draft.Carousel.Select(i => i.ImageId).Distinct().Should().HaveCount(5);
    }

    [Fact]
    public async Task Carrusel_PublicarLoCopiaRetirarLoQuitaYLaImagenPublicadaSeReconoce()
    {
        await using var e = await Escenario.CrearAsync(factory, Ct);
        await e.Admin.GuardarAsync(e.Organismo, new GuardarPortalMarcaRequest("Portal", Colores(), 0), e.Actor, Ct);
        var publicada1 = await AgregarCarruselAsync(e, "Publicada", "https://example.test/p");
        var publicada = (await e.Admin.PublicarAsync(e.Organismo, publicada1.RowVersion, e.Actor, Ct)).Valor!;
        var enPublicado = publicada.Carousel[0].ImageId;
        var soloBorrador = (await AgregarCarruselAsync(e, "Borrador")).Carousel[1].ImageId;

        publicada.Published!.Carousel.Should().ContainSingle().Which.Should().Be(new PortalMarcaCarruselSnapshotItemDto(enPublicado, "Publicada", "https://example.test/p"));
        (await e.Repo.ImagenEstaPublicadaAsync(enPublicado, Ct)).Should().BeTrue();
        (await e.Repo.ImagenEstaPublicadaAsync(soloBorrador, Ct)).Should().BeFalse();
        (await e.Admin.ObtenerAsync(e.Organismo, Ct)).HasUnpublishedChanges.Should().BeTrue();

        var punto = new PortalMarcaPuntoHandler(e.Repo, e.Almacen, Microsoft.Extensions.Logging.Abstractions.NullLogger<PortalMarcaPuntoHandler>.Instance);
        (await punto.ObtenerAsync(e.Codigo, Ct)).Carrusel.Should().ContainSingle().Which.ImageId.Should().Be(enPublicado);

        var actual = await e.Admin.ObtenerAsync(e.Organismo, Ct);
        await e.Admin.RetirarAsync(e.Organismo, actual.RowVersion, e.Actor, Ct);
        (await e.Repo.ImagenEstaPublicadaAsync(enPublicado, Ct)).Should().BeFalse();
        (await e.Repo.ObtenerAsync(e.Organismo, Ct)).Draft.Carousel.Should().HaveCount(2);
    }

    [Fact]
    public async Task Carrusel_UnDocumentoViejoSinLaClaveSeLeeVacioYNoHayCambiosSinPublicar()
    {
        await using var e = await Escenario.CrearAsync(factory, Ct);
        var g = (await e.Admin.GuardarAsync(e.Organismo, new GuardarPortalMarcaRequest("Viejo", Colores(), 0), e.Actor, Ct)).Valor!;
        (await e.Admin.PublicarAsync(e.Organismo, g.RowVersion, e.Actor, Ct)).Valor!.HasUnpublishedChanges.Should().BeFalse();

        var leido = await e.Repo.ObtenerAsync(e.Organismo, Ct);

        leido.Draft.Carousel.Should().BeEmpty();
        leido.Published!.Carousel.Should().BeEmpty();
        leido.HasUnpublishedChanges.Should().BeFalse();
        await e.Admin.ObtenerAsync(e.Organismo, Ct);
    }

    /// <summary>Otro ámbito de servicios (otro DbContext) que opera sobre un organismo ya sembrado; no lo crea ni lo borra.</summary>
    private sealed class EscenarioSecundario(Microsoft.Extensions.DependencyInjection.IServiceScope scope) : IAsyncDisposable
    {
        public PortalMarcaImagenHandler Imagenes { get; } = new(
            scope.ServiceProvider.GetRequiredService<IPortalMarcaRepository>(),
            new AlmacenImagenEnMemoria(),
            new PortalMarcaAdminHandler(
                scope.ServiceProvider.GetRequiredService<IPortalMarcaRepository>(),
                scope.ServiceProvider.GetRequiredService<Flit.Admin.Application.Auditing.IAdminAuditWriter>()));

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
