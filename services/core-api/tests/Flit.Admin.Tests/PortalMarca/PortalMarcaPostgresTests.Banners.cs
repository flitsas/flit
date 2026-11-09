using Flit.Admin.Application.Companies.TransitOffices.PortalMarca;
using Flit.Admin.Domain.Companies.TransitOffices.PortalMarca;
using Flit.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Flit.Admin.Tests.PortalMarca;

/// <summary>Mejora de la Épica #14297 — lista de banners de la marca contra PostgreSQL real (jsonb, bloqueo de fila, publicado, migración del banner único).</summary>
public sealed partial class PortalMarcaPostgresTests
{
    private static async Task<PortalMarcaDto> AgregarBannerAsync(Escenario e, string alt, string? enlace = null)
    {
        using var s = new MemoryStream(ImagenesDePrueba.Png());
        var r = await e.Imagenes.AgregarBannerAsync(e.Organismo, "b.png", s, alt, enlace, e.Actor, Ct);
        r.Error.Should().BeNull();
        return r.Valor!;
    }

    [Fact]
    public async Task Banners_AgregarOrdenarEditarYQuitarPersistenSinPisarNombreNiColores_Y_BannerIdEsElPrimero()
    {
        await using var e = await Escenario.CrearAsync(factory, Ct);
        await e.Admin.GuardarAsync(e.Organismo, new GuardarPortalMarcaRequest("Con banners", Colores(), 0), e.Actor, Ct);

        await AgregarBannerAsync(e, "Primero", "https://example.test/a");
        var dos = await AgregarBannerAsync(e, "Segundo");
        dos.Draft.PlatformName.Should().Be("Con banners");
        dos.Banners.Select(i => i.Alt).Should().Equal("Primero", "Segundo");
        (await e.Repo.ObtenerImagenAsync(dos.Banners[1].ImageId, Ct))!.Kind.Should().Be("banner");
        (await e.Repo.ObtenerImagenAsync(dos.Banners[1].ImageId, Ct))!.Version.Should().Be(2, "numeración propia del tipo");
        dos.Draft.BannerId.Should().Be(dos.Banners[0].ImageId);

        var (a, b) = (dos.Banners[0].ImageId, dos.Banners[1].ImageId);
        var orden = (await e.Imagenes.OrdenarBannersAsync(e.Organismo, [b, a], e.Actor, Ct)).Valor!;
        orden.Banners.Select(i => i.Alt).Should().Equal("Segundo", "Primero");
        orden.Draft.BannerId.Should().Be(b);
        (await e.Imagenes.OrdenarBannersAsync(e.Organismo, [a], e.Actor, Ct)).Error!.Code.Should().Be(PortalMarcaErrores.Datos);

        var editado = (await e.Imagenes.ActualizarBannerAsync(e.Organismo, a, "Editado", null, e.Actor, Ct)).Valor!;
        editado.Banners[1].Alt.Should().Be("Editado");
        editado.Banners[1].LinkUrl.Should().BeNull();

        var quitado = (await e.Imagenes.QuitarBannerAsync(e.Organismo, b, e.Actor, Ct)).Valor!;
        quitado.Banners.Select(i => i.Alt).Should().Equal("Editado");
        quitado.Draft.BannerId.Should().Be(a);
        (await e.Repo.ObtenerImagenAsync(b, Ct)).Should().NotBeNull("el binario y su fila se conservan");
        quitado.Draft.Colors.Should().Be(new PortalMarcaColoresDto("#0B3D91", "#0F5132", "#FFFFFF"));
    }

    [Fact]
    public async Task Banners_ElLimiteDeSeisSeHaceCumplirEnLaBase_Y_EnParaleloNoSePierdeNingunoNiSeSupera()
    {
        await using var e = await Escenario.CrearAsync(factory, Ct);
        await AgregarBannerAsync(e, "Base");
        var tareas = Enumerable.Range(1, 8).Select(async n =>
        {
            using var alcance = factory.Services.CreateScope();
            await using var otro = new EscenarioSecundario(alcance);
            using var s = new MemoryStream(ImagenesDePrueba.Png());
            return await otro.Imagenes.AgregarBannerAsync(e.Organismo, "p.png", s, $"Paralelo {n}", null, e.Actor, Ct);
        }).ToArray();

        var resultados = await Task.WhenAll(tareas);

        var final = await e.Repo.ObtenerAsync(e.Organismo, Ct);
        final.Draft.Banners.Should().HaveCount(6);
        final.Draft.Banners.Select(i => i.ImageId).Distinct().Should().HaveCount(6);
        resultados.Count(r => r.Error is null).Should().Be(5);
        resultados.Where(r => r.Error is not null).Should().OnlyContain(r => r.Error!.Code == PortalMarcaErrores.BannersLleno);
        (await e.Db.Database.SqlQuery<long>($"""
            SELECT count(*) AS "Value" FROM admin.transit_office_portal_brand_images WHERE transit_office_id = {e.Organismo} AND kind = 'banner'
            """).ToListAsync(Ct)).Single().Should().Be(6, "las imágenes rechazadas no se registran");
    }

