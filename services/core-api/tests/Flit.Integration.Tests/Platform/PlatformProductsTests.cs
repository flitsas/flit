using System.Text;
using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Entities.Platform;
using Flit.Infrastructure.Persistence.Repositories.Platform;
using Flit.Integration.Tests.Postgres;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace Flit.Integration.Tests.Platform;

/// <summary>
/// HU #12958 (B-03, ADR-0063) — schema <c>platform</c> contra PostgreSQL real: catálogo sembrado,
/// <c>tramites</c> encendido para las empresas existentes (backfill) y para las nuevas (disparador),
/// y el repositorio que enciende o apaga con auditoría en <c>admin.tenant_config_audit_logs</c>.
/// </summary>
public sealed class PlatformProductsTests(PostgresDatabaseFixture fixture) : PostgresTestBase(fixture)
{
    private const string DdlResource = "Flit.Infrastructure.Persistence.Sql.Ddl.119-HU12958-platform-products.sql";

    private async Task SeedTenantAsync(Guid? id = null, string code = "IT-LONE")
    {
        await using var ctx = NewContext();
        ctx.Tenants.Add(TenantSeed.Lone(id, code));
        await ctx.SaveChangesAsync();
    }

    private static string LoadDdl()
    {
        using var stream = typeof(FlitDbContext).Assembly.GetManifestResourceStream(DdlResource)!;
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    // ── AC1 — catálogo ─────────────────────────────────────────────────────────────

    [PostgresFact]
    public async Task AC1_ElCatalogoTraeLosCincoProductosEnElOrdenDelContrato()
    {
        await using var ctx = NewContext();
        var products = await new ProductCatalogRepository(ctx).ListAsync();

        products.Select(p => p.Code).Should().Equal("plataforma", "tramites", "comparendos", "diagnostico", "demo");
        products.Should().OnlyContain(p => p.IsActive);
        products.Single(p => p.Code == "tramites").Name.Should().Be("Trámites");
    }

    // ── AC2 — tramites encendido para todas las empresas ──────────────────────────

    [PostgresFact]
    public async Task AC2_EmpresaNueva_NaceConTramitesEncendido()
    {
        await SeedTenantAsync();

        await using var ctx = NewContext();
        var rows = await ctx.Set<TenantProductEntity>().AsNoTracking()
            .Where(r => r.TenantId == TenantSeed.LoneId).ToListAsync();

        rows.Should().ContainSingle();
        rows[0].ProductCode.Should().Be("tramites");
        rows[0].Enabled.Should().BeTrue();
        rows[0].UpdatedBy.Should().BeNull("lo encendió el disparador, no un usuario");
    }

    [PostgresFact]
    public async Task AC2_Backfill_EnciendeTramitesAEmpresasQueNoLoTenian_YEsIdempotente()
    {
        // Simula una empresa anterior a la migración: sin disparador, nace sin filas.
        await using (var ctx = NewContext())
        {
            await ctx.Database.ExecuteSqlRawAsync("ALTER TABLE identity.tenants DISABLE TRIGGER tr_tenants_default_products");
        }

        try
        {
            await SeedTenantAsync();
        }
        finally
        {
            await using var ctx = NewContext();
            await ctx.Database.ExecuteSqlRawAsync("ALTER TABLE identity.tenants ENABLE TRIGGER tr_tenants_default_products");
        }

        await using (var ctx = NewContext())
        {
            (await ctx.Set<TenantProductEntity>().CountAsync(r => r.TenantId == TenantSeed.LoneId)).Should().Be(0);

            // Correr el DDL dos veces: la segunda no duplica ni falla. Comando Npgsql directo porque
            // ExecuteSqlRaw lee las llaves del regex ({1,39}) como parámetros de formato.
            await ctx.Database.OpenConnectionAsync();
            for (var i = 0; i < 2; i++)
            {
                await using var cmd = ctx.Database.GetDbConnection().CreateCommand();
                cmd.CommandText = LoadDdl();
                await cmd.ExecuteNonQueryAsync();
            }
        }

        await using var check = NewContext();
        var rows = await check.Set<TenantProductEntity>().AsNoTracking()
            .Where(r => r.TenantId == TenantSeed.LoneId).ToListAsync();
        rows.Should().ContainSingle(r => r.ProductCode == "tramites" && r.Enabled);
        (await check.Set<ProductEntity>().CountAsync()).Should().Be(5);
    }

    // ── AC3 — encender o apagar con auditoría ─────────────────────────────────────

    // changedBy va nulo: tenant_config_audit_logs.changed_by tiene FK a identity.users. La atribución
    // al usuario la cubre SetTenantProductEnabledHandlerTests.
    [PostgresFact]
    public async Task AC3_EncenderUnProductoNuevo_CreaLaFilaYAuditaEnabledYNotes()
    {
        await SeedTenantAsync();

        await using (var ctx = NewContext())
        {
            var change = await new TenantProductRepository(ctx)
                .SetAsync(TenantSeed.LoneId, "comparendos", enabled: true, notes: "Piloto", changedBy: null);

            change.Changed.Should().BeTrue();
            change.Current.Enabled.Should().BeTrue();
        }

        await using var check = NewContext();
        var audit = await check.TenantConfigAuditLogs.AsNoTracking()
            .Where(a => a.TenantId == TenantSeed.LoneId && a.EntityName == "TenantProduct")
            .OrderBy(a => a.FieldName).ToListAsync();

        audit.Select(a => a.FieldName).Should().Equal("enabled", "notes");
        audit.Should().OnlyContain(a => a.Operation == "create" && a.TargetEntityType == "TENANT_PRODUCT");
        audit[0].NewValue.Should().Be("true");
        audit[1].NewValue.Should().Be("\"Piloto\"");
    }

    [PostgresFact]
    public async Task AC3_ApagarTramites_AuditaElCambioYRepetirNoEscribeNada()
    {
        await SeedTenantAsync();

        await using (var ctx = NewContext())
        {
            var repo = new TenantProductRepository(ctx);
            (await repo.SetAsync(TenantSeed.LoneId, "tramites", enabled: false, notes: null, changedBy: null)).Changed.Should().BeTrue();
        }

        await using (var ctx = NewContext())
        {
            var repeat = await new TenantProductRepository(ctx).SetAsync(TenantSeed.LoneId, "tramites", enabled: false, notes: null, changedBy: null);
            repeat.Changed.Should().BeFalse();
        }

        await using var check = NewContext();
        var audit = await check.TenantConfigAuditLogs.AsNoTracking()
            .Where(a => a.TenantId == TenantSeed.LoneId && a.EntityName == "TenantProduct").ToListAsync();

        // El disparador dejó notas de la migración; apagar sin notas también las borra: dos campos.
        audit.Should().HaveCount(2);
        var enabled = audit.Single(a => a.FieldName == "enabled");
        enabled.OldValue.Should().Be("true");
        enabled.NewValue.Should().Be("false");
        enabled.Operation.Should().Be("update");
        audit.Single(a => a.FieldName == "notes").NewValue.Should().BeNull();
    }

    // ── Restricciones del motor ───────────────────────────────────────────────────

    [PostgresFact]
    public async Task Plataforma_NoSeHabilitaPorEmpresa_ElMotorLoRechaza()
    {
        await SeedTenantAsync();

        await using var ctx = NewContext();
        ctx.Set<TenantProductEntity>().Add(new TenantProductEntity { TenantId = TenantSeed.LoneId, ProductCode = "plataforma", Enabled = true });

        var act = () => ctx.SaveChangesAsync();
        (await act.Should().ThrowAsync<DbUpdateException>())
            .WithInnerException<PostgresException>()
            .Which.ConstraintName.Should().Be("ck_tenant_products_not_plataforma");
    }

    [PostgresFact]
    public async Task ProductoInexistente_ElMotorLoRechazaPorFk()
    {
        await SeedTenantAsync();

        await using var ctx = NewContext();
        var act = () => new TenantProductRepository(ctx).SetAsync(TenantSeed.LoneId, "flotas", enabled: true, notes: null, changedBy: null);

        (await act.Should().ThrowAsync<DbUpdateException>())
            .WithInnerException<PostgresException>()
            .Which.ConstraintName.Should().Be("fk_tenant_products_products");
    }
}
