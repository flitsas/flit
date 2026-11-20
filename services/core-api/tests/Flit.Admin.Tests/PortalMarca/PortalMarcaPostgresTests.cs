using Flit.Admin.Application.Auditing;
using Flit.Admin.Application.Companies.TransitOffices.PortalMarca;
using Flit.Admin.Domain.Companies.Branding;
using Flit.Admin.Domain.Companies.TransitOffices.PortalMarca;
using Flit.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Xunit;

namespace Flit.Admin.Tests.PortalMarca;

/// <summary>
/// Feature #14374 (Épica #14297) — DDL 180 y repositorio de la marca del portal contra PostgreSQL real (migración
/// <c>20261105120000_HU14375_PortalMarcaOrganismo</c> aplicada por el host). Cada prueba arma su propio organismo y lo retira.
/// </summary>
public sealed partial class PortalMarcaPostgresTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static PortalMarcaColoresDto Colores() => new("#0B3D91", "#0F5132", "#FFFFFF");

    // ── DDL ──────────────────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task DDL_LasTablasNacenPropiasPorOrganismoYSinDisparadoresDeMarcaBlanca()
    {
        await using var e = await Escenario.CrearAsync(factory, Ct);

        var triggers = await e.Db.Database.SqlQuery<string>($"""
            SELECT tgname AS "Value" FROM pg_trigger t
              JOIN pg_class c ON c.oid = t.tgrelid JOIN pg_namespace n ON n.oid = c.relnamespace
             WHERE n.nspname = 'admin' AND c.relname IN ('transit_office_portal_brandings', 'transit_office_portal_brand_images')
               AND NOT t.tgisinternal
            """).ToListAsync(Ct);

        triggers.Should().NotContain(t => t.Contains("marca_blanca", StringComparison.Ordinal), "las tablas de marca blanca no se reutilizan");
        triggers.Should().Contain("tr_transit_office_portal_brandings_row_version").And.Contain("tr_transit_office_portal_brandings_audit");
        (await e.Db.Database.SqlQuery<string>($"""
            SELECT column_name AS "Value" FROM information_schema.columns
             WHERE table_schema = 'admin' AND table_name = 'transit_office_portal_brandings' AND column_name IN ('tenant_id', 'transit_office_id')
            """).ToListAsync(Ct)).Should().Equal("transit_office_id");
    }

    [Fact]
    public async Task DDL_UnPublicadoSinFechaOSinAutorSeRechazaPorElCheck()
    {
        await using var e = await Escenario.CrearAsync(factory, Ct);

        var act = () => e.Db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO admin.transit_office_portal_brandings (transit_office_id, published, published_version)
            VALUES ({e.Organismo}, jsonb_build_object('schemaVersion', 1), 1)
            """, Ct);

        var ex = await act.Should().ThrowAsync<PostgresException>();
        ex.Which.ConstraintName.Should().Be("ck_transit_office_portal_brandings_published_consistent");
    }

    [Fact]
    public async Task DDL_UnOrganismoInexistenteSeRechazaPorLaLlaveForanea()
    {
        await using var e = await Escenario.CrearAsync(factory, Ct);

        var act = () => e.Repo.GuardarBorradorAsync(Guid.NewGuid(), "Nombre", null, 0, e.Actor, Ct);

        var ex = await act.Should().ThrowAsync<Exception>();
        (ex.Which as PostgresException ?? ex.Which.InnerException as PostgresException)!.ConstraintName
            .Should().Be("fk_transit_office_portal_brandings_transit_offices");
    }

    // ── Persistencia y concurrencia ──────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task SinFilaLaMarcaEsUnRegistroVacioConVersionCero()
    {
        await using var e = await Escenario.CrearAsync(factory, Ct);

        var m = await e.Repo.ObtenerAsync(e.Organismo, Ct);

        m.RowVersion.Should().Be(0);
        m.Draft.Should().Be(PortalMarcaSnapshot.Empty);
        m.Published.Should().BeNull();
        m.PublishedVersion.Should().Be(0);
    }

    [Fact]
    public async Task ElPrimeroQueGuardaConVersionCeroCreaLaFilaYElSegundoRecibeConflictoSinPisarlo()
    {
        await using var e = await Escenario.CrearAsync(factory, Ct);

        var primero = await e.Repo.GuardarBorradorAsync(e.Organismo, "Primero", new BrandColors("#0B3D91", "#0F5132", "#FFFFFF"), 0, e.Actor, Ct);
        var segundo = await e.Repo.GuardarBorradorAsync(e.Organismo, "Segundo", null, 0, e.Actor, Ct);

        primero.Should().NotBeNull();
        primero!.RowVersion.Should().BeGreaterThan(0);
        segundo.Should().BeNull("leyó «sin marca» y otra persona ya la creó");
        (await e.Repo.ObtenerAsync(e.Organismo, Ct)).Draft.PlatformName.Should().Be("Primero");

        var tercero = await e.Repo.GuardarBorradorAsync(e.Organismo, "Tercero", null, primero.RowVersion, e.Actor, Ct);
        tercero!.Draft.PlatformName.Should().Be("Tercero");
        tercero.Draft.Colors.Should().BeNull("el borrador se reemplaza por lo que envía el editor");
        (await e.Repo.GuardarBorradorAsync(e.Organismo, "Viejo", null, primero.RowVersion, e.Actor, Ct)).Should().BeNull();
    }

    [Fact]
    public async Task PublicarYRetirarPersistenLaVersionElPublicadoYConservanElBorrador()
    {
        await using var e = await Escenario.CrearAsync(factory, Ct);
        var g = await e.Admin.GuardarAsync(e.Organismo, new GuardarPortalMarcaRequest("Mi organismo", Colores(), 0), e.Actor, Ct);

        var p = await e.Admin.PublicarAsync(e.Organismo, g.Valor!.RowVersion, e.Actor, Ct);

        p.Error.Should().BeNull();
        p.Valor!.PublishedVersion.Should().Be(1);
        p.Valor.Published.Should().BeEquivalentTo(p.Valor.Draft);
        p.Valor.PublishedBy!.UserId.Should().Be(e.Actor);
        p.Valor.PublishedAt.Should().NotBeNull();
        (await e.Admin.PublicarAsync(e.Organismo, g.Valor.RowVersion, e.Actor, Ct)).Error!.Code
            .Should().Be(PortalMarcaErrores.Conflicto, "ya cambió la versión");

        var r = await e.Admin.RetirarAsync(e.Organismo, p.Valor.RowVersion, e.Actor, Ct);

        r.Valor!.Published.Should().BeNull();
        r.Valor.PublishedAt.Should().BeNull();
        r.Valor.Draft.PlatformName.Should().Be("Mi organismo");
        r.Valor.PublishedVersion.Should().Be(1);
        var republicada = await e.Admin.PublicarAsync(e.Organismo, r.Valor.RowVersion, e.Actor, Ct);
        republicada.Valor!.PublishedVersion.Should().Be(2, "la versión sigue subiendo tras un retiro");
    }

    // ── Entrega al portal: inmediato y solo lo publicado ─────────────────────────────────────────────────────────────

    // ── Auditoría y aislamiento de marca blanca ──────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AC5_PublicarDejaEnLaAuditoriaQuienYQueCambio()
    {
        await using var e = await Escenario.CrearAsync(factory, Ct);
        var g = (await e.Admin.GuardarAsync(e.Organismo, new GuardarPortalMarcaRequest("Auditado", Colores(), 0), e.Actor, Ct)).Valor!;

        await e.Admin.PublicarAsync(e.Organismo, g.RowVersion, e.Actor, Ct);

        var filas = await e.Db.Database.SqlQuery<string>($"""
            SELECT new_value AS "Value" FROM admin.tenant_config_audit_logs
             WHERE entity_name = 'portal_marca_publicada' AND target_entity_id = {e.Organismo} AND changed_by = {e.Actor}
            """).ToListAsync(Ct);
        filas.Should().ContainSingle().Which.Should().Contain("Auditado").And.Contain("#0B3D91");
    }

    [Fact]
    public async Task LaMarcaDelPortalNoTocaLasTablasDeMarcaBlanca()
    {
        await using var e = await Escenario.CrearAsync(factory, Ct);
        var antes = (await e.Db.Database.SqlQuery<long>($"""SELECT count(*) AS "Value" FROM admin.tenant_brandings""").ToListAsync(Ct)).Single();
        var g = (await e.Admin.GuardarAsync(e.Organismo, new GuardarPortalMarcaRequest("Portal", Colores(), 0), e.Actor, Ct)).Valor!;
        await e.Admin.PublicarAsync(e.Organismo, g.RowVersion, e.Actor, Ct);

        (await e.Db.Database.SqlQuery<long>($"""SELECT count(*) AS "Value" FROM admin.tenant_brandings""").ToListAsync(Ct)).Single().Should().Be(antes);
    }

    private sealed partial class Escenario : IAsyncDisposable
    {
        private readonly IServiceScope _scope;
        private readonly string _sufijo = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();

        public FlitDbContext Db { get; }
        public IPortalMarcaRepository Repo { get; }
        public PortalMarcaAdminHandler Admin { get; }
        public AlmacenImagenEnMemoria Almacen { get; } = new();
        public Guid Organismo { get; } = Guid.NewGuid();
        public Guid Actor { get; } = Guid.NewGuid();
        public string Codigo { get; }

        private Escenario(IServiceScope scope)
        {
            _scope = scope;
            var sp = scope.ServiceProvider;
            Db = sp.GetRequiredService<FlitDbContext>();
            Repo = sp.GetRequiredService<IPortalMarcaRepository>();
            Admin = new PortalMarcaAdminHandler(Repo, sp.GetRequiredService<IAdminAuditWriter>());
            Codigo = $"PM{_sufijo}";
        }

        public static async Task<Escenario> CrearAsync(WebApplicationFactory<Program> host, CancellationToken ct)
        {
            var e = new Escenario(host.Services.CreateScope());
            var correo = $"pm-{e._sufijo.ToLowerInvariant()}@example.test";
            await e.Db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO identity.users (id, email, display_name, status, created_at) VALUES ({e.Actor}, {correo}, {"Actor de prueba"}, 'active', now())", ct);
            await e.Db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code, city_name, department_name)
                VALUES ({e.Organismo}, {e.Codigo}, {"Organismo de prueba " + e.Codigo}, '99', '999', 'Municipio de prueba', 'Departamento de prueba')
                """, ct);
            return e;
        }

        public async ValueTask DisposeAsync()
        {
            await Db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM admin.tenant_config_audit_logs WHERE target_entity_id = {Organismo}");
            await Db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM admin.transit_office_portal_brand_images WHERE transit_office_id = {Organismo}");
            await Db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM admin.transit_office_portal_brandings WHERE transit_office_id = {Organismo}");
            await Db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM catalogs.transit_offices WHERE id = {Organismo}");
            await Db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM identity.users WHERE id = {Actor}");
            _scope.Dispose();
        }
    }
}