    [Fact]
    public async Task Banners_PublicarLosCopia_CambiarlosSubeLaVersion_RetirarLosQuita_Y_LaImagenPublicadaSeReconoce()
    {
        await using var e = await Escenario.CrearAsync(factory, Ct);
        await e.Admin.GuardarAsync(e.Organismo, new GuardarPortalMarcaRequest("Portal", Colores(), 0), e.Actor, Ct);
        var con1 = await AgregarBannerAsync(e, "Publicado", "https://example.test/p");
        var v1 = (await e.Admin.PublicarAsync(e.Organismo, con1.RowVersion, e.Actor, Ct)).Valor!;
        var enPublicado = v1.Banners[0].ImageId;
        var con2 = await AgregarBannerAsync(e, "Borrador");
        var soloBorrador = con2.Banners[1].ImageId;

        v1.Published!.Banners.Should().ContainSingle().Which.Should().Be(new PortalMarcaCarruselSnapshotItemDto(enPublicado, "Publicado", "https://example.test/p"));
        (await e.Repo.ImagenEstaPublicadaAsync(enPublicado, Ct)).Should().BeTrue();
        (await e.Repo.ImagenEstaPublicadaAsync(soloBorrador, Ct)).Should().BeFalse();
        con2.HasUnpublishedChanges.Should().BeTrue();

        var punto = new PortalMarcaPuntoHandler(e.Repo, e.Almacen, Microsoft.Extensions.Logging.Abstractions.NullLogger<PortalMarcaPuntoHandler>.Instance);
        (await punto.ObtenerAsync(e.Codigo, Ct)).Banners.Should().ContainSingle().Which.ImageId.Should().Be(enPublicado);
        (await punto.ObtenerImagenAsync(soloBorrador, null, Ct)).Encontrada.Should().BeFalse("una imagen de borrador no se sirve");

        var v2 = (await e.Admin.PublicarAsync(e.Organismo, con2.RowVersion, e.Actor, Ct)).Valor!;
        v2.PublishedVersion.Should().Be(v1.PublishedVersion + 1, "cambiar los banners cambia la versión publicada");
        v2.HasUnpublishedChanges.Should().BeFalse();
        (await e.Repo.ImagenEstaPublicadaAsync(soloBorrador, Ct)).Should().BeTrue();

        await e.Admin.RetirarAsync(e.Organismo, v2.RowVersion, e.Actor, Ct);
        (await e.Repo.ImagenEstaPublicadaAsync(enPublicado, Ct)).Should().BeFalse();
        (await e.Repo.ObtenerAsync(e.Organismo, Ct)).Draft.Banners.Should().HaveCount(2);
    }

    [Fact]
    public async Task Banners_ElEndpointLegacyReemplazaYQuitaElPrimero_YNoDejaBannerIdEnElJson()
    {
        await using var e = await Escenario.CrearAsync(factory, Ct);
        using var s1 = new MemoryStream(ImagenesDePrueba.Png());
        var legacy = (await e.Imagenes.SubirAsync(e.Organismo, PortalMarcaImagen.KindBanner, "b.png", s1, e.Actor, Ct)).Valor!;
        legacy.Banners.Should().ContainSingle().Which.Alt.Should().Be("Banner del organismo");
        await AgregarBannerAsync(e, "Segundo");

        using var s2 = new MemoryStream(ImagenesDePrueba.Png());
        var reemplazado = (await e.Imagenes.SubirAsync(e.Organismo, PortalMarcaImagen.KindBanner, "n.png", s2, e.Actor, Ct)).Valor!;
        reemplazado.Banners.Select(i => i.Alt).Should().Equal("Banner del organismo", "Segundo");
        reemplazado.Banners[0].ImageId.Should().NotBe(legacy.Banners[0].ImageId);

        (await e.Db.Database.SqlQuery<bool>($"""
            SELECT jsonb_exists(draft, 'bannerId') AS "Value" FROM admin.transit_office_portal_brandings WHERE transit_office_id = {e.Organismo}
            """).ToListAsync(Ct)).Single().Should().BeFalse();

        var sin = await e.Imagenes.QuitarAsync(e.Organismo, PortalMarcaImagen.KindBanner, e.Actor, Ct);
        sin.Banners.Select(i => i.Alt).Should().Equal("Segundo");
        sin.Draft.BannerId.Should().Be(sin.Banners[0].ImageId);
    }

