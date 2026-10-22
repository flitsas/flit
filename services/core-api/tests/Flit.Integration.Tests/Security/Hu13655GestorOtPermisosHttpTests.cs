using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using Flit.Infrastructure.Persistence.Entities.Identity;
using Flit.Infrastructure.Persistence.Entities.Security;
using Flit.Infrastructure.Persistence.Repositories;
using Flit.Infrastructure.Persistence.Sql;
using Flit.Infrastructure.Security;
using Flit.Integration.Tests.MarcaBlanca;
using Flit.Integration.Tests.Postgres;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Npgsql;
using Xunit;

namespace Flit.Integration.Tests.Security;

/// <summary>
/// HU #13655 (Feature #13104 F-D2, Épica #12753) de punta a punta con el rol «Gestor OT» real (DDL 137 + 147): login real
/// contra Postgres → JWT → host HTTP real.
/// <list type="bullet">
///   <item>El token del Gestor OT trae solo sus permisos base (bandeja e Inicio) y es de organismo de tránsito.</item>
///   <item>Con el flag OT encendido: la bandeja abre; Prelación documental no, hasta que el Admin OT le otorga
///   <c>ot.documentos.manage</c> como permiso individual (y entonces sí).</item>
///   <item>FB-9: el Admin OT edita los permisos de un Gestor OT de su organismo, no los de otro Admin OT.</item>
///   <item>Usuarios: el Admin OT invita con el rol Gestor OT y le cambia el rol; el Gestor OT sin el permiso no entra
///   (aun con el flag apagado) y, con él delegado, no puede invitar administradores.</item>
/// </list>
/// </summary>
public sealed class Hu13655GestorOtPermisosHttpTests(PostgresDatabaseFixture fixture) : PostgresTestBase(fixture)
{
    private const string AdminEmail = "admin-it13655@ot.test";
    private const string OtroAdminEmail = "admin2-it13655@ot.test";
    private const string GestorEmail = "gestor-it13655@ot.test";

    private static readonly Guid OtTenant = new("13655000-0000-4000-8000-0000000000a1");
    private static readonly Guid Office = new("13655000-0000-4000-8000-0000000000b1");
    private static readonly Guid AdminId = new("13655000-0000-4000-8000-000000000001");
    private static readonly Guid OtroAdminId = new("13655000-0000-4000-8000-000000000002");
    private static readonly Guid GestorId = new("13655000-0000-4000-8000-000000000003");

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private Guid _otAdminRole;
    private Guid _gestorRole;

    private async Task SeedAsync()
    {
        await using (var ctx = NewContext())
        {
            ctx.Tenants.Add(TenantSeed.New(OtTenant, "IT-OT-13655", false, null));
            await ctx.SaveChangesAsync(Ct);
        }

        await using var cn = await Fixture.OpenConnectionAsync();
        await ExecAsync(cn, $"""
            INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code)
            VALUES ('{Office}', 'IT13655', 'OT integración 13655', '05', '05001');
            INSERT INTO admin.transit_office_profiles (tenant_id, transit_office_id) VALUES ('{OtTenant}', '{Office}');
            INSERT INTO security.roles (code, name, is_system, is_active, target_entity_type, product_code, tenant_id)
            VALUES ('ot_admin', 'Administrador OT', true, true, 'TRANSIT_OFFICE', 'tramites', NULL);
            """);
        await ExecAsync(cn, EmbeddedDdl.LoadUp("137-HU13638-slugs-ot.sql"));
        await ExecAsync(cn, EmbeddedDdl.LoadUp("139-HU13641-permisos-enforced.sql"));
        await ExecAsync(cn, EmbeddedDdl.LoadUp("147-HU13655-rol-gestor-ot.sql"));

        await using (var ctx = NewContext())
        {
            _otAdminRole = await ctx.Roles.Where(r => r.Code == "ot_admin").Select(r => r.Id).SingleAsync(Ct);
            _gestorRole = await ctx.Roles.Where(r => r.Code == "gestor_tramites_ot").Select(r => r.Id).SingleAsync(Ct);
            AddUser(ctx, AdminId, AdminEmail, _otAdminRole);
            AddUser(ctx, OtroAdminId, OtroAdminEmail, _otAdminRole);
            AddUser(ctx, GestorId, GestorEmail, _gestorRole);
            await ctx.SaveChangesAsync(Ct);
        }
    }

