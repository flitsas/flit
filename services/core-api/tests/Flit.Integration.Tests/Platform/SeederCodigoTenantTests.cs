using Flit.Infrastructure.Security;
using Flit.Integration.Tests.Postgres;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Flit.Integration.Tests.Platform;

/// <summary>
/// HU #13606 (Feature #13602, Épica #13517) — la siembra de desarrollo ya no depende del código de compañía. Desde la
/// HU #13604 el código es el consecutivo COMP-XXXX: el seeder identifica las compañías demo por NIT
/// (<see cref="DevelopmentAuthSeeder.DemoTenantTaxId"/>, <see cref="DevelopmentAuthSeeder.DemoEmpresaTenantTaxId"/>) y
/// deja que Postgres asigne el código al crearlas.
/// <para>
/// Uso de ejemplo: <c>await DevelopmentAuthSeeder.SeedAsync(db, hasher, new SeedSettings(true, true), ct)</c> sobre una
/// base ya recodificada → arranca sin error y no crea una segunda «Empresa Demo».
/// </para>
/// </summary>
public sealed class SeederCodigoTenantTests(PostgresDatabaseFixture fixture) : PostgresTestBase(fixture)
{
    private static readonly SeedSettings TodoEncendido = new(RbacCatalog: true, DemoData: true);

    /// <summary>
    /// El dev-seed 16 crea grants para dos compañías mock que siembra la migración <c>SeedMockCompanies</c>, pero el
    /// arnés trunca <c>identity.tenants</c> antes de cada prueba (no es catálogo). Se reponen con su id fijo; el código
    /// lo asigna la secuencia, como en cualquier alta.
    /// </summary>
    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();
        if (!PostgresAvailability.IsAvailable)
        {
            return;
        }

        await using var db = NewContext();
        await db.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO identity.tenants (id, legal_name, tax_id, tenant_type, is_active, created_at)
            VALUES ('0ad1c0de-0000-4000-8000-000000000001', 'Renting Andino S.A.S.', '900123456-1', 'RENTING', true, now()),
                   ('0ad1c0de-0000-4000-8000-000000000002', 'Concesionario AutoPacífico S.A.', '901234567-2', 'CONCESIONARIO', true, now())
            ON CONFLICT (id) DO NOTHING
            """,
            TestContext.Current.CancellationToken);
    }

    private async Task SembrarAsync()
    {
        await using var db = NewContext();
        await DevelopmentAuthSeeder.SeedAsync(db, new Argon2PasswordHasher(), TodoEncendido, TestContext.Current.CancellationToken);
    }

    // ── AC2 — base nueva ─────────────────────────────────────────────────────

    [PostgresFact]
    public async Task AC2_EnUnaBaseNuevaLasCompaniasDemoNacenConCodigoDeLaSecuencia()
    {
        await SembrarAsync();

        await using var db = NewContext();
        var ct = TestContext.Current.CancellationToken;
        var demos = await db.Tenants.AsNoTracking()
            .Where(t => t.TaxId == DevelopmentAuthSeeder.DemoTenantTaxId || t.TaxId == DevelopmentAuthSeeder.DemoEmpresaTenantTaxId)
            .ToListAsync(ct);

        demos.Should().HaveCount(2);
        demos.Should().OnlyContain(t => t.Code.StartsWith("COMP-") && t.LegacyCode == null);

        // Los usuarios semilla quedan asociados como siempre: el AdminCompany en la compañía demo.
        var empresa = demos.Single(t => t.TaxId == DevelopmentAuthSeeder.DemoEmpresaTenantTaxId);
        var admin = await db.Users.AsNoTracking().SingleAsync(u => u.Email == DevelopmentAuthSeeder.DemoAdminCompanyEmail, ct);
        (await db.UserRoleAssignments.AsNoTracking().AnyAsync(a => a.UserId == admin.Id && a.TenantId == empresa.Id && a.DeletedAt == null, ct))
            .Should().BeTrue();
    }

    // ── AC1 — base ya recodificada ───────────────────────────────────────────

    [PostgresFact]
    public async Task AC1_EnUnaBaseRecodificadaEncuentraLaCompaniaDemoYNoCreaOtra()
    {
        var ct = TestContext.Current.CancellationToken;
        await SembrarAsync();

        // Estado que deja la migración 136 en una base de dev existente: la compañía demo conserva su NIT, su código
        // pasa a COMP-XXXX y el anterior queda en legacy_code.
        await using (var db = NewContext())
        {
            await db.Database.ExecuteSqlRawAsync(
                "UPDATE identity.tenants SET legacy_code = 'EMPRESA_DEMO', code = 'COMP-9001' WHERE tax_id = {0}",
                [DevelopmentAuthSeeder.DemoEmpresaTenantTaxId], ct);
        }

        // Antes de la HU, este segundo arranque buscaba EMPRESA_DEMO por código, no lo encontraba e intentaba crear otra
        // compañía con el mismo NIT: uq_tenants_tax_id y la API no arrancaba.
        var arranque = async () => await SembrarAsync();
        await arranque.Should().NotThrowAsync();

        await using var check = NewContext();
        var empresas = await check.Tenants.AsNoTracking()
            .Where(t => t.TaxId == DevelopmentAuthSeeder.DemoEmpresaTenantTaxId).ToListAsync(ct);
        empresas.Should().ContainSingle().Which.Code.Should().Be("COMP-9001");
    }

    // ── AC3 — idempotencia ───────────────────────────────────────────────────

    [PostgresFact]
    public async Task AC3_SembrarDosVecesNoDuplicaTenantsNiAsignaciones()
    {
        var ct = TestContext.Current.CancellationToken;
        await SembrarAsync();

        int tenants, asignaciones;
        await using (var db = NewContext())
        {
            tenants = await db.Tenants.CountAsync(ct);
            asignaciones = await db.UserRoleAssignments.CountAsync(ct);
        }

        await SembrarAsync();

        await using var check = NewContext();
        (await check.Tenants.CountAsync(ct)).Should().Be(tenants);
        (await check.UserRoleAssignments.CountAsync(ct)).Should().Be(asignaciones);
    }
}
