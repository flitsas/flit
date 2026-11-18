using Flit.Api.Usuarios;
using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Repositories;
using Flit.Modules.Security.Application.UserRoles.ProductRoles;
using Flit.Modules.Security.Domain.UserRoles.ProductRoles;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Flit.Admin.Tests.Usuarios;

/// <summary>
/// ADR-0089 (HU #14587, CR-3) — <see cref="ProductRoleRepository"/> y <see cref="ActorUsuariosProducto"/> contra PostgreSQL 16
/// con las migraciones aplicadas (base de pruebas en 5433, <c>ConnectionStrings__Core</c>). Comprueba lo que los fakes no
/// pueden: el disparador <c>tr_ura_product_code</c>, el índice por producto, la marca de suspensión, la guarda atómica del
/// último admin, la FK a <c>platform.products</c>, la traducción de violaciones a códigos del contrato y la lectura del
/// actor. Cada prueba siembra sus propias empresas y usuarios inventados.
/// </summary>
/// <remarks>Uso de ejemplo: <c>await using var e = await Escena.CrearAsync(ct); await e.Repo.SuspendAsync(...)</c>.</remarks>
public sealed class ProductRoleRepositoryPostgresTests
{
    private const string P = "diagnostico";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task CambiarRol_SoloTocaDiagnostico_ElRolDeTramitesSigueVigente()
    {
        await using var e = await Escena.CrearAsync(Ct);
        var u = await e.UsuarioAsync(e.Empresa, e.RolUsuario, conTramites: true);

        await e.Repo.ReplaceProductRoleAsync(e.Empresa, u, P, e.RolAdmin, e.Actor, null, Ct);

        (await e.RolesVigentesAsync(u, e.Empresa)).Should().BeEquivalentTo(["admin_diagnostico:diagnostico", $"{e.CodigoTramites}:tramites"]);
    }

    [Fact]
    public async Task Suspender_DejaLaMarca_SinTocarLaCuenta_YReactivarDevuelveElRol()
    {
        await using var e = await Escena.CrearAsync(Ct);
        var u = await e.UsuarioAsync(e.Empresa, e.RolUsuario, conTramites: true);

        await e.Repo.SuspendAsync(e.Empresa, u, P, e.RolUsuario, e.Actor, null, Ct);

        var suspendido = await e.Repo.GetMemberAsync(e.Empresa, u, P, Ct);
        suspendido!.CurrentRole.Should().BeNull();
        suspendido.SuspendedRole!.Code.Should().Be("usuario_diagnostico");
        suspendido.AccountActive.Should().BeTrue("SP-12=a: la cuenta no cambia");
        (await e.RolesVigentesAsync(u, e.Empresa)).Should().Equal($"{e.CodigoTramites}:tramites");

        await e.Repo.ReactivateAsync(e.Empresa, u, P, e.RolUsuario, e.Actor, Ct);

        var activo = await e.Repo.GetMemberAsync(e.Empresa, u, P, Ct);
        activo!.CurrentRole!.Code.Should().Be("usuario_diagnostico");
        activo.SuspendedRole.Should().BeNull();
    }

    [Fact]
    public async Task GuardaAtomica_ElUnicoAdminActivo_NoSePierde_YElSuspendidoTemporalNoCuenta()
    {
        await using var e = await Escena.CrearAsync(Ct);
        var unico = await e.UsuarioAsync(e.Empresa, e.RolAdmin);
        var suspendidoTemporal = await e.UsuarioAsync(e.Empresa, e.RolAdmin);
        await e.SuspensionTemporalAsync(suspendidoTemporal, e.Empresa);

        (await e.Repo.CountOtherActiveAdminsAsync(e.Empresa, "admin_diagnostico", unico, Ct)).Should().Be(0, "CR-5");
        var cambiar = () => e.Repo.ReplaceProductRoleAsync(e.Empresa, unico, P, e.RolUsuario, e.Actor, "admin_diagnostico", Ct);
        var suspender = () => e.Repo.SuspendAsync(e.Empresa, unico, P, e.RolAdmin, e.Actor, "admin_diagnostico", Ct);

        (await cambiar.Should().ThrowAsync<ProductRoleRuleException>()).Which.Code.Should().Be("ULTIMO_ADMINISTRADOR");
        (await suspender.Should().ThrowAsync<ProductRoleRuleException>()).Which.Code.Should().Be("ULTIMO_ADMINISTRADOR");
        (await e.RolesVigentesAsync(unico, e.Empresa)).Should().Equal("admin_diagnostico:diagnostico");
    }

