using System.Reflection;
using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Entities.Security;
using Flit.Infrastructure.Security;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Flit.Infrastructure.Tests.Security;

/// <summary>
/// HU #12205 (Feature #12201, ADR-0056-generacion-documental-standalone) —
/// <c>DevelopmentAuthSeeder.SeedGeneracionDocumentalPermissionsAsync</c>.
/// </summary>
/// <remarks>
/// Uso de ejemplo:
/// <code>
/// await DevelopmentAuthSeeder.SeedGeneracionDocumentalPermissionsAsync(db, ct);
/// // → módulo "generacion-documental" (SortOrder 10) + permisos .read/.generate
/// //   concedidos a SuperAdmin y AdminCompany.
/// </code>
/// Cubre los AC «El módulo se siembra en una base ya sembrada» y «El seeder es idempotente».
/// </remarks>
public sealed class SeedGeneracionDocumentalPermissionsTests
{
    private const string ModuleCode = "generacion-documental";
    private const string ReadSlug = "generacion-documental.read";
    private const string GenerateSlug = "generacion-documental.generate";

    private static FlitDbContext NewDb(string name)
    {
        var options = new DbContextOptionsBuilder<FlitDbContext>()
            .UseInMemoryDatabase(name)
            .Options;
        return new FlitDbContext(options);
    }

    /// <summary>
    /// Deja la base en el estado real de DEV/QA/PDN: el módulo <c>dashboard</c> ya existe, así que
    /// <c>SeedBaseModulesAsync</c> haría early-return y no crearía nada nuevo.
    /// </summary>
    private static async Task SeedBaseAlreadyDoneAsync(FlitDbContext db, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;

        db.SecurityModules.Add(new SecurityModule
        {
            Id = Guid.CreateVersion7(),
            Code = "dashboard",
            Name = "Dashboard",
            SortOrder = 1,
            IsActive = true,
            CreatedAt = now,
        });

        db.Roles.Add(new Role
        {
            Id = Guid.CreateVersion7(),
            Code = "SuperAdmin",
            Name = "Super Administrador",
            TargetEntityType = "COMPANY",
            IsSystem = true,
            IsActive = true,
            CreatedAt = now,
        });
        db.Roles.Add(new Role
        {
            Id = Guid.CreateVersion7(),
            Code = "AdminCompany",
            Name = "Administrador de Compañía",
            TargetEntityType = "COMPANY",
            IsSystem = true,
            IsActive = true,
            CreatedAt = now,
        });

        await db.SaveChangesAsync(ct);
    }

    private static async Task<bool> HasGrantAsync(FlitDbContext db, string roleCode, string slug, CancellationToken ct)
    {
        var role = await db.Roles.FirstAsync(r => r.Code == roleCode, ct);
        var action = await db.RbacActions.FirstAsync(a => a.Slug == slug, ct);
        return await db.RoleGrants.AnyAsync(g => g.RoleId == role.Id && g.PermissionId == action.Id, ct);
    }

    // ── AC1 — el módulo se siembra en una base donde SeedBaseModulesAsync ya hizo early-return ──

    [Fact]
    public async Task Seed_SobreBaseYaSembrada_CreaModuloConSortOrder10()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = NewDb(nameof(Seed_SobreBaseYaSembrada_CreaModuloConSortOrder10));
        await SeedBaseAlreadyDoneAsync(db, ct);

        // El array de SeedBaseModulesAsync no contiene el módulo, y además ese método ya hizo
        // early-return: si el seeder nuevo no existiera, el módulo nunca aparecería.
        (await db.SecurityModules.AnyAsync(m => m.Code == ModuleCode, ct)).Should().BeFalse();

        await DevelopmentAuthSeeder.SeedGeneracionDocumentalPermissionsAsync(db, ct);

        var module = await db.SecurityModules.SingleAsync(m => m.Code == ModuleCode, ct);
        module.SortOrder.Should().Be((short)10);
        module.Name.Should().Be("Generación documental");
        module.IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task Seed_SobreBaseYaSembrada_CreaLosDosPermisosBajoElModulo()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = NewDb(nameof(Seed_SobreBaseYaSembrada_CreaLosDosPermisosBajoElModulo));
        await SeedBaseAlreadyDoneAsync(db, ct);

        await DevelopmentAuthSeeder.SeedGeneracionDocumentalPermissionsAsync(db, ct);

        var module = await db.SecurityModules.SingleAsync(m => m.Code == ModuleCode, ct);
        var actions = await db.RbacActions
            .Where(a => a.Slug == ReadSlug || a.Slug == GenerateSlug)
            .ToListAsync(ct);

