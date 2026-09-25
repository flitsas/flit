using System.Text;
using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Entities.Identity;
using Flit.Infrastructure.Persistence.Entities.Security;
using Flit.Infrastructure.Persistence.Repositories;
using Flit.Integration.Tests.Postgres;
using Flit.Modules.Security.Application.UserRoles;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace Flit.Integration.Tests.Platform;

/// <summary>
/// HU #12964 (B-04, decisión D1) contra PostgreSQL real: un rol por usuario en cada producto, el espejo
/// transitorio AdminCompany ⇒ admin_tramites, la regla de permisos del mismo producto, el llenado inicial
/// del DDL 120 y el login, que suma los permisos de los dos roles.
/// </summary>
public sealed class PlatformRbacTests(PostgresDatabaseFixture fixture) : PostgresTestBase(fixture)
{
    private const string DdlResource = "Flit.Infrastructure.Persistence.Sql.Ddl.120-HU12964-rbac-por-producto.sql";

    private static readonly Guid UserId = new("aaaaaaaa-0000-4000-8000-000000000001");
    private static readonly Guid ActorId = new("aaaaaaaa-0000-4000-8000-000000000002");
    private static readonly Guid AdminCompanyId = new("bbbbbbbb-0000-4000-8000-000000000001");
    private static readonly Guid AdminTramitesId = new("bbbbbbbb-0000-4000-8000-000000000002");
    private static readonly Guid RadicadorId = new("bbbbbbbb-0000-4000-8000-000000000003");
    private static readonly Guid SuperAdminId = new("bbbbbbbb-0000-4000-8000-000000000004");
    private static readonly Guid PlatformPermissionId = new("cccccccc-0000-4000-8000-000000000001");
    private static readonly Guid TramitesPermissionId = new("cccccccc-0000-4000-8000-000000000002");

    private async Task SeedAsync()
    {
        await using var ctx = NewContext();
        ctx.Tenants.Add(TenantSeed.Lone());
        foreach (var (id, email) in new[] { (UserId, "it-rbac@empresa.test"), (ActorId, "it-actor@empresa.test") })
        {
            ctx.Users.Add(new User { Id = id, Email = email, DisplayName = email, Status = "active", HomeTenantId = TenantSeed.LoneId, CreatedAt = DateTimeOffset.UtcNow });
        }

        ctx.Roles.AddRange(
            NewRole(AdminCompanyId, "AdminCompany", "plataforma", isSystem: true),
            NewRole(AdminTramitesId, "admin_tramites", "tramites", isSystem: true),
            NewRole(RadicadorId, "Radicador", "tramites", isSystem: false),
            NewRole(SuperAdminId, "SuperAdmin", "plataforma", isSystem: true));

        var platformModule = new SecurityModule { Id = Guid.CreateVersion7(), Code = "it-usuarios", Name = "Usuarios IT", ProductCode = "plataforma", CreatedAt = DateTimeOffset.UtcNow };
        var tramitesModule = new SecurityModule { Id = Guid.CreateVersion7(), Code = "it-reportes", Name = "Reportes IT", ProductCode = "tramites", CreatedAt = DateTimeOffset.UtcNow };
        ctx.SecurityModules.AddRange(platformModule, tramitesModule);
        ctx.RbacActions.AddRange(
            new RbacAction { Id = PlatformPermissionId, ModuleId = platformModule.Id, Slug = "it.usuarios.manage", Name = "Usuarios", HttpMethod = "GET", RoutePattern = "/it/usuarios", CreatedAt = DateTimeOffset.UtcNow },
            new RbacAction { Id = TramitesPermissionId, ModuleId = tramitesModule.Id, Slug = "it.reportes.read", Name = "Reportes", HttpMethod = "GET", RoutePattern = "/it/reportes", CreatedAt = DateTimeOffset.UtcNow });
        await ctx.SaveChangesAsync();

        ctx.RoleGrants.AddRange(
            new RoleGrant { Id = Guid.CreateVersion7(), RoleId = AdminCompanyId, PermissionId = PlatformPermissionId, CreatedAt = DateTimeOffset.UtcNow },
            new RoleGrant { Id = Guid.CreateVersion7(), RoleId = AdminTramitesId, PermissionId = TramitesPermissionId, CreatedAt = DateTimeOffset.UtcNow });
        await ctx.SaveChangesAsync();
    }

