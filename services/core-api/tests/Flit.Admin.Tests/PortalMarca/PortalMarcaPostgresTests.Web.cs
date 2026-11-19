using Flit.Admin.Application.Companies.TransitOffices.PortalMarca;
using Flit.Admin.Domain.Companies.Branding;
using Flit.Admin.Domain.Companies.TransitOffices.PortalMarca;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace Flit.Admin.Tests.PortalMarca;

/// <summary>
/// HU #14401 (Feature #14400) — DDL 184 (ámbito web en las tablas de marca) y repositorio contra PostgreSQL real. La marca web es una
/// única fila sin organismo; estas pruebas la crean y la retiran, porque no pertenece a ningún <see cref="Escenario"/>.
/// </summary>
public sealed partial class PortalMarcaPostgresTests
{
    private static async Task LimpiarWebAsync(Escenario e)
    {
        await e.Db.Database.ExecuteSqlRawAsync("DELETE FROM admin.transit_office_portal_brand_images WHERE scope = 'web'");
        await e.Db.Database.ExecuteSqlRawAsync("DELETE FROM admin.transit_office_portal_brandings WHERE scope = 'web'");
        await e.Db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM admin.tenant_config_audit_logs WHERE changed_by = {e.Actor}");
    }

    [Fact]
    public async Task Web_DDL_ElAmbitoNoSePuedeCruzarYSoloHayUnaMarcaWeb()
    {
        await using var e = await Escenario.CrearAsync(factory, Ct);
        try
        {
            var webConOrganismo = () => e.Db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO admin.transit_office_portal_brandings (scope, transit_office_id) VALUES ('web', {e.Organismo})
                """, Ct);
            (await webConOrganismo.Should().ThrowAsync<PostgresException>()).Which.ConstraintName
                .Should().Be("ck_transit_office_portal_brandings_scope");

            var organismoSinOrganismo = () => e.Db.Database.ExecuteSqlRawAsync(
                "INSERT INTO admin.transit_office_portal_brandings (scope, transit_office_id) VALUES ('organismo', NULL)", Ct);
            (await organismoSinOrganismo.Should().ThrowAsync<PostgresException>()).Which.ConstraintName
                .Should().Be("ck_transit_office_portal_brandings_scope");

            var sinAmbito = () => e.Db.Database.ExecuteSqlRawAsync(
                "INSERT INTO admin.transit_office_portal_brandings (scope, transit_office_id) VALUES ('otro', NULL)", Ct);
            (await sinAmbito.Should().ThrowAsync<PostgresException>()).Which.ConstraintName
                .Should().Be("ck_transit_office_portal_brandings_scope");

            await e.Db.Database.ExecuteSqlRawAsync("INSERT INTO admin.transit_office_portal_brandings (scope, transit_office_id) VALUES ('web', NULL)", Ct);
            var segundaWeb = () => e.Db.Database.ExecuteSqlRawAsync(
                "INSERT INTO admin.transit_office_portal_brandings (scope, transit_office_id) VALUES ('web', NULL)", Ct);
            (await segundaWeb.Should().ThrowAsync<PostgresException>()).Which.ConstraintName
                .Should().Be("uq_transit_office_portal_brandings_web");

            var imagenWebConOrganismo = () => e.Db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO admin.transit_office_portal_brand_images
                    (id, scope, transit_office_id, kind, version, content_type, storage_path, storage_sha256, size_bytes)
                VALUES ({Guid.NewGuid()}, 'web', {e.Organismo}, 'logo', 1, 'image/png', 'x', {new string('a', 64)}, 1)
                """, Ct);
            (await imagenWebConOrganismo.Should().ThrowAsync<PostgresException>()).Which.ConstraintName
                .Should().Be("ck_transit_office_portal_brand_images_scope");

            await e.Db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO admin.transit_office_portal_brand_images
                    (id, scope, transit_office_id, kind, version, content_type, storage_path, storage_sha256, size_bytes)
                VALUES ({Guid.NewGuid()}, 'web', NULL, 'logo', 1, 'image/png', 'x', {new string('a', 64)}, 1)
                """, Ct);
            var versionRepetida = () => e.Db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO admin.transit_office_portal_brand_images
                    (id, scope, transit_office_id, kind, version, content_type, storage_path, storage_sha256, size_bytes)
                VALUES ({Guid.NewGuid()}, 'web', NULL, 'logo', 1, 'image/png', 'y', {new string('b', 64)}, 1)
                """, Ct);
            (await versionRepetida.Should().ThrowAsync<PostgresException>()).Which.ConstraintName
                .Should().Be("uq_transit_office_portal_brand_images_web_version");
        }
        finally
        {
            await LimpiarWebAsync(e);
        }
    }

    [Fact]
    public async Task Web_DDL_LasFilasDeOrganismoSiguenUnicasPorOrganismoYQuedanEnAmbitoOrganismo()
    {
        await using var e = await Escenario.CrearAsync(factory, Ct);
        await e.Repo.GuardarBorradorAsync(e.Organismo, "Org", null, 0, e.Actor, Ct);

        var otraDelMismo = () => e.Db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO admin.transit_office_portal_brandings (scope, transit_office_id) VALUES ('organismo', {e.Organismo})
            """, Ct);

        (await otraDelMismo.Should().ThrowAsync<PostgresException>()).Which.ConstraintName.Should().Be("uq_transit_office_portal_brandings_office");
        (await e.Db.Database.SqlQuery<string>($"""
            SELECT scope AS "Value" FROM admin.transit_office_portal_brandings WHERE transit_office_id = {e.Organismo}
            """).ToListAsync(Ct)).Should().Equal("organismo");
    }

    [Fact]
    public async Task Web_GuardarPublicarRetirarYEntregarSinTocarLaMarcaDelOrganismo()
    {
        await using var e = await Escenario.CrearAsync(factory, Ct);
        try
        {
            var organismo = (await e.Admin.GuardarAsync(e.Organismo, new GuardarPortalMarcaRequest("Organismo", Colores(), 0), e.Actor, Ct)).Valor!;
            (await e.Admin.PublicarAsync(e.Organismo, organismo.RowVersion, e.Actor, Ct)).Error.Should().BeNull();
            var organismoPublicado = await e.Repo.ObtenerAsync(e.Organismo, Ct);

            (await e.Repo.ObtenerAsync(PortalMarcaAmbito.Web, Ct)).Should().Be(PortalMarcaRegistro.Vacio(PortalMarcaAmbito.Web), "la web no hereda al organismo");
            (await e.Punto.ObtenerWebAsync(Ct)).Should().Be(MarcaPuntoDto.Flit);

            var guardada = (await e.Admin.GuardarAsync(PortalMarcaAmbito.Web, new GuardarPortalMarcaRequest("Web", Colores(), 0), e.Actor, Ct)).Valor!;
            (await e.Admin.GuardarAsync(PortalMarcaAmbito.Web, new GuardarPortalMarcaRequest("Otra", null, 0), e.Actor, Ct)).Error!.Code
                .Should().Be(PortalMarcaErrores.Conflicto, "quien leyó «sin marca» no pisa a quien ya la creó");
            (await e.Punto.ObtenerWebAsync(Ct)).Should().Be(MarcaPuntoDto.Flit, "el borrador no sale");

            var publicada = (await e.Admin.PublicarAsync(PortalMarcaAmbito.Web, guardada.RowVersion, e.Actor, Ct)).Valor!;
            var web = await e.Punto.ObtenerWebAsync(Ct);
            web.Origen.Should().Be("web");
            web.Nombre.Should().Be("Web");
            web.Version.Should().Be(1);
            (await e.Punto.ObtenerAsync(e.Codigo, Ct)).Nombre.Should().Be("Organismo", "el organismo conserva la suya");
            (await e.Repo.ObtenerAsync(e.Organismo, Ct)).Should().Be(organismoPublicado, "publicar la web no toca al organismo");

            await e.Admin.RetirarAsync(PortalMarcaAmbito.Web, publicada.RowVersion, e.Actor, Ct);
            (await e.Punto.ObtenerWebAsync(Ct)).Should().Be(MarcaPuntoDto.Flit);
            (await e.Punto.ObtenerAsync(e.Codigo, Ct)).Origen.Should().Be("organismo", "retirar la web no retira al organismo");
        }
        finally
        {
            await LimpiarWebAsync(e);
        }
    }

    [Fact]
    public async Task Web_LasImagenesSeVersionanPorAmbitoYSoloLaPublicadaSale()
    {
        await using var e = await Escenario.CrearAsync(factory, Ct);
        try
        {
            using var deOrganismo = new MemoryStream(ImagenesDePrueba.Png());
            var logoOrganismo = (await e.Imagenes.SubirAsync(e.Organismo, PortalMarcaImagen.KindLogo, "o.png", deOrganismo, e.Actor, Ct)).Valor!.Draft.LogoId!.Value;

            using var uno = new MemoryStream(ImagenesDePrueba.Png());
            var conLogo = (await e.Imagenes.SubirAsync(PortalMarcaAmbito.Web, PortalMarcaImagen.KindLogo, "w.png", uno, e.Actor, Ct)).Valor!;
            using var dos = new MemoryStream(ImagenesDePrueba.Jpeg());
            var conOtro = (await e.Imagenes.SubirAsync(PortalMarcaAmbito.Web, PortalMarcaImagen.KindLogo, "w2.jpg", dos, e.Actor, Ct)).Valor!;

            (await e.Repo.ObtenerImagenAsync(logoOrganismo, Ct))!.Version.Should().Be(1);
            (await e.Repo.ObtenerImagenAsync(logoOrganismo, Ct))!.TransitOfficeId.Should().Be(e.Organismo);
            (await e.Repo.ObtenerImagenAsync(conLogo.Draft.LogoId!.Value, Ct))!.Version.Should().Be(1, "la versión cuenta por ámbito");
            (await e.Repo.ObtenerImagenAsync(conOtro.Draft.LogoId!.Value, Ct))!.Version.Should().Be(2);
            (await e.Repo.ObtenerImagenAsync(conOtro.Draft.LogoId!.Value, Ct))!.TransitOfficeId.Should().Be(PortalMarcaAmbito.Web);
            (await e.Repo.ObtenerAsync(e.Organismo, Ct)).Draft.LogoId.Should().Be(logoOrganismo, "cargar el logo de la web no cambia el del organismo");

            (await e.Punto.ObtenerImagenAsync(conOtro.Draft.LogoId!.Value, null, Ct)).Encontrada.Should().BeFalse("es de borrador");
            var nombre = (await e.Admin.GuardarAsync(PortalMarcaAmbito.Web, new GuardarPortalMarcaRequest("Web", Colores(), conOtro.RowVersion), e.Actor, Ct)).Valor!;
            (await e.Admin.PublicarAsync(PortalMarcaAmbito.Web, nombre.RowVersion, e.Actor, Ct)).Error.Should().BeNull();

            (await e.Punto.ObtenerImagenAsync(conOtro.Draft.LogoId!.Value, null, Ct)).Encontrada.Should().BeTrue();
            (await e.Punto.ObtenerWebAsync(Ct)).LogoId.Should().Be(conOtro.Draft.LogoId);
            (await e.Punto.ObtenerImagenAsync(conLogo.Draft.LogoId!.Value, null, Ct)).Encontrada.Should().BeFalse("la versión anterior no está publicada");

            var sinLogo = await e.Imagenes.QuitarAsync(PortalMarcaAmbito.Web, PortalMarcaImagen.KindLogo, e.Actor, Ct);
            sinLogo.Draft.LogoId.Should().BeNull();
            (await e.Repo.ObtenerAsync(e.Organismo, Ct)).Draft.LogoId.Should().Be(logoOrganismo, "quitar el de la web no quita el del organismo");
        }
        finally
        {
            await LimpiarWebAsync(e);
        }
    }

    [Fact]
    public async Task Web_ElLogoPrimeroCreaLaFilaWebYGuardarDespuesNoLoPisa()
    {
        await using var e = await Escenario.CrearAsync(factory, Ct);
        try
        {
            using var logo = new MemoryStream(ImagenesDePrueba.Png());
            var conLogo = (await e.Imagenes.SubirAsync(PortalMarcaAmbito.Web, PortalMarcaImagen.KindLogo, "w.png", logo, e.Actor, Ct)).Valor!;

            var guardada = await e.Repo.GuardarBorradorAsync(
                PortalMarcaAmbito.Web, "Web", new BrandColors("#0B3D91", "#0F5132", "#FFFFFF"), conLogo.RowVersion, e.Actor, Ct);

            guardada!.Draft.LogoId.Should().Be(conLogo.Draft.LogoId);
            guardada.Draft.PlatformName.Should().Be("Web");
            (await e.Db.Database.SqlQuery<long>($"""
                SELECT count(*) AS "Value" FROM admin.transit_office_portal_brandings WHERE scope = 'web'
                """).ToListAsync(Ct)).Should().Equal(1);
        }
        finally
        {
            await LimpiarWebAsync(e);
        }
    }
}
