using Flit.Integration.Tests.Postgres;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace Flit.Integration.Tests.Hierarchy;

/// <summary>
/// HU #12406 AC8 — la migración <c>20260910140000_HU12406_HeadTenantTypesAndParentSnapshot</c> contra
/// PostgreSQL real: sin poblado (ningún tipo ni <c>is_group_parent</c> cambia; todo trámite existente
/// queda con padre NULL), idempotente (reaplicar el DDL no falla ni duplica nada) y reversible
/// (<c>Down</c> deja la base como en #12323 —catálogo de tres tipos, trigger de 107— y <c>Up</c> la
/// vuelve a subir). Cada prueba termina con la efímera en la última migración para no contaminar a
/// las demás.
/// <para>
/// Uso de ejemplo: <c>await Migrator(ctx).MigrateAsync(Previous)</c> → columna ausente y catálogo de
/// tres → <c>MigrateAsync(Latest)</c> → columna presente y NULL, catálogo de cinco.
/// </para>
/// </summary>
public sealed class HeadTenantTypeMigrationTests(PostgresDatabaseFixture fixture) : PostgresTestBase(fixture)
{
    private const string Previous = "20260910130000_HU12323_HierarchySwitchesAndLinkAudit";
    private const string Latest = "20260910140000_HU12406_HeadTenantTypesAndParentSnapshot";

    private static IMigrator Migrator(DbContext ctx) => ctx.GetService<IMigrator>();

    [PostgresFact]
    public async Task AC8_Down_y_Up_dejan_los_tipos_intactos_y_todo_tramite_con_padre_nulo()
    {
        // Mundo real de F0: clientes con los tres tipos de siempre, sin cabezas, y trámites.
        await SeedWorldWithoutHeadsAsync();

        await using (var ctx = NewContext())
        {
            await Migrator(ctx).MigrateAsync(Previous);
            (await ColumnExistsAsync("tramites", "procedure_instances", "parent_tenant_id_at_creation")).Should().BeFalse();
            (await ConstraintExistsAsync("ck_tenants_group_parent_by_type")).Should().BeFalse();
            (await ConstraintDefAsync("ck_tenants_tenant_type")).Should().NotContain("CONCESION'").And.NotContain("MARCA_BLANCA");
            (await TriggerColumnsAsync("tr_tenants_hierarchy")).Should().BeEquivalentTo(["parent_tenant_id", "is_group_parent"], "Down restaura el trigger de 107");
            (await FunctionSourceAsync("identity", "trg_tenant_hierarchy_depth")).Should().NotContain("tenant_type");
        }

        await using (var ctx = NewContext())
        {
            await Migrator(ctx).MigrateAsync(Latest);
        }

        (await ColumnExistsAsync("tramites", "procedure_instances", "parent_tenant_id_at_creation")).Should().BeTrue();
        (await ConstraintExistsAsync("ck_tenants_group_parent_by_type")).Should().BeTrue();
        (await ConstraintDefAsync("ck_tenants_tenant_type")).Should().Contain("'CONCESION'").And.Contain("'MARCA_BLANCA'");
        (await TriggerColumnsAsync("tr_tenants_hierarchy")).Should().BeEquivalentTo(["parent_tenant_id", "is_group_parent", "tenant_type"]);

        await using var check = NewContext();
        var tenants = await check.Tenants.AsNoTracking().OrderBy(t => t.Code).ToListAsync();
        tenants.Select(t => (t.Code, t.TenantType, t.IsGroupParent)).Should().BeEquivalentTo(
        [
            ("IT-LONE", "CONCESIONARIO", false),
            ("IT-LONE-2", "RENTING", false),
            ("IT-LONE-3", "FLIT", false),
        ], "sin poblado: ningún tipo ni marca cambia");
        (await check.ProcedureInstances.AsNoTracking().CountAsync()).Should().BeGreaterThan(0);
        (await check.ProcedureInstances.AsNoTracking().AllAsync(p => p.ParentTenantIdAtCreation == null)).Should().BeTrue("sin poblado");
        (await check.Database.GetPendingMigrationsAsync()).Should().BeEmpty();
    }