    [Fact]
    public async Task GuardaAtomica_ConOtroAdminActivo_Deja()
    {
        await using var e = await Escena.CrearAsync(Ct);
        var a1 = await e.UsuarioAsync(e.Empresa, e.RolAdmin);
        await e.UsuarioAsync(e.Empresa, e.RolAdmin);

        await e.Repo.ReplaceProductRoleAsync(e.Empresa, a1, P, e.RolUsuario, e.Actor, "admin_diagnostico", Ct);

        (await e.RolesVigentesAsync(a1, e.Empresa)).Should().Equal("usuario_diagnostico:diagnostico");
    }

    [Fact]
    public async Task ViolacionDeUnicidad_SeTraduceARolYaVigente()
    {
        await using var e = await Escena.CrearAsync(Ct);
        var u = await e.UsuarioAsync(e.Empresa, e.RolUsuario);

        var act = () => e.Repo.AddProductRoleAsync(e.Empresa, u, e.RolAdmin, e.Actor, Ct);

        (await act.Should().ThrowAsync<ProductRoleRuleException>()).Which.Should()
            .Match<ProductRoleRuleException>(x => x.Code == "ROL_YA_VIGENTE" && x.Kind == ProductRoleErrorKind.FailedPrecondition);
    }

    [Fact]
    public async Task MarcaDeProductoInexistente_LaRechazaLaFk_YSeTraduce()
    {
        await using var e = await Escena.CrearAsync(Ct);
        var u = await e.UsuarioAsync(e.Empresa, e.RolUsuario);

        var act = () => e.Repo.SuspendAsync(e.Empresa, u, "no_existe", e.RolUsuario, e.Actor, null, Ct);

        (await act.Should().ThrowAsync<ProductRoleRuleException>()).Which.Kind.Should().Be(ProductRoleErrorKind.NotFound);
        (await e.EscalarAsync("SELECT character_maximum_length::text AS \"Value\" FROM information_schema.columns WHERE table_schema = 'security' AND table_name = 'product_role_suspensions' AND column_name = 'product_code'"))
            .Should().Be("40");
        (await e.EscalarAsync("SELECT count(*)::text AS \"Value\" FROM pg_constraint WHERE conname = 'fk_product_role_suspensions_products'")).Should().Be("1");
    }

    [Fact]
    public async Task ListadoYConteos_SoloListaBlanca_SinSuspendidosTemporalesEnLosAdmins()
    {
        await using var e = await Escena.CrearAsync(Ct);
        await e.UsuarioAsync(e.Empresa, e.RolUsuario, nombre: "Usuario Lista Blanca");
        await e.UsuarioAsync(e.Empresa, e.RolAdmin, nombre: "Admin Activo");
        var suspendido = await e.UsuarioAsync(e.Empresa, e.RolAdmin, nombre: "Admin Suspendido Temporal");
        await e.SuspensionTemporalAsync(suspendido, e.Empresa);
        await e.UsuarioAsync(e.Empresa, e.RolPropio, nombre: "Con Rol Propio");
        await e.InvitacionAsync(e.Empresa, e.RolUsuario);
        await e.InvitacionAsync(e.Empresa, e.RolPropio);

        var conteo = (await new ListProductUsersHandler(e.Repo).CountByTenantAsync(P, Ct)).Single(c => c.TenantId == e.Empresa);
        var lista = await new ListProductUsersHandler(e.Repo).HandleAsync(
            new ListProductUsersQuery(e.Empresa, new ProductActor(e.Actor, true), P, null, [], [], 1, 25), Ct);

        conteo.Should().Be(new ProductTenantCount(e.Empresa, 4, 1), "rol propio fuera (CR-1); el admin con suspensión temporal no cuenta como admin (CR-5)");
        lista.Total.Should().Be(4);
        lista.Users.Select(u => u.Name).Should().NotContain("Con Rol Propio");
    }

