using Flit.Infrastructure.Persistence.Entities.Identity;
using Flit.Infrastructure.Persistence.Entities.Platform;
using Flit.Infrastructure.Persistence.Entities.Security;
using Flit.Infrastructure.Persistence.Repositories.Platform;
using Flit.Integration.Tests.Postgres;
using Flit.Modules.Platform.Application.Access;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Flit.Integration.Tests.Platform;

/// <summary>
/// HU #12965 (B-05) — matriz del ADR-0063 contra PostgreSQL real, con el resolutor y su almacén reales:
/// empresa con y sin producto, usuario con y sin rol, producto encendido o apagado, empresa hija con la
/// cabeza apagada, roles de otro producto y plataforma. El SuperAdmin no pasa por el resolutor (§2.1).
/// </summary>
public sealed class ProductAccessStoreTests(PostgresDatabaseFixture fixture) : PostgresTestBase(fixture)
{
    private static readonly Guid UserId = new("dddddddd-0000-4000-8000-000000000001");
    private static readonly Guid RadicadorId = new("dddddddd-0000-4000-8000-000000000002");
    private static readonly Guid AdminCompanyId = new("dddddddd-0000-4000-8000-000000000003");
    private static readonly Guid AdminTramitesId = new("dddddddd-0000-4000-8000-000000000004");

    /// <summary>Cabeza y una hija; el usuario pertenece a la hija con <paramref name="roleId"/>.</summary>
    private async Task SeedAsync(Guid? roleId)
    {
        await using var ctx = NewContext();
        ctx.Tenants.Add(TenantSeed.GroupParent());
        ctx.Tenants.Add(TenantSeed.ChildOf(TenantSeed.ParentId));
        ctx.Users.Add(new User { Id = UserId, Email = "it-access@empresa.test", DisplayName = "IT", Status = "active", HomeTenantId = TenantSeed.ChildId, CreatedAt = DateTimeOffset.UtcNow });
        ctx.Roles.AddRange(
            new Role { Id = RadicadorId, Code = "Radicador", Name = "Radicador", ProductCode = "tramites", CreatedAt = DateTimeOffset.UtcNow },
            new Role { Id = AdminCompanyId, Code = "AdminCompany", Name = "AdminCompany", ProductCode = "plataforma", IsSystem = true, CreatedAt = DateTimeOffset.UtcNow },
            new Role { Id = AdminTramitesId, Code = "admin_tramites", Name = "admin_tramites", ProductCode = "tramites", IsSystem = true, CreatedAt = DateTimeOffset.UtcNow });
        var module = new SecurityModule { Id = Guid.CreateVersion7(), Code = "it-tramites", Name = "Trámites IT", ProductCode = "tramites", CreatedAt = DateTimeOffset.UtcNow };
        ctx.SecurityModules.Add(module);
        var permission = new RbacAction { Id = Guid.CreateVersion7(), ModuleId = module.Id, Slug = "it.tramites.read", Name = "Leer", HttpMethod = "GET", RoutePattern = "/it", CreatedAt = DateTimeOffset.UtcNow };
        ctx.RbacActions.Add(permission);
        await ctx.SaveChangesAsync();

        ctx.RoleGrants.Add(new RoleGrant { Id = Guid.CreateVersion7(), RoleId = RadicadorId, PermissionId = permission.Id, CreatedAt = DateTimeOffset.UtcNow });
        if (roleId is { } r)
        {
            ctx.UserRoleAssignments.Add(new UserRoleAssignment { Id = Guid.CreateVersion7(), TenantId = TenantSeed.ChildId, UserId = UserId, RoleId = r, AssignedAt = DateTimeOffset.UtcNow, CreatedAt = DateTimeOffset.UtcNow });
        }

        await ctx.SaveChangesAsync();
    }

    private async Task SetTramitesAsync(Guid tenantId, bool enabled)
    {
        await using var ctx = NewContext();
        var row = await ctx.Set<TenantProductEntity>().FirstAsync(x => x.TenantId == tenantId && x.ProductCode == "tramites");
        row.Enabled = enabled;
        await ctx.SaveChangesAsync();
    }

    private async Task<Flit.Modules.Security.Application.Products.ProductAccess> ResolveAsync(string product)
    {
        await using var ctx = NewContext();
        return await new ProductAccessResolver(new ProductAccessStore(ctx))
            .ResolveAsync(UserId, TenantSeed.ChildId, product, TestContext.Current.CancellationToken);
    }

    [PostgresFact]
    public async Task EmpresaYCabezaConTramites_UsuarioConRol_Accede()
    {
        await SeedAsync(RadicadorId);

        var access = await ResolveAsync("tramites");

        access.ProductEnabled.Should().BeTrue();
        access.Roles.Select(r => r.Code).Should().Equal("Radicador");
        access.Permissions.Should().Equal("it.tramites.read");
    }

    [PostgresFact]
    public async Task UsuarioSinRolEnElProducto_ProductoEncendidoPeroSinRoles()
    {
        await SeedAsync(roleId: null);

        var access = await ResolveAsync("tramites");

        access.ProductEnabled.Should().BeTrue();
        access.Roles.Should().BeEmpty();
    }

    [PostgresFact]
    public async Task ProductoApagadoParaLaEmpresa_NoEstaEncendido()
    {
        await SeedAsync(RadicadorId);
        await SetTramitesAsync(TenantSeed.ChildId, enabled: false);

        (await ResolveAsync("tramites")).ProductEnabled.Should().BeFalse();
    }

    [PostgresFact]
    public async Task CabezaApagada_LaHijaNoTieneElProducto()
    {
        await SeedAsync(RadicadorId);
        await SetTramitesAsync(TenantSeed.ParentId, enabled: false);

        (await ResolveAsync("tramites")).ProductEnabled.Should().BeFalse();
    }

    [PostgresFact]
    public async Task EmpresaSinFila_ProductoApagado()
    {
        await SeedAsync(RadicadorId);

        (await ResolveAsync("comparendos")).ProductEnabled.Should().BeFalse();
    }

    [PostgresFact]
    public async Task AdminCompany_EnTramitesViajaAdminTramites_YEnPlataformaAdminCompany()
    {
        await SeedAsync(AdminCompanyId); // el espejo agrega admin_tramites

        (await ResolveAsync("tramites")).Roles.Select(r => r.Code).Should().Equal("admin_tramites");
        var hub = await ResolveAsync("plataforma");
        hub.ProductEnabled.Should().BeTrue();
        hub.Roles.Select(r => r.Code).Should().Equal("AdminCompany");
    }
}