    [PostgresFact]
    public async Task AC8_Reaplicar_el_DDL_es_idempotente()
    {
        await SeedWorldWithoutHeadsAsync();

        await using var ctx = NewContext();
        var ddl = Flit.Infrastructure.Persistence.Sql.EmbeddedDdl.LoadUp("109-HU12406-head-tenant-types-and-parent-snapshot.sql");

        var act = () => ctx.Database.ExecuteSqlRawAsync(ddl); // DDL embebido y fijo: sin parámetros, sin concatenación
        await act.Should().NotThrowAsync();
        await act.Should().NotThrowAsync("dos veces seguidas tampoco falla");

        (await ConstraintCountAsync("ck_tenants_tenant_type")).Should().Be(1);
        (await ConstraintCountAsync("ck_tenants_group_parent_by_type")).Should().Be(1);
        (await TriggerCountAsync("tr_tenants_hierarchy")).Should().Be(1);
        (await TriggerCountAsync("tr_procedure_instances_parent_snapshot_immutable")).Should().Be(1);
        (await ctx.Tenants.AsNoTracking().AllAsync(t => !t.IsGroupParent)).Should().BeTrue();
    }

    /// <summary>
    /// Precondición documentada en el DDL: si existiera una fila marcada cabeza con un tipo que no es
    /// de cabeza (imposible hoy: ninguna ruta marca cabezas), el Up falla con claridad y no deja nada
    /// a medias (DDL transaccional). Se fabrica ese estado a propósito y, corregido, el Up sube.
    /// </summary>
    [PostgresFact]
    public async Task AC8_Con_una_cabeza_marcada_sin_tipo_de_cabeza_el_Up_falla_completo_y_no_deja_nada_a_medias()
    {
        await using (var ctx = NewContext())
        {
            await Migrator(ctx).MigrateAsync(Previous);
            await ctx.Database.ExecuteSqlInterpolatedAsync(
                $"""
                INSERT INTO identity.tenants (id, code, legal_name, tax_id, tenant_type, is_active, created_at, parent_tenant_id, is_group_parent)
                VALUES ({TenantSeed.ParentId}, 'IT-PARENT', 'Cabeza sin tipo de cabeza', '900000000-1', 'CONCESIONARIO', true, now(), NULL, true)
                """);
        }

        try
        {
            await using (var ctx = NewContext())
            {
                var up = () => Migrator(ctx).MigrateAsync(Latest);
                var pg = (await up.Should().ThrowAsync<PostgresException>()).Which;
                pg.SqlState.Should().Be("23514");
                pg.ConstraintName.Should().Be("ck_tenants_group_parent_by_type");
            }

            (await ColumnExistsAsync("tramites", "procedure_instances", "parent_tenant_id_at_creation")).Should().BeFalse("la migración es una sola transacción: nada a medias");
            (await ConstraintDefAsync("ck_tenants_tenant_type")).Should().NotContain("MARCA_BLANCA");
        }
        finally
        {
            // Corrige la precondición (desmarca la cabeza) y sube la efímera a la última migración.
            await using var fix = NewContext();
            await fix.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE identity.tenants SET is_group_parent = false WHERE id = {TenantSeed.ParentId}");
            await Migrator(fix).MigrateAsync(Latest);
        }

        (await ConstraintExistsAsync("ck_tenants_group_parent_by_type")).Should().BeTrue();
    }

    /// <summary>El Down devuelve el catálogo a tres valores: con una cabeza declarada falla a propósito (no reescribe tipos).</summary>
    [PostgresFact]
    public async Task AC8_El_Down_con_una_cabeza_declarada_falla_en_vez_de_reescribir_su_tipo()
    {
        await using (var ctx = NewContext())
        {
            ctx.Tenants.Add(TenantSeed.GroupParent());
            await ctx.SaveChangesAsync();
        }

        await using (var ctx = NewContext())
        {
            var down = () => Migrator(ctx).MigrateAsync(Previous);
            var pg = (await down.Should().ThrowAsync<PostgresException>()).Which;
            pg.SqlState.Should().Be("23514");
            pg.ConstraintName.Should().Be("ck_tenants_tenant_type");
        }

        // Transaccional: sigue en la última migración, con todos los artefactos.
        (await ColumnExistsAsync("tramites", "procedure_instances", "parent_tenant_id_at_creation")).Should().BeTrue();
        (await ConstraintExistsAsync("ck_tenants_group_parent_by_type")).Should().BeTrue();
        await using var check = NewContext();
        (await check.Database.GetPendingMigrationsAsync()).Should().BeEmpty();
        (await check.Tenants.AsNoTracking().SingleAsync(t => t.Id == TenantSeed.ParentId)).TenantType.Should().Be("CONCESION");
    }