    [Fact]
    public async Task ResolverInvitacion_AdminCompanyProtegido_RolPropioYaMiembro_OtraEmpresa()
    {
        await using var e = await Escena.CrearAsync(Ct);
        var adminCompany = await e.UsuarioAsync(e.Empresa, e.RolAdminCompany);
        var propio = await e.UsuarioAsync(e.Empresa, e.RolPropio);
        var ajeno = await e.UsuarioAsync(e.OtraEmpresa, e.RolUsuario);

        (await e.Repo.ResolveInviteTargetAsync(e.Empresa, e.Correo(adminCompany), P, Ct)).Kind.Should().Be(InviteTargetKind.ProtectedMember);
        (await e.Repo.ResolveInviteTargetAsync(e.Empresa, e.Correo(propio), P, Ct)).Kind.Should().Be(InviteTargetKind.MemberWithRole);
        (await e.Repo.ResolveInviteTargetAsync(e.Empresa, e.Correo(ajeno), P, Ct)).Kind.Should().Be(InviteTargetKind.OtherTenant);
        (await e.Repo.ResolveInviteTargetAsync(e.Empresa, $"nadie.{Guid.NewGuid():N}@ejemplo.test", P, Ct)).Kind.Should().Be(InviteTargetKind.NewEmail);
    }

    [Fact]
    public async Task Actor_AdminConPermiso_SuperAdmin_YUsuarioSinGestion()
    {
        await using var e = await Escena.CrearAsync(Ct);
        var admin = await e.UsuarioAsync(e.Empresa, e.RolAdmin);
        var usuario = await e.UsuarioAsync(e.Empresa, e.RolUsuario);
        var super = await e.UsuarioAsync(e.OtraEmpresa, e.RolSuperAdmin);
        var actor = new ActorUsuariosProducto(e.Db);

        (await actor.VerificarAsync(e.Empresa, admin, P, Ct)).Should().Be(new ProductActor(admin, false));
        (await actor.VerificarAsync(e.Empresa, usuario, P, Ct)).Should().BeNull("usuario_diagnostico no gestiona usuarios");
        (await actor.VerificarAsync(e.Empresa, super, P, Ct)).Should().Be(new ProductActor(super, true));
        (await actor.VerificarAsync(null, super, P, Ct)).Should().Be(new ProductActor(super, true));
        (await actor.VerificarAsync(null, admin, P, Ct)).Should().BeNull("los conteos globales son solo del SuperAdmin");

        await e.SuspensionTemporalAsync(admin, e.Empresa);
        (await actor.VerificarAsync(e.Empresa, admin, P, Ct)).Should().BeNull("suspendido temporalmente en la empresa");
    }

    [Fact]
    public async Task EmpresaHabilitada_SoloConDiagnosticoEncendido()
    {
        await using var e = await Escena.CrearAsync(Ct);
        var actor = new ActorUsuariosProducto(e.Db);

        (await actor.EmpresaHabilitadaAsync(e.Empresa, P, Ct)).Should().BeTrue();
        (await actor.EmpresaHabilitadaAsync(e.OtraEmpresa, P, Ct)).Should().BeFalse("sin Diagnóstico encendido");
        (await actor.EmpresaHabilitadaAsync(Guid.NewGuid(), P, Ct)).Should().BeFalse("no existe");
    }

    /// <summary>Datos inventados de una prueba: dos empresas (solo la primera con Diagnóstico), roles y usuarios.</summary>
    private sealed class Escena : IAsyncDisposable
    {
        private readonly Dictionary<Guid, string> _correos = [];

