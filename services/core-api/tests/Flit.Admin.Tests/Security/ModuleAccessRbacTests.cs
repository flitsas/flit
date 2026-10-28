using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Entities.Security;
using Flit.Infrastructure.Persistence.Repositories;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Flit.Admin.Tests.Security;

/// <summary>
/// HU #10664 — acceso a módulos por RBAC puro. Tras eliminar <c>tenant_module_grants</c>, el acceso a
/// módulos se gobierna únicamente por los roles: el constructor de roles SuperAdmin
/// (<c>includeAll=true</c>) ve todos los módulos activos (transversal), y el caller tenant
/// (<c>includeAll=false</c>) solo ve los módulos cuyos slugs están en sus permisos. Se ejercita
/// <see cref="SecurityModuleRepository.ListAccessibleAsync"/> contra Postgres real.
/// </summary>
public sealed class ModuleAccessRbacTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public ModuleAccessRbacTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    // ── includeAll=true (constructor de roles SuperAdmin) → todos los módulos activos, transversal ──

    [Fact]
    public async Task ListAccessibleAsync_IncludeAll_ReturnsAllActiveModules_Transversal()
    {
        await using var db = CreateDbContext();
        var suffix = Guid.NewGuid().ToString("N")[..8];

        var moduleA = await SeedModuleAsync(db, $"HU10664-A-{suffix}");
        var moduleB = await SeedModuleAsync(db, $"HU10664-B-{suffix}");
        try
        {
            var repo = new SecurityModuleRepository(db);

            var result = await repo.ListAccessibleAsync(
                permissionSlugs: [],
                includeAll: true,
                productCode: null,
                ct: TestContext.Current.CancellationToken);

            var ids = result.Select(m => m.Id).ToList();
            ids.Should().Contain(moduleA.Id);
            ids.Should().Contain(moduleB.Id,
                "con includeAll=true (SuperAdmin) todos los módulos activos son visibles, sin habilitación por empresa");
        }
        finally
        {
            await SoftDeleteSeedAsync(db, moduleA.Id, moduleB.Id);
        }
    }

    // ── includeAll=false (caller tenant) → solo módulos cuyos slugs están en sus permisos ──────

    [Fact]
    public async Task ListAccessibleAsync_TenantCaller_ReturnsOnlyModulesWithGrantedSlugs()
    {
        await using var db = CreateDbContext();
        var suffix = Guid.NewGuid().ToString("N")[..8];

        var visible = await SeedModuleAsync(db, $"HU10664-VIS-{suffix}");
        var hidden = await SeedModuleAsync(db, $"HU10664-HID-{suffix}");
        try
        {
            var repo = new SecurityModuleRepository(db);

            var result = await repo.ListAccessibleAsync(
                permissionSlugs: [visible.ActionSlug],
                includeAll: false,
                productCode: null,
                ct: TestContext.Current.CancellationToken);

            var ids = result.Select(m => m.Id).ToList();
            ids.Should().Contain(visible.Id,
                "el módulo cuyo slug está en los permisos del caller debe verse");
            ids.Should().NotContain(hidden.Id,
                "un módulo cuyo slug no está en los permisos del caller no debe verse (RBAC puro, sin grants por empresa)");
        }
        finally
        {
            await SoftDeleteSeedAsync(db, visible.Id, hidden.Id);
        }
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private FlitDbContext CreateDbContext() =>
        _factory.Services.CreateScope().ServiceProvider.GetRequiredService<FlitDbContext>();

    private static async Task<SeededModule> SeedModuleAsync(FlitDbContext db, string code)
    {
        var module = new SecurityModule
        {
            Id = Guid.NewGuid(),
            Code = code,
            Name = $"Módulo de prueba {code}",
            SortOrder = 1,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        db.SecurityModules.Add(module);

        var action = new RbacAction
        {
            Id = Guid.NewGuid(),
            ModuleId = module.Id,
            Slug = $"{code}.read".ToLowerInvariant(),
            Name = $"Leer {code}",
            Action = "READ",
            HttpMethod = "GET",
            RoutePattern = $"/api/v1/{code}",
            Scope = "tenant",
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        db.RbacActions.Add(action);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        return new SeededModule(module.Id, module.Code, action.Slug);
    }

    /// <summary>Bug #14010: estos módulos de prueba no pueden quedar en el catálogo RBAC compartido.</summary>
    private static async Task SoftDeleteSeedAsync(FlitDbContext db, params Guid[] moduleIds)
    {
        var now = DateTimeOffset.UtcNow;
        var modules = await db.SecurityModules.Where(m => moduleIds.Contains(m.Id)).ToListAsync(TestContext.Current.CancellationToken);
        foreach (var module in modules)
        {
            module.IsActive = false;
            module.DeletedAt = now;
        }

        var actions = await db.RbacActions.Where(a => moduleIds.Contains(a.ModuleId)).ToListAsync(TestContext.Current.CancellationToken);
        foreach (var action in actions)
        {
            action.IsActive = false;
            action.DeletedAt = now;
        }

        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private sealed record SeededModule(Guid Id, string Code, string ActionSlug);
}
