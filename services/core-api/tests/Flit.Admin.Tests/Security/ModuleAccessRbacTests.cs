using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Entities.Security;
using Flit.Infrastructure.Persistence.Repositories;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
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

        var repo = new SecurityModuleRepository(db);

        var result = await repo.ListAccessibleAsync(
            permissionSlugs: [],
            includeAll: true,
            ct: TestContext.Current.CancellationToken);

        var ids = result.Select(m => m.Id).ToList();
        ids.Should().Contain(moduleA.Id);
        ids.Should().Contain(moduleB.Id,
            "con includeAll=true (SuperAdmin) todos los módulos activos son visibles, sin habilitación por empresa");
    }

    // ── includeAll=false (caller tenant) → solo módulos cuyos slugs están en sus permisos ──────

    [Fact]
    public async Task ListAccessibleAsync_TenantCaller_ReturnsOnlyModulesWithGrantedSlugs()
    {
        await using var db = CreateDbContext();
        var suffix = Guid.NewGuid().ToString("N")[..8];

        var visible = await SeedModuleAsync(db, $"HU10664-VIS-{suffix}");
        var hidden = await SeedModuleAsync(db, $"HU10664-HID-{suffix}");

        var repo = new SecurityModuleRepository(db);

        var result = await repo.ListAccessibleAsync(
            permissionSlugs: [visible.ActionSlug],
            includeAll: false,
            ct: TestContext.Current.CancellationToken);

        var ids = result.Select(m => m.Id).ToList();
        ids.Should().Contain(visible.Id,
            "el módulo cuyo slug está en los permisos del caller debe verse");
        ids.Should().NotContain(hidden.Id,
            "un módulo cuyo slug no está en los permisos del caller no debe verse (RBAC puro, sin grants por empresa)");
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

    private sealed record SeededModule(Guid Id, string Code, string ActionSlug);
}