        private Escena(FlitDbContext db) => Db = db;

        public FlitDbContext Db { get; }
        public ProductRoleRepository Repo => new(Db);
        public Guid Empresa { get; } = Guid.CreateVersion7();
        public Guid OtraEmpresa { get; } = Guid.CreateVersion7();
        public Guid Actor { get; private set; }
        public Guid RolUsuario { get; private set; }
        public Guid RolAdmin { get; private set; }
        public Guid RolPropio { get; private set; }
        public Guid RolTramites { get; private set; }
        public Guid RolAdminCompany { get; private set; }
        public Guid RolSuperAdmin { get; private set; }
        public string CodigoTramites { get; } = $"tram_prueba_{Guid.NewGuid():N}"[..30];

        public static async Task<Escena> CrearAsync(CancellationToken ct)
        {
            var cs = Environment.GetEnvironmentVariable("ConnectionStrings__Core");
            cs.Should().NotBeNullOrWhiteSpace("estas pruebas corren contra la base de pruebas 5433 (ConnectionStrings__Core)");
            var db = new FlitDbContext((DbContextOptions<FlitDbContext>)NpgsqlConventions.Apply(new DbContextOptionsBuilder<FlitDbContext>(), cs!).Options);
            var e = new Escena(db);
            await e.SembrarAsync(ct);
            return e;
        }

        private async Task SembrarAsync(CancellationToken ct)
        {
            foreach (var t in new[] { Empresa, OtraEmpresa })
            {
                await Db.Database.ExecuteSqlInterpolatedAsync(
                    $"INSERT INTO identity.tenants (id, code, legal_name, tax_id, tenant_type, created_at) VALUES ({t}, {"prb-" + Guid.NewGuid().ToString("N")[..20]}, {"Empresa de prueba"}, {Guid.NewGuid().ToString("N")[..12]}, {"RENTING"}, now())", ct);
            }

            await Db.Database.ExecuteSqlInterpolatedAsync(
                $"INSERT INTO platform.tenant_products (tenant_id, product_code, enabled) VALUES ({Empresa}, {P}, true)", ct);

            RolUsuario = await RolGlobalAsync("usuario_diagnostico", "diagnostico", ct);
            RolAdmin = await RolGlobalAsync("admin_diagnostico", "diagnostico", ct);
            RolAdminCompany = await RolGlobalAsync("AdminCompany", "plataforma", ct);
            RolSuperAdmin = await RolGlobalAsync("SuperAdmin", "plataforma", ct);
            RolTramites = await RolGlobalAsync(CodigoTramites, "tramites", ct);
            RolPropio = Guid.CreateVersion7();
            await Db.Database.ExecuteSqlInterpolatedAsync(
                $"INSERT INTO security.roles (id, code, name, is_system, is_active, target_entity_type, product_code, tenant_id) VALUES ({RolPropio}, {"rol_propio_diagnostico"}, {"Rol propio"}, false, true, {"COMPANY"}, {P}, {Empresa})", ct);

            await Db.Database.ExecuteSqlRawAsync("""
                INSERT INTO security.modules (code, name, product_code)
                SELECT 'diagnostico_usuarios', 'Diagnóstico · Usuarios', 'diagnostico'
                 WHERE NOT EXISTS (SELECT 1 FROM security.modules WHERE code = 'diagnostico_usuarios');
                INSERT INTO security.permissions (module_id, slug, name, http_method, route_pattern)
                SELECT m.id, 'diagnostico.usuarios.gestionar', 'Gestionar los usuarios de Diagnóstico', '', ''
                  FROM security.modules m WHERE m.code = 'diagnostico_usuarios'
                   AND NOT EXISTS (SELECT 1 FROM security.permissions WHERE slug = 'diagnostico.usuarios.gestionar');
                INSERT INTO security.role_permissions (role_id, permission_id)
                SELECT r.id, p.id FROM security.roles r, security.permissions p
                 WHERE r.code = 'admin_diagnostico' AND r.tenant_id IS NULL AND r.deleted_at IS NULL AND p.slug = 'diagnostico.usuarios.gestionar'
                   AND NOT EXISTS (SELECT 1 FROM security.role_permissions x WHERE x.role_id = r.id AND x.permission_id = p.id);
                """, ct);

            Actor = await UsuarioAsync(OtraEmpresa, RolSuperAdmin);
        }