    private static Role NewRole(Guid id, string code, string product, bool isSystem) => new()
    {
        Id = id, Code = code, Name = code, TargetEntityType = "COMPANY", ProductCode = product, IsSystem = isSystem, IsActive = true, CreatedAt = DateTimeOffset.UtcNow,
    };

    private async Task AssignAsync(Guid roleId)
    {
        await using var ctx = NewContext();
        ctx.UserRoleAssignments.Add(new UserRoleAssignment
        {
            Id = Guid.CreateVersion7(), TenantId = TenantSeed.LoneId, UserId = UserId, RoleId = roleId,
            AssignedAt = DateTimeOffset.UtcNow, CreatedAt = DateTimeOffset.UtcNow,
        });
        await ctx.SaveChangesAsync();
    }

    private async Task<List<string>> ActiveRoleCodesAsync()
    {
        await using var ctx = NewContext();
        return await (
            from a in ctx.UserRoleAssignments.AsNoTracking()
            join r in ctx.Roles.AsNoTracking() on a.RoleId equals r.Id
            where a.UserId == UserId && a.TenantId == TenantSeed.LoneId && a.DeletedAt == null
            orderby r.Code
            select r.Code).ToListAsync();
    }

    private async Task ExecAsync(string sql)
    {
        await using var ctx = NewContext();
        await ctx.Database.OpenConnectionAsync();
        await using var cmd = ctx.Database.GetDbConnection().CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync();
    }