        actions.Should().HaveCount(2);
        actions.Should().OnlyContain(a => a.ModuleId == module.Id && a.IsActive);
        actions.Single(a => a.Slug == ReadSlug).HttpMethod.Should().Be("GET");
        actions.Single(a => a.Slug == GenerateSlug).HttpMethod.Should().Be("POST");
    }

    [Theory]
    [InlineData("SuperAdmin", ReadSlug)]
    [InlineData("SuperAdmin", GenerateSlug)]
    [InlineData("AdminCompany", ReadSlug)]
    [InlineData("AdminCompany", GenerateSlug)]
    public async Task Seed_OtorgaAmbosPermisos_ASuperAdminYAdminCompany(string roleCode, string slug)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = NewDb("grants-" + roleCode + "-" + slug);
        await SeedBaseAlreadyDoneAsync(db, ct);

        await DevelopmentAuthSeeder.SeedGeneracionDocumentalPermissionsAsync(db, ct);

        (await HasGrantAsync(db, roleCode, slug, ct)).Should().BeTrue();
    }

    // ── AC2 — idempotencia en ambos sentidos ────────────────────────────────────────

    [Fact]
    public async Task Seed_DosArranques_NoDuplicaModuloPermisosNiGrantsNiLanza()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = NewDb(nameof(Seed_DosArranques_NoDuplicaModuloPermisosNiGrantsNiLanza));
        await SeedBaseAlreadyDoneAsync(db, ct);

        await DevelopmentAuthSeeder.SeedGeneracionDocumentalPermissionsAsync(db, ct);

        var act = async () => await DevelopmentAuthSeeder.SeedGeneracionDocumentalPermissionsAsync(db, ct);
        await act.Should().NotThrowAsync();

        (await db.SecurityModules.CountAsync(m => m.Code == ModuleCode, ct)).Should().Be(1);
        (await db.RbacActions.CountAsync(a => a.Slug == ReadSlug, ct)).Should().Be(1);
        (await db.RbacActions.CountAsync(a => a.Slug == GenerateSlug, ct)).Should().Be(1);

        var actionIds = await db.RbacActions
            .Where(a => a.Slug == ReadSlug || a.Slug == GenerateSlug)
            .Select(a => a.Id)
            .ToListAsync(ct);
        var roleIds = await db.Roles
            .Where(r => r.Code == "SuperAdmin" || r.Code == "AdminCompany")
            .Select(r => r.Id)
            .ToListAsync(ct);

        // 2 permisos × 2 roles = 4 grants, ni uno más tras el segundo arranque.
        (await db.RoleGrants
            .CountAsync(g => actionIds.Contains(g.PermissionId) && roleIds.Contains(g.RoleId), ct))
            .Should().Be(4);
    }

    [Fact]
    public async Task Seed_SinRolesEnLaBase_NoLanzaYCreaModuloYPermisos()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = NewDb(nameof(Seed_SinRolesEnLaBase_NoLanzaYCreaModuloYPermisos));

        var act = async () => await DevelopmentAuthSeeder.SeedGeneracionDocumentalPermissionsAsync(db, ct);
        await act.Should().NotThrowAsync();

        (await db.SecurityModules.CountAsync(m => m.Code == ModuleCode, ct)).Should().Be(1);
        (await db.RbacActions.CountAsync(a => a.Slug == ReadSlug || a.Slug == GenerateSlug, ct)).Should().Be(2);
        (await db.RoleGrants.CountAsync(ct)).Should().Be(0);
    }

    // ── AC1 (última cláusula) — el array de SeedBaseModulesAsync no fue modificado ───

    [Fact]
    public async Task SeedBaseModulesAsync_NoDeclaraElModuloDeGeneracionDocumental()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = NewDb(nameof(SeedBaseModulesAsync_NoDeclaraElModuloDeGeneracionDocumental));

        // Base VACÍA: SeedBaseModulesAsync sí ejecuta su array completo. Si alguien hubiera agregado
        // el módulo/permisos ahí (justo lo que el AC prohíbe), este test lo detecta.
        var method = typeof(DevelopmentAuthSeeder).GetMethod(
            "SeedBaseModulesAsync",
            BindingFlags.NonPublic | BindingFlags.Static)!;
        await (Task)method.Invoke(null, new object[] { db, ct })!;

        (await db.SecurityModules.AnyAsync(m => m.Code == ModuleCode, ct)).Should().BeFalse();
        (await db.RbacActions.AnyAsync(a => a.Slug.StartsWith("generacion-documental."), ct)).Should().BeFalse();

        // Y el array sigue siendo el de los 7 módulos base.
        (await db.SecurityModules.CountAsync(ct)).Should().Be(7);
    }

    // ── Contrato — autorización por permiso, nunca por policy de grupo ──────────────

    [Fact]
    public void NingunaRutaDelModuloUsaSuperAdminPolicy()
    {
        // El AC exige que ninguna ruta del grupo esté protegida por AdminAuthorization.SuperAdminPolicy.
        // Se verifica sobre el código fuente porque los endpoints son de HU-02 y aún no existen: en
        // cuanto lleguen, este test falla si alguien copia el patrón de AdminImprontasEndpoints.
        var repoRoot = FindRepoRoot();
        var apiDir = Path.Combine(repoRoot, "services", "core-api", "src", "Flit.Api");
        var files = Directory.Exists(apiDir)
            ? Directory.GetFiles(apiDir, "*GeneracionDocumental*.cs", SearchOption.AllDirectories)
            : Array.Empty<string>();

        foreach (var file in files)
        {
            File.ReadAllText(file).Should().NotContain(
                "SuperAdminPolicy",
                "las rutas de generación documental se autorizan con RequirePermission(\"generacion-documental.*\")");
        }
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, ".git")))
            dir = dir.Parent;
        return dir?.FullName ?? AppContext.BaseDirectory;
    }
}