        private async Task<Guid> RolGlobalAsync(string code, string product, CancellationToken ct)
        {
            await Db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO security.roles (code, name, is_system, is_active, target_entity_type, product_code)
                SELECT {code}, {code}, true, true, 'COMPANY', {product}
                 WHERE NOT EXISTS (SELECT 1 FROM security.roles WHERE code = {code} AND target_entity_type = 'COMPANY' AND tenant_id IS NULL AND deleted_at IS NULL)
                """, ct);
            return await Db.Database.SqlQuery<Guid>(
                $"SELECT id AS \"Value\" FROM security.roles WHERE code = {code} AND target_entity_type = 'COMPANY' AND tenant_id IS NULL AND deleted_at IS NULL ORDER BY created_at LIMIT 1").SingleAsync(ct);
        }

        public async Task<Guid> UsuarioAsync(Guid tenant, Guid rol, bool conTramites = false, string nombre = "Persona de prueba")
        {
            var id = Guid.CreateVersion7();
            var correo = $"prb.{id:N}@ejemplo.test";
            _correos[id] = correo;
            await Db.Database.ExecuteSqlInterpolatedAsync(
                $"INSERT INTO identity.users (id, email, display_name, status, created_at, home_tenant_id) VALUES ({id}, {correo}, {nombre}, 'active', now(), {tenant})", Ct);
            await AsignarAsync(id, tenant, rol);
            if (conTramites)
                await AsignarAsync(id, tenant, RolTramites);
            return id;
        }

        private Task AsignarAsync(Guid usuario, Guid tenant, Guid rol) =>
            Db.Database.ExecuteSqlInterpolatedAsync(
                $"INSERT INTO security.user_role_assignments (tenant_id, user_id, role_id, assigned_at) VALUES ({tenant}, {usuario}, {rol}, now())", Ct);

        public string Correo(Guid usuario) => _correos[usuario];

        public Task SuspensionTemporalAsync(Guid usuario, Guid tenant) =>
            Db.Database.ExecuteSqlInterpolatedAsync(
                $"INSERT INTO security.user_temp_suspensions (tenant_id, user_id, starts_at, reason, created_at) VALUES ({tenant}, {usuario}, now() - interval '1 hour', 'prueba', now())", Ct);

        public Task InvitacionAsync(Guid tenant, Guid rol)
        {
            var id = Guid.CreateVersion7();
            return Db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO security.user_invitations (id, tenant_id, email, full_name, token_hash, status, invited_by, created_at)
                VALUES ({id}, {tenant}, {$"inv.{id:N}@ejemplo.test"}, 'Invitado de prueba', {id.ToString("N")}, 'pending', {Actor}, now());
                INSERT INTO security.invitation_roles (id, tenant_id, invitation_id, role_id, created_at) VALUES ({Guid.CreateVersion7()}, {tenant}, {id}, {rol}, now());
                """, Ct);
        }

        public Task<List<string>> RolesVigentesAsync(Guid usuario, Guid tenant) =>
            Db.Database.SqlQuery<string>($"""
                SELECT r.code || ':' || a.product_code AS "Value" FROM security.user_role_assignments a JOIN security.roles r ON r.id = a.role_id
                 WHERE a.user_id = {usuario} AND a.tenant_id = {tenant} AND a.deleted_at IS NULL ORDER BY 1
                """).ToListAsync(Ct);

        public Task<string> EscalarAsync(string sql) => Db.Database.SqlQueryRaw<string>(sql).SingleAsync(Ct);

        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }
}