    // ── helpers ─────────────────────────────────────────────────────────────────

    private async Task SeedWorldWithoutHeadsAsync()
    {
        await using var ctx = NewContext();
        ctx.Tenants.Add(TenantSeed.Lone());
        ctx.Tenants.Add(TenantSeed.New(TenantSeed.ChildId, "IT-LONE-2", isGroupParent: false, parentId: null, "RENTING"));
        ctx.Tenants.Add(TenantSeed.New(TenantSeed.GrandchildId, "IT-LONE-3", isGroupParent: false, parentId: null, "FLIT"));
        await ctx.SaveChangesAsync();

        // Un trámite sembrado directo (sin comando): representa las filas previas a la HU.
        var type = await ctx.ProcedureTypes.AsNoTracking().OrderBy(t => t.Code).FirstAsync();
        var user = new Flit.Infrastructure.Persistence.Entities.Identity.User
        {
            Id = new("88888888-8888-4888-8888-888888888888"),
            Email = "it-migration@flit.test",
            DisplayName = "Migración",
            Status = "active",
            HomeTenantId = TenantSeed.LoneId,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        ctx.Users.Add(user);
        await ctx.SaveChangesAsync();

        ctx.ProcedureInstances.Add(new Flit.Tramites.Domain.Entities.ProcedureInstance
        {
            Id = Guid.NewGuid(),
            TenantId = TenantSeed.LoneId,
            ProcedureTypeId = type.Id,
            CreatedByUserId = user.Id,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await ctx.SaveChangesAsync();
    }

    private async Task<bool> ColumnExistsAsync(string schema, string table, string column)
    {
        await using var connection = await Fixture.OpenConnectionAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema = @s AND table_name = @t AND column_name = @c)",
            connection);
        cmd.Parameters.AddWithValue("s", schema);
        cmd.Parameters.AddWithValue("t", table);
        cmd.Parameters.AddWithValue("c", column);
        return (bool)(await cmd.ExecuteScalarAsync())!;
    }

    private async Task<bool> ConstraintExistsAsync(string name) => await ConstraintCountAsync(name) > 0;

    private async Task<long> ConstraintCountAsync(string name)
    {
        await using var connection = await Fixture.OpenConnectionAsync();
        await using var cmd = new NpgsqlCommand("SELECT count(*) FROM pg_constraint WHERE conname = @n", connection);
        cmd.Parameters.AddWithValue("n", name);
        return (long)(await cmd.ExecuteScalarAsync())!;
    }

    private async Task<string> ConstraintDefAsync(string name)
    {
        await using var connection = await Fixture.OpenConnectionAsync();
        await using var cmd = new NpgsqlCommand("SELECT pg_get_constraintdef(oid) FROM pg_constraint WHERE conname = @n", connection);
        cmd.Parameters.AddWithValue("n", name);
        return (string?)await cmd.ExecuteScalarAsync() ?? string.Empty;
    }

    private async Task<long> TriggerCountAsync(string name)
    {
        await using var connection = await Fixture.OpenConnectionAsync();
        await using var cmd = new NpgsqlCommand("SELECT count(*) FROM pg_trigger WHERE tgname = @n AND NOT tgisinternal", connection);
        cmd.Parameters.AddWithValue("n", name);
        return (long)(await cmd.ExecuteScalarAsync())!;
    }

    private async Task<string[]> TriggerColumnsAsync(string name)
    {
        await using var connection = await Fixture.OpenConnectionAsync();
        await using var cmd = new NpgsqlCommand(
            """
            SELECT string_agg(a.attname, ',' ORDER BY a.attnum)
              FROM pg_trigger t
              JOIN pg_attribute a ON a.attrelid = t.tgrelid AND a.attnum = ANY (t.tgattr)
             WHERE t.tgname = @n
            """, connection);
        cmd.Parameters.AddWithValue("n", name);
        return ((string?)await cmd.ExecuteScalarAsync())?.Split(',') ?? [];
    }

    private async Task<string> FunctionSourceAsync(string schema, string name)
    {
        await using var connection = await Fixture.OpenConnectionAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT p.prosrc FROM pg_proc p JOIN pg_namespace n ON n.oid = p.pronamespace WHERE n.nspname = @s AND p.proname = @n",
            connection);
        cmd.Parameters.AddWithValue("s", schema);
        cmd.Parameters.AddWithValue("n", name);
        return (string)(await cmd.ExecuteScalarAsync())!;
    }
}