    private static string LoadDdl()
    {
        using var stream = typeof(FlitDbContext).Assembly.GetManifestResourceStream(DdlResource)!;
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    // ── Espejo AdminCompany ⇒ admin_tramites ──────────────────────────────────────

    [PostgresFact]
    public async Task AsignarAdminCompany_AgregaAdminTramites()
    {
        await SeedAsync();

        await AssignAsync(AdminCompanyId);

        (await ActiveRoleCodesAsync()).Should().Equal("AdminCompany", "admin_tramites");
    }

    [PostgresFact]
    public async Task QuitarAdminCompany_CierraAdminTramites()
    {
        await SeedAsync();
        await AssignAsync(AdminCompanyId);

        await using (var ctx = NewContext())
        {
            await new RemoveRoleAssignmentHandler(new UserRoleAssignmentRepository(ctx))
                .HandleAsync(UserId, TenantSeed.LoneId, AdminCompanyId, ActorId, TestContext.Current.CancellationToken);
        }

        (await ActiveRoleCodesAsync()).Should().BeEmpty();
    }

    [PostgresFact]
    public async Task CambiarDeAdminCompanyARadicador_QuedaSoloRadicador()
    {
        await SeedAsync();
        await AssignAsync(AdminCompanyId);

        await using (var ctx = NewContext())
        {
            await new AssignRoleHandler(new UserRoleAssignmentRepository(ctx)).HandleAsync(
                new AssignRoleCommand(TenantSeed.LoneId, UserId, RadicadorId, ActorId), TestContext.Current.CancellationToken);
        }

        (await ActiveRoleCodesAsync()).Should().Equal("Radicador");
    }

    [PostgresFact]
    public async Task CambiarDeRadicadorAAdminCompany_QuedanAdminCompanyYAdminTramites()
    {
        await SeedAsync();
        await AssignAsync(RadicadorId);

        await using (var ctx = NewContext())
        {
            await new AssignRoleHandler(new UserRoleAssignmentRepository(ctx)).HandleAsync(
                new AssignRoleCommand(TenantSeed.LoneId, UserId, AdminCompanyId, ActorId), TestContext.Current.CancellationToken);
        }

        (await ActiveRoleCodesAsync()).Should().Equal("AdminCompany", "admin_tramites");
    }

    // ── Un rol por usuario en cada producto ───────────────────────────────────────

    [PostgresFact]
    public async Task DosRolesDelMismoProducto_ElMotorLoRechaza()
    {
        await SeedAsync();
        await AssignAsync(RadicadorId);

        var act = () => AssignAsync(AdminTramitesId);

        (await act.Should().ThrowAsync<DbUpdateException>())
            .WithInnerException<PostgresException>()
            .Which.ConstraintName.Should().Be("uq_ura_active_user_tenant_product");
    }

    // ── Permisos del mismo producto ───────────────────────────────────────────────

    [PostgresFact]
    public async Task PermisoDeTramitesEnRolDePlataforma_ElMotorLoRechaza()
    {
        await SeedAsync();

        await using var ctx = NewContext();
        ctx.RoleGrants.Add(new RoleGrant { Id = Guid.CreateVersion7(), RoleId = AdminCompanyId, PermissionId = TramitesPermissionId, CreatedAt = DateTimeOffset.UtcNow });

        var act = () => ctx.SaveChangesAsync();
        (await act.Should().ThrowAsync<DbUpdateException>())
            .WithInnerException<PostgresException>()
            .Which.ConstraintName.Should().Be("ck_role_permissions_same_product");
    }

    [PostgresFact]
    public async Task SuperAdmin_PuedeTenerPermisosDeCualquierProducto()
    {
        await SeedAsync();

        await using var ctx = NewContext();
        ctx.RoleGrants.AddRange(
            new RoleGrant { Id = Guid.CreateVersion7(), RoleId = SuperAdminId, PermissionId = PlatformPermissionId, CreatedAt = DateTimeOffset.UtcNow },
            new RoleGrant { Id = Guid.CreateVersion7(), RoleId = SuperAdminId, PermissionId = TramitesPermissionId, CreatedAt = DateTimeOffset.UtcNow });

        await ctx.SaveChangesAsync();
    }

    // ── Llenado inicial del DDL 120 ───────────────────────────────────────────────

    [PostgresFact]
    public async Task Migracion_PasaLosPermisosDeTramitesYAsignaAdminTramitesAlAdminCompanyExistente()
    {
        await SeedAsync();

        // Estado anterior a la B-04: AdminCompany con un permiso de Trámites y sin admin_tramites.
        await ExecAsync("""
            ALTER TABLE security.role_permissions DISABLE TRIGGER tr_role_permissions_same_product;
            ALTER TABLE security.user_role_assignments DISABLE TRIGGER tr_ura_mirror_admin_tramites;
            """);
        try
        {
            await using (var ctx = NewContext())
            {
                ctx.RoleGrants.Add(new RoleGrant { Id = Guid.CreateVersion7(), RoleId = AdminCompanyId, PermissionId = TramitesPermissionId, CreatedAt = DateTimeOffset.UtcNow });
                await ctx.SaveChangesAsync();
            }

            await AssignAsync(AdminCompanyId);
            (await ActiveRoleCodesAsync()).Should().Equal("AdminCompany");
        }
        finally
        {
            await ExecAsync("""
                ALTER TABLE security.role_permissions ENABLE TRIGGER tr_role_permissions_same_product;
                ALTER TABLE security.user_role_assignments ENABLE TRIGGER tr_ura_mirror_admin_tramites;
                """);
        }

        // Correr el DDL dos veces: la segunda no cambia nada.
        await ExecAsync(LoadDdl());
        await ExecAsync(LoadDdl());

        (await ActiveRoleCodesAsync()).Should().Equal("AdminCompany", "admin_tramites");
        await using var check = NewContext();
        var adminCompanyGrants = await check.RoleGrants.AsNoTracking().Where(g => g.RoleId == AdminCompanyId).Select(g => g.PermissionId).ToListAsync();
        var adminTramitesGrants = await check.RoleGrants.AsNoTracking().Where(g => g.RoleId == AdminTramitesId).Select(g => g.PermissionId).ToListAsync();
        adminCompanyGrants.Should().Equal(PlatformPermissionId);
        adminTramitesGrants.Should().Equal(TramitesPermissionId);
    }

    // ── Login ─────────────────────────────────────────────────────────────────────

    [PostgresFact]
    public async Task Login_AdminCompany_SumaLosPermisosDeLosDosProductos_ConAdminCompanyPrimero()
    {
        await SeedAsync();
        await AssignAsync(AdminCompanyId);
        await using (var ctx = NewContext())
        {
            ctx.UserCredentials.Add(new UserCredential { Id = Guid.CreateVersion7(), UserId = UserId, PasswordHash = "hash", CreatedAt = DateTimeOffset.UtcNow });
            await ctx.SaveChangesAsync();
        }

        await using var db = NewContext();
        var snapshot = await new AuthUserRepository(db, new UserRoleAssignmentRepository(db))
            .FindByEmailAsync("it-rbac@empresa.test", TestContext.Current.CancellationToken);

        snapshot.Should().NotBeNull();
        snapshot!.ActiveRoles.Select(r => r.Code).Should().Equal("AdminCompany", "admin_tramites");
        snapshot.PermissionSlugs.Should().BeEquivalentTo("it.usuarios.manage", "it.reportes.read");
        snapshot.TenantId.Should().Be(TenantSeed.LoneId);
    }
}