    [Fact]
    public async Task Banners_MigracionDelBannerUnico_PasaABanners0EnBorradorYPublicado_EsIdempotente_NoTocaLaWeb_Y_DownLoRestituye()
    {
        await using var e = await Escenario.CrearAsync(factory, Ct);
        var (bannerBorrador, bannerPublicado) = (Guid.NewGuid(), Guid.NewGuid());
        var draft = $$"""{"schemaVersion":1,"platformName":"Viejo","bannerId":"{{bannerBorrador}}"}""";
        var published = $$"""{"schemaVersion":1,"platformName":"Viejo","bannerId":"{{bannerPublicado}}"}""";
        await e.Db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO admin.transit_office_portal_brandings
                (scope, transit_office_id, draft, published, published_version, published_at, published_by, created_by, updated_by, row_version)
            VALUES ('organismo', {e.Organismo}, CAST({draft} AS jsonb), CAST({published} AS jsonb), 1, now(), {e.Actor}, {e.Actor}, {e.Actor}, 1)
            """, Ct);

        var sql = LeerDdl("188-portal-marca-banners.sql");
        await e.Db.Database.ExecuteSqlRawAsync(sql, Ct);
        await e.Db.Database.ExecuteSqlRawAsync(sql, Ct); // idempotente

        var marca = await e.Repo.ObtenerAsync(e.Organismo, Ct);
        marca.Draft.Banners.Should().ContainSingle().Which.Should().Be(new PortalMarcaCarruselItem(bannerBorrador, "Banner del organismo", null));
        marca.Published!.Banners.Should().ContainSingle().Which.Should().Be(new PortalMarcaCarruselItem(bannerPublicado, "Banner del organismo", null));
        marca.Draft.BannerId.Should().Be(bannerBorrador);
        marca.Published.BannerId.Should().Be(bannerPublicado);
        marca.PublishedVersion.Should().Be(1, "migrar no cambia la versión publicada");
        (await e.Db.Database.SqlQuery<bool>($"""
            SELECT jsonb_exists(draft, 'bannerId') OR jsonb_exists(published, 'bannerId') AS "Value"
              FROM admin.transit_office_portal_brandings WHERE transit_office_id = {e.Organismo}
            """).ToListAsync(Ct)).Single().Should().BeFalse("el campo legacy se retira");

        // La migración Down (banners[0] → bannerId) deja el documento en la forma anterior.
        var down = """
            UPDATE admin.transit_office_portal_brandings
               SET draft = CASE WHEN jsonb_typeof(draft -> 'banners') = 'array' AND jsonb_array_length(draft -> 'banners') > 0
                     THEN (draft - 'banners') || jsonb_build_object('bannerId', draft -> 'banners' -> 0 ->> 'imageId') ELSE draft - 'banners' END
             WHERE transit_office_id = '{0}';
            """.Replace("{0}", e.Organismo.ToString(), StringComparison.Ordinal);
        await e.Db.Database.ExecuteSqlRawAsync(down, Ct);
        (await e.Db.Database.SqlQuery<string>($"""
            SELECT draft ->> 'bannerId' AS "Value" FROM admin.transit_office_portal_brandings WHERE transit_office_id = {e.Organismo}
            """).ToListAsync(Ct)).Single().Should().Be(bannerBorrador.ToString());
    }

    [Fact]
    public async Task Banners_UnDocumentoViejoConBannerIdSinMigrarSeLeeComoBanners0()
    {
        await using var e = await Escenario.CrearAsync(factory, Ct);
        var legacy = Guid.NewGuid();
        var draft = $$"""{"schemaVersion":1,"bannerId":"{{legacy}}"}""";
        await e.Db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO admin.transit_office_portal_brandings (scope, transit_office_id, draft, created_by, updated_by, row_version)
            VALUES ('organismo', {e.Organismo}, CAST({draft} AS jsonb), {e.Actor}, {e.Actor}, 1)
            """, Ct);

        var leido = await e.Repo.ObtenerAsync(e.Organismo, Ct);

        leido.Draft.Banners.Should().ContainSingle().Which.ImageId.Should().Be(legacy);
        leido.Draft.BannerId.Should().Be(legacy);
    }

    private static string LeerDdl(string nombre)
    {
        var assembly = typeof(FlitDbContext).Assembly;
        using var stream = assembly.GetManifestResourceStream("Flit.Infrastructure.Persistence.Sql.Ddl." + nombre)
            ?? throw new InvalidOperationException("DDL no embebido: " + nombre);
        using var reader = new StreamReader(stream);
        return string.Join(Environment.NewLine, reader.ReadToEnd().Split((char)10).Where(l => !l.TrimStart().StartsWith("--", StringComparison.Ordinal)));
    }
}