    private static void AddUser(Flit.Infrastructure.Persistence.FlitDbContext ctx, Guid id, string email, Guid roleId)
    {
        var now = DateTimeOffset.UtcNow;
        ctx.Users.Add(new User { Id = id, Email = email, DisplayName = email, Status = "active", HomeTenantId = OtTenant, CreatedAt = now });
        ctx.UserCredentials.Add(new UserCredential { Id = Guid.CreateVersion7(), UserId = id, PasswordHash = "hash", CreatedAt = now });
        ctx.UserRoleAssignments.Add(new UserRoleAssignment
        {
            Id = Guid.CreateVersion7(), TenantId = OtTenant, UserId = id, RoleId = roleId, AssignedAt = now, CreatedAt = now,
        });
    }

    private static async Task ExecAsync(NpgsqlConnection cn, string sql)
    {
        await using var cmd = new NpgsqlCommand(sql, cn);
        await cmd.ExecuteNonQueryAsync(Ct);
    }

    private async Task<Guid> PermissionIdAsync(string slug)
    {
        await using var ctx = NewContext();
        return await ctx.RbacActions.Where(a => a.Slug == slug).Select(a => a.Id).SingleAsync(Ct);
    }

    private async Task AllowAsync(Guid userId, string slug)
    {
        var permission = await PermissionIdAsync(slug);
        await using var ctx = NewContext();
        await ctx.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO security.user_permission_overrides (tenant_id, user_id, product_code, permission_id, effect, granted_by)
            VALUES ({OtTenant}, {userId}, 'tramites', {permission}, 'allow', {AdminId})
            """, Ct);
    }

    /// <summary>Login real: snapshot del usuario desde Postgres y emisión del JWT heredado.</summary>
    private async Task<string> LoginAsync(string email)
    {
        await using var db = NewContext();
        var user = (await new AuthUserRepository(db, new UserRoleAssignmentRepository(db)).FindByEmailAsync(email, Ct))!;
        var issuer = new RsaJwtTokenIssuer(
            new JwtKeyMaterial { SigningKey = new RsaSecurityKey(RSA.Create(2048)), Issuer = "https://api.flit.co", Audience = "flit-api" },
            Options.Create(new JwtSettings()));
        return issuer.IssueToken(
            user.UserId, user.Email, user.TenantId, user.TenantName, user.TenantTaxId, user.EntityType, user.TenantType,
            user.IsGroupParent, user.ActiveRoles, user.PermissionSlugs, "flit").Token;
    }

    private async Task<HttpResponseMessage> SendAsync(string email, HttpMethod method, string url, object? body = null, bool otFlag = false)
    {
        var token = await LoginAsync(email);
        await using var factory = new MarcaBlancaApiFactory(Fixture);
        await using var configured = factory.WithWebHostBuilder(b => b.ConfigureAppConfiguration((_, c) =>
            c.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Security:OtPermissionEnforcement:Enabled"] = otFlag ? "true" : "false",
            })));
        using var client = configured.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var request = new HttpRequestMessage(method, url);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        var response = await client.SendAsync(request, Ct);
        await response.Content.LoadIntoBufferAsync(Ct);
        return response;
    }

    private static async Task<string> BodyAsync(HttpResponseMessage response) => await response.Content.ReadAsStringAsync(Ct);

    [PostgresFact]
    public async Task Token_DelGestorOt_TraeSoloSusPermisosBase_YEsDeOrganismo()
    {
        await SeedAsync();

        var jwt = new JsonWebToken(await LoginAsync(GestorEmail));

        jwt.GetClaim("entity_type").Value.Should().Be("TRANSIT_OFFICE");
        jwt.Claims.Where(c => c.Type == "role").Select(c => c.Value).Should().Equal("gestor_tramites_ot");
        jwt.Claims.Where(c => c.Type == "permissions").Select(c => c.Value).Should()
            .BeEquivalentTo("ot.tramites.manage", "ot.reportes.manage");
    }

    [PostgresFact]
    public async Task FlagEncendido_GestorOt_AbreLaBandeja_YPrelacionSoloConElPermisoIndividual()
    {
        await SeedAsync();

        using (var bandeja = await SendAsync(GestorEmail, HttpMethod.Get, "/api/v1/admin/ot/client-procedures", otFlag: true))
        {
            bandeja.StatusCode.Should().NotBe(HttpStatusCode.Forbidden, await BodyAsync(bandeja));
            bandeja.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized);
        }

        using (var sinPermiso = await SendAsync(GestorEmail, HttpMethod.Get, "/api/v1/admin/ot/document-precedence", otFlag: true))
        {
            sinPermiso.StatusCode.Should().Be(HttpStatusCode.Forbidden);
            (await BodyAsync(sinPermiso)).Should().Contain("OT_PERMISSION_REQUIRED");
        }

        await AllowAsync(GestorId, "ot.documentos.manage");

        using var conPermiso = await SendAsync(GestorEmail, HttpMethod.Get, "/api/v1/admin/ot/document-precedence", otFlag: true);
        conPermiso.StatusCode.Should().NotBe(HttpStatusCode.Forbidden, await BodyAsync(conPermiso));
        conPermiso.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized);
    }

    [PostgresFact]
    public async Task FB9_AdminOt_EditaAlGestorOtDeSuOrganismo_PeroNoAOtroAdministrador()
    {
        await SeedAsync();
        // Los permisos OT solo se otorgan con su exigencia encendida (NOT_ENFORCED si no, regla de F-B).
        var documentos = await PermissionIdAsync("ot.documentos.manage");

        using (var gestor = await SendAsync(AdminEmail, HttpMethod.Put,
                   $"/api/v1/security/users/{GestorId}/permission-overrides/{documentos}", new { effect = "allow" }, otFlag: true))
        {
            gestor.StatusCode.Should().Be(HttpStatusCode.OK, await BodyAsync(gestor));
        }

        using var admin = await SendAsync(AdminEmail, HttpMethod.Put,
            $"/api/v1/security/users/{OtroAdminId}/permission-overrides/{documentos}", new { effect = "allow" }, otFlag: true);
        admin.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await BodyAsync(admin)).Should().Contain("TARGET_IS_ADMIN");

        await using var ctx = NewContext();
        (await ctx.Database.SqlQuery<int>($"""
            SELECT count(*)::int AS "Value" FROM security.user_permission_overrides
             WHERE user_id = {GestorId} AND permission_id = {documentos} AND effect = 'allow'
            """).SingleAsync(Ct)).Should().Be(1);
    }

    [PostgresFact]
    public async Task AdminOt_InvitaConElRolGestorOt_YLeCambiaElRol()
    {
        await SeedAsync();
        const string email = "nuevo-gestor-it13655@ot.test";

        using (var invite = await SendAsync(AdminEmail, HttpMethod.Post, "/api/v1/admin/ot/users/invite",
                   new { email, fullName = "Nuevo Gestor", roleIds = new[] { _gestorRole } }))
        {
            invite.StatusCode.Should().Be(HttpStatusCode.Created, await BodyAsync(invite));
        }

        await using (var ctx = NewContext())
        {
            var invitation = await ctx.UserInvitations.AsNoTracking().SingleAsync(i => i.Email == email, Ct);
            invitation.TenantId.Should().Be(OtTenant);
            invitation.RoleId.Should().Be(_gestorRole);
        }

        using (var aAdmin = await SendAsync(AdminEmail, HttpMethod.Put, $"/api/v1/security/users/{GestorId}/role", new { roleId = _otAdminRole }))
        {
            aAdmin.StatusCode.Should().Be(HttpStatusCode.OK, await BodyAsync(aAdmin));
        }

        using (var aGestor = await SendAsync(AdminEmail, HttpMethod.Put, $"/api/v1/security/users/{GestorId}/role", new { roleId = _gestorRole }))
        {
            aGestor.StatusCode.Should().Be(HttpStatusCode.OK, await BodyAsync(aGestor));
        }

        await using var check = NewContext();
        (await check.UserRoleAssignments.Where(a => a.UserId == GestorId && a.DeletedAt == null).Select(a => a.RoleId).SingleAsync(Ct))
            .Should().Be(_gestorRole);
    }

    [PostgresFact]
    public async Task AdminOt_InvitarSinRol_Responde400RoleRequired_YNoCreaLaInvitacion()
    {
        await SeedAsync();
        const string email = "sin-rol-admin-it13655@ot.test";

        // El rol es obligatorio también para el Admin OT: ya no se asigna Administrador OT por defecto.
        using (var sinRol = await SendAsync(AdminEmail, HttpMethod.Post, "/api/v1/admin/ot/users/invite",
                   new { email, fullName = "Sin rol" }))
        {
            sinRol.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            var body = await BodyAsync(sinRol);
            body.Should().Contain("ROLE_REQUIRED");
            body.Should().Contain("Elige el rol de la persona.");
        }

        using (var vacio = await SendAsync(AdminEmail, HttpMethod.Post, "/api/v1/admin/ot/users/invite",
                   new { email, fullName = "Sin rol", roleIds = Array.Empty<Guid>() }))
        {
            vacio.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            (await BodyAsync(vacio)).Should().Contain("ROLE_REQUIRED");
        }

        await using var ctx = NewContext();
        (await ctx.UserInvitations.AsNoTracking().AnyAsync(i => i.Email == email, Ct)).Should().BeFalse();
    }

    [PostgresFact]
    public async Task FlagApagado_GestorOtSinUsuarios_NoEntraAUsuarios_NiIntegracion()
    {
        await SeedAsync();

        foreach (var url in new[] { "/api/v1/admin/ot/users", "/api/v1/admin/ot/webhooks" })
        {
            using var response = await SendAsync(GestorEmail, HttpMethod.Get, url);
            response.StatusCode.Should().Be(HttpStatusCode.Forbidden, url);
            (await BodyAsync(response)).Should().Contain("OT_PERMISSION_REQUIRED");
        }

        using var invite = await SendAsync(GestorEmail, HttpMethod.Post, "/api/v1/admin/ot/users/invite",
            new { email = "escalada-it13655@ot.test", fullName = "Escalada" });
        invite.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        using var admin = await SendAsync(AdminEmail, HttpMethod.Get, "/api/v1/admin/ot/users");
        admin.StatusCode.Should().Be(HttpStatusCode.OK, "el Admin OT conserva Usuarios con el flag apagado");
    }

    [PostgresFact]
    public async Task GestorOtConUsuariosDelegado_InvitaGestores_PeroNoAdministradores()
    {
        await SeedAsync();
        await AllowAsync(GestorId, "ot.usuarios.manage");

        using (var sinRol = await SendAsync(GestorEmail, HttpMethod.Post, "/api/v1/admin/ot/users/invite",
                   new { email = "sin-rol-it13655@ot.test", fullName = "Sin rol" }))
        {
            sinRol.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            (await BodyAsync(sinRol)).Should().Contain("ROLE_REQUIRED");
        }

        using (var admin = await SendAsync(GestorEmail, HttpMethod.Post, "/api/v1/admin/ot/users/invite",
                   new { email = "admin-nuevo-it13655@ot.test", fullName = "Admin", roleIds = new[] { _otAdminRole } }))
        {
            admin.StatusCode.Should().Be(HttpStatusCode.Forbidden);
            (await BodyAsync(admin)).Should().Contain("ADMIN_ROLE_NOT_ASSIGNABLE");
        }

        using (var editarAdmin = await SendAsync(GestorEmail, HttpMethod.Patch, $"/api/v1/admin/ot/users/{AdminId}",
                   new { displayName = "Otro nombre", rowVersion = 0 }))
        {
            editarAdmin.StatusCode.Should().Be(HttpStatusCode.Forbidden);
            (await BodyAsync(editarAdmin)).Should().Contain("TARGET_IS_ADMIN");
        }

        using var gestor = await SendAsync(GestorEmail, HttpMethod.Post, "/api/v1/admin/ot/users/invite",
            new { email = "gestor-nuevo-it13655@ot.test", fullName = "Gestor", roleIds = new[] { _gestorRole } });
        gestor.StatusCode.Should().Be(HttpStatusCode.Created, await BodyAsync(gestor));
    }
}
