using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Entities.Admin;
using Flit.Infrastructure.Persistence.Entities.Catalogs;
using Flit.Infrastructure.Persistence.Entities.Identity;
using Flit.Infrastructure.Persistence.Entities.Security;
using Flit.Infrastructure.Persistence.Repositories;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Flit.Admin.Tests.Security;

/// <summary>
/// HU #10504 — scoping de módulos por tipo de tenant en el constructor de roles SuperAdmin
/// (<c>GET /api/v1/security/modules?targetEntityType=COMPANY|TRANSIT_OFFICE</c>). Ejercita
/// <see cref="SecurityModuleRepository.ListAccessibleAsync"/> contra Postgres real (mismo patrón
/// que <see cref="MultiRoleUserAssignmentEndpointsTests"/> / <see cref="SecurityEndpointsHardeningTests"/>),
/// porque el filtro usa <c>TenantModuleGrants</c> + <c>TransitOfficeProfiles</c> vía LINQ que debe
/// traducirse a SQL real, no InMemory.
/// </summary>
public sealed class ModuleTenantScopingTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public ModuleTenantScopingTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    // ── (a) TRANSIT_OFFICE excluye un módulo con grants solo hacia COMPANY ──────

    [Fact]
    public async Task ListAccessibleAsync_TransitOffice_ExcludesModuleGrantedOnlyToCompany()
    {
        await using var db = CreateDbContext();
        var suffix = Guid.NewGuid().ToString("N")[..8];

        var companyTenant = await SeedTenantAsync(db, suffix, isOt: false);
        var otTenant = await SeedTenantAsync(db, suffix, isOt: true);

        var companyOnlyModule = await SeedModuleWithGrantsAsync(
            db, $"HU10504-CO-{suffix}", [companyTenant.Id]);

        var repo = new SecurityModuleRepository(db);

        var result = await repo.ListAccessibleAsync(
            permissionSlugs: [],
            includeAll: true,
            tenantId: null,
            targetEntityType: "TRANSIT_OFFICE",
            ct: TestContext.Current.CancellationToken);

        result.Select(m => m.Id).Should().NotContain(companyOnlyModule.Id,
            "un módulo con grant únicamente hacia una Compañía no debe verse en el checklist de roles OT");

        // Verifica también el sentido contrario para no dejar pasar un filtro invertido.
        var companyResult = await repo.ListAccessibleAsync(
            permissionSlugs: [],
            includeAll: true,
            tenantId: null,
            targetEntityType: "COMPANY",
            ct: TestContext.Current.CancellationToken);

        companyResult.Select(m => m.Id).Should().Contain(companyOnlyModule.Id,
            "el mismo módulo sí debe verse en el checklist de roles COMPANY");

        _ = otTenant; // usado únicamente para dejar sembrado el tipo de tenant contrastante
    }

    // ── (b) módulo con grants hacia AMBOS tipos aparece en ambos checklists ─────

    [Fact]
    public async Task ListAccessibleAsync_ModuleGrantedToBothTypes_AppearsInBothChecklists()
    {
        await using var db = CreateDbContext();
        var suffix = Guid.NewGuid().ToString("N")[..8];

        var companyTenant = await SeedTenantAsync(db, suffix, isOt: false);
        var otTenant = await SeedTenantAsync(db, suffix, isOt: true);

        var bothModule = await SeedModuleWithGrantsAsync(
            db, $"HU10504-BOTH-{suffix}", [companyTenant.Id, otTenant.Id]);

        var repo = new SecurityModuleRepository(db);

        var otResult = await repo.ListAccessibleAsync(
            [], true, null, "TRANSIT_OFFICE", TestContext.Current.CancellationToken);
        var companyResult = await repo.ListAccessibleAsync(
            [], true, null, "COMPANY", TestContext.Current.CancellationToken);

        otResult.Select(m => m.Id).Should().Contain(bothModule.Id);
        companyResult.Select(m => m.Id).Should().Contain(bothModule.Id);
    }

    // ── (c) módulo sin ningún grant aparece para ambos tipos (módulo "global") ──

    [Fact]
    public async Task ListAccessibleAsync_ModuleWithoutAnyGrant_AppearsForBothTypes()
    {
        await using var db = CreateDbContext();
        var suffix = Guid.NewGuid().ToString("N")[..8];

        var noGrantModule = await SeedModuleWithGrantsAsync(
            db, $"HU10504-NOGRANT-{suffix}", []);

        var repo = new SecurityModuleRepository(db);

        var otResult = await repo.ListAccessibleAsync(
            [], true, null, "TRANSIT_OFFICE", TestContext.Current.CancellationToken);
        var companyResult = await repo.ListAccessibleAsync(
            [], true, null, "COMPANY", TestContext.Current.CancellationToken);

        otResult.Select(m => m.Id).Should().Contain(noGrantModule.Id,
            "un módulo sin scope definido (sin TenantModuleGrant) es global y debe verse siempre");
        companyResult.Select(m => m.Id).Should().Contain(noGrantModule.Id);
    }

    // ── (d) includeAll=true sin targetEntityType (null) → sin cambios (regresión) ──

    [Fact]
    public async Task ListAccessibleAsync_IncludeAllWithoutTargetEntityType_ReturnsAllModules_Unfiltered()
    {
        await using var db = CreateDbContext();
        var suffix = Guid.NewGuid().ToString("N")[..8];

        var companyTenant = await SeedTenantAsync(db, suffix, isOt: false);
        var otTenant = await SeedTenantAsync(db, suffix, isOt: true);

        var companyOnlyModule = await SeedModuleWithGrantsAsync(db, $"HU10504-D-CO-{suffix}", [companyTenant.Id]);
        var otOnlyModule = await SeedModuleWithGrantsAsync(db, $"HU10504-D-OT-{suffix}", [otTenant.Id]);
        var noGrantModule = await SeedModuleWithGrantsAsync(db, $"HU10504-D-NG-{suffix}", []);

        var repo = new SecurityModuleRepository(db);

        var result = await repo.ListAccessibleAsync(
            permissionSlugs: [],
            includeAll: true,
            tenantId: null,
            targetEntityType: null,
            ct: TestContext.Current.CancellationToken);

        var ids = result.Select(m => m.Id).ToList();
        ids.Should().Contain(companyOnlyModule.Id,
            "la pantalla 'Módulos y Permisos' (sin targetEntityType) sigue viendo TODOS los módulos");
        ids.Should().Contain(otOnlyModule.Id);
        ids.Should().Contain(noGrantModule.Id);
    }

    // ── (e) branch !includeAll (tenant normal, HU10163) sin cambios de comportamiento ──

    [Fact]
    public async Task ListAccessibleAsync_TenantCaller_TargetEntityTypeParamIsIgnored_NoRegression()
    {
        await using var db = CreateDbContext();
        var suffix = Guid.NewGuid().ToString("N")[..8];

        var companyTenant = await SeedTenantAsync(db, suffix, isOt: false);

        var grantedModule = await SeedModuleWithGrantsAsync(
            db, $"HU10504-E-GRANTED-{suffix}", [companyTenant.Id]);
        var notGrantedModule = await SeedModuleWithGrantsAsync(
            db, $"HU10504-E-NOTGRANTED-{suffix}", []); // grant a OTRO tenant → tenant normal no debe verlo
        var otherTenant = await SeedTenantAsync(db, suffix, isOt: false);
        await GrantModuleAsync(db, notGrantedModule.Id, otherTenant.Id);

        var permissionSlugs = new[]
        {
            grantedModule.Actions[0].ActionSlug,
            notGrantedModule.Actions[0].ActionSlug,
        };

        var repo = new SecurityModuleRepository(db);

        // El comportamiento HU10163 (opt-in por tenant) no debe cambiar sin importar el valor de
        // targetEntityType, porque ese parámetro solo aplica cuando includeAll=true.
        var withTargetEntityType = await repo.ListAccessibleAsync(
            permissionSlugs, includeAll: false, tenantId: companyTenant.Id,
            targetEntityType: "TRANSIT_OFFICE", ct: TestContext.Current.CancellationToken);

        var withoutTargetEntityType = await repo.ListAccessibleAsync(
            permissionSlugs, includeAll: false, tenantId: companyTenant.Id,
            targetEntityType: null, ct: TestContext.Current.CancellationToken);

        var idsWith = withTargetEntityType.Select(m => m.Id).ToList();
        var idsWithout = withoutTargetEntityType.Select(m => m.Id).ToList();

        idsWith.Should().BeEquivalentTo(idsWithout,
            "targetEntityType no debe alterar en absoluto el branch !includeAll (HU10163)");
        idsWith.Should().Contain(grantedModule.Id);
        idsWith.Should().NotContain(notGrantedModule.Id,
            "el tenant llamante tiene al menos un grant propio, así que queda restringido a sus propios grants");
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private FlitDbContext CreateDbContext() =>
        _factory.Services.CreateScope().ServiceProvider.GetRequiredService<FlitDbContext>();

    private static async Task<Tenant> SeedTenantAsync(FlitDbContext db, string suffix, bool isOt)
    {
        var tenant = new Tenant
        {
            Id = Guid.NewGuid(),
            Code = $"HU10504-{(isOt ? "OT" : "CO")}-{Guid.NewGuid():N}"[..20],
            LegalName = $"Tenant de prueba HU10504 ({(isOt ? "OT" : "Compañía")}) {suffix}",
            TaxId = $"90{Random.Shared.Next(1000000, 9999999)}-{Random.Shared.Next(1, 9)}",
            TenantType = "RENTING",
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        db.Tenants.Add(tenant);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        if (isOt)
        {
            var office = new TransitOffice
            {
                Id = Guid.NewGuid(),
                Code = $"HU10504-{Guid.NewGuid():N}"[..10],
                Name = $"Organismo de prueba HU10504 {suffix}",
                DepartmentCode = "11",
                CityCode = "11001",
                IsActive = true,
            };
            db.TransitOffices.Add(office);
            // EF no conoce el FK transit_office_profiles -> transit_offices (solo existe en el
            // DDL crudo, sin HasOne/HasForeignKey en la configuración), así que no puede ordenar
            // los inserts por dependencia: hay que guardar el catálogo antes que el perfil.
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);

            db.TransitOfficeProfiles.Add(new TransitOfficeProfile
            {
                Id = Guid.NewGuid(),
                TenantId = tenant.Id,
                TransitOfficeId = office.Id,
                OperationMode = "dashboard",
                QuipuxReadOnly = false,
                CreatedAt = DateTimeOffset.UtcNow,
            });
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        return tenant;
    }

    private static async Task<SeededModule> SeedModuleWithGrantsAsync(
        FlitDbContext db, string codeSuffix, IReadOnlyList<Guid> grantTenantIds)
    {
        var module = new SecurityModule
        {
            Id = Guid.NewGuid(),
            Code = codeSuffix,
            Name = $"Módulo de prueba {codeSuffix}",
            SortOrder = 1,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        db.SecurityModules.Add(module);

        var action = new RbacAction
        {
            Id = Guid.NewGuid(),
            ModuleId = module.Id,
            Slug = $"{codeSuffix}.read".ToLowerInvariant(),
            Name = $"Leer {codeSuffix}",
            Action = "READ",
            HttpMethod = "GET",
            RoutePattern = $"/api/v1/{codeSuffix}",
            Scope = "tenant",
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        db.RbacActions.Add(action);

        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        foreach (var tenantId in grantTenantIds)
            await GrantModuleAsync(db, module.Id, tenantId);

        return new SeededModule(module.Id, module.Code, [new SeededAction(action.Id, action.Slug)]);
    }

    private static async Task GrantModuleAsync(FlitDbContext db, Guid moduleId, Guid tenantId)
    {
        db.TenantModuleGrants.Add(new TenantModuleGrant
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ModuleId = moduleId,
            GrantedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private sealed record SeededModule(Guid Id, string Code, IReadOnlyList<SeededAction> Actions);

    private sealed record SeededAction(Guid ActionId, string ActionSlug);
}
