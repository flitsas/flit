using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Entities.Identity;
using Flit.Integration.Tests.Postgres;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Flit.Integration.Tests.Hierarchy;

/// <summary>
/// HU #12319 (AC3) — los invariantes de la jerarquía padre-hija (DDL 107, HU #12318) los fuerza el
/// MOTOR, no la aplicación: FK <c>fk_tenants_parent_tenant</c> (RESTRICT), CHECK
/// <c>ck_tenants_parent_not_self</c> y trigger <c>tr_tenants_hierarchy</c> (profundidad 2). Aquí se
/// afirma sobre <see cref="PostgresException.SqlState"/>, <see cref="PostgresException.ConstraintName"/>
/// y el texto del <c>RAISE EXCEPTION</c> del trigger, contra PostgreSQL real.
/// <para>
/// Uso de ejemplo: cada prueba parte de una base recién reseteada (<see cref="PostgresTestBase"/>),
/// siembra con <see cref="TenantSeed"/> a través del <see cref="FlitDbContext"/> real y captura la
/// <see cref="DbUpdateException"/> cuyo <c>InnerException</c> es la <see cref="PostgresException"/>.
/// </para>
/// </summary>
public sealed class TenantHierarchyConstraintsTests(PostgresDatabaseFixture fixture) : PostgresTestBase(fixture)
{
    private const string CheckViolation = "23514";
    private const string ForeignKeyViolation = "23503";

    // ── happy path ──────────────────────────────────────────────────────────────

    [PostgresFact]
    public async Task Padre_cabeza_de_grupo_e_hijo_colgado_se_persisten_y_se_leen()
    {
        await SeedAsync(TenantSeed.GroupParent());
        await SeedAsync(TenantSeed.ChildOf(TenantSeed.ParentId));

        await using var ctx = NewContext();
        var child = await ctx.Tenants.AsNoTracking().SingleAsync(t => t.Id == TenantSeed.ChildId);
        var parent = await ctx.Tenants.AsNoTracking().SingleAsync(t => t.Id == TenantSeed.ParentId);

        child.ParentTenantId.Should().Be(TenantSeed.ParentId);
        child.IsGroupParent.Should().BeFalse();
        parent.IsGroupParent.Should().BeTrue();
        parent.ParentTenantId.Should().BeNull();
    }

    // ── tr_tenants_hierarchy (23514) ────────────────────────────────────────────

    /// <summary>Regla (b): un hijo no puede ser a la vez cabeza de grupo.</summary>
    [PostgresFact]
    public async Task Hijo_marcado_cabeza_de_grupo_es_rechazado_por_el_trigger()
    {
        await SeedAsync(TenantSeed.GroupParent());

        var pg = await ExpectPostgresErrorAsync(() =>
            SeedAsync(TenantSeed.New(TenantSeed.ChildId, "IT-CHILD", isGroupParent: true, parentId: TenantSeed.ParentId)));

        pg.SqlState.Should().Be(CheckViolation);
        pg.MessageText.Should().Contain("una cabeza de grupo (is_group_parent) no puede tener padre (parent_tenant_id)");
        pg.MessageText.Should().Contain(TenantSeed.ChildId.ToString());
    }

    /// <summary>
    /// Profundidad máxima 2: un nieto no puede colgar de un hijo. En el flujo normal lo que se
    /// dispara es la regla (a2) —el hijo no es cabeza de grupo—, porque (b) impide que un hijo sea
    /// cabeza y por tanto (a3) «ya tiene padre» nunca llega a evaluarse con datos legales.
    /// </summary>
    [PostgresFact]
    public async Task Nieto_colgado_del_hijo_es_rechazado_por_profundidad_maxima_2()
    {
        await SeedAsync(TenantSeed.GroupParent());
        await SeedAsync(TenantSeed.ChildOf(TenantSeed.ParentId));

        var pg = await ExpectPostgresErrorAsync(() =>
            SeedAsync(TenantSeed.ChildOf(TenantSeed.ChildId, id: TenantSeed.GrandchildId, code: "IT-GRANDCHILD")));

        pg.SqlState.Should().Be(CheckViolation);
        pg.MessageText.Should().Contain($"tenant {TenantSeed.GrandchildId}: el padre {TenantSeed.ChildId} no es cabeza de grupo");

        await using var check = NewContext();
        (await check.Tenants.AnyAsync(t => t.Id == TenantSeed.GrandchildId)).Should().BeFalse();
    }

    /// <summary>
    /// Regla (a3) como defensa en profundidad: si un hijo llegara a ser cabeza de grupo por fuera del
    /// trigger (estado ilegal fabricado con el trigger deshabilitado dentro de una transacción que se
    /// revierte), colgar un nieto de él se rechaza igualmente con «profundidad máxima 2».
    /// </summary>
    [PostgresFact]
    public async Task Nieto_es_rechazado_por_profundidad_aunque_el_hijo_fuera_cabeza_por_estado_ilegal()
    {
        await SeedAsync(TenantSeed.GroupParent());
        await SeedAsync(TenantSeed.ChildOf(TenantSeed.ParentId));

        await using var connection = await Fixture.OpenConnectionAsync();
        await using var tx = await connection.BeginTransactionAsync();
        await Exec(connection, tx, "ALTER TABLE identity.tenants DISABLE TRIGGER tr_tenants_hierarchy");
        // HU #12406: ck_tenants_group_parent_by_type acopla is_group_parent al tipo, así que el estado
        // ilegal se fabrica con los dos campos a la vez (solo el trigger está deshabilitado, no el CHECK).
        await Exec(connection, tx, $"UPDATE identity.tenants SET is_group_parent = true, tenant_type = 'CONCESION' WHERE id = '{TenantSeed.ChildId}'");
        await Exec(connection, tx, "ALTER TABLE identity.tenants ENABLE TRIGGER tr_tenants_hierarchy");

        await using var insert = new NpgsqlCommand(
            """
            INSERT INTO identity.tenants (id, code, legal_name, tax_id, tenant_type, is_active, created_at, parent_tenant_id, is_group_parent)
            VALUES (@id, 'IT-GRANDCHILD', 'Nieto', '900000000-9', 'CONCESIONARIO', true, now(), @parent, false)
            """,
            connection,
            tx);
        insert.Parameters.AddWithValue("id", TenantSeed.GrandchildId);
        insert.Parameters.AddWithValue("parent", TenantSeed.ChildId);

        var pg = (await insert.Invoking(c => c.ExecuteNonQueryAsync()).Should().ThrowAsync<PostgresException>()).Which;
        await tx.RollbackAsync();

        pg.SqlState.Should().Be(CheckViolation);
        pg.MessageText.Should().Contain($"el padre {TenantSeed.ChildId} ya tiene padre; profundidad máxima 2");
    }

    /// <summary>Regla (a2): el padre debe ser cabeza de grupo.</summary>
    [PostgresFact]
    public async Task Padre_que_no_es_cabeza_de_grupo_es_rechazado()
    {
        await SeedAsync(TenantSeed.Lone());

        var pg = await ExpectPostgresErrorAsync(() => SeedAsync(TenantSeed.ChildOf(TenantSeed.LoneId)));

        pg.SqlState.Should().Be(CheckViolation);
        pg.MessageText.Should().Contain("no es cabeza de grupo (is_group_parent = false)");
    }

    /// <summary>Regla (a1): el padre debe existir. Se dispara antes que la FK porque el trigger es BEFORE.</summary>
    [PostgresFact]
    public async Task Padre_inexistente_es_rechazado_por_el_trigger_antes_que_por_la_FK()
    {
        var pg = await ExpectPostgresErrorAsync(() => SeedAsync(TenantSeed.ChildOf(Guid.NewGuid())));

        pg.SqlState.Should().Be(CheckViolation);
        pg.MessageText.Should().Contain("no existe");
    }

    /// <summary>Regla (c): quien tiene hijos no puede dejar de ser cabeza de grupo.</summary>
    [PostgresFact]
    public async Task Degradar_cabeza_de_grupo_con_hijos_es_rechazado()
    {
        await SeedAsync(TenantSeed.GroupParent());
        await SeedAsync(TenantSeed.ChildOf(TenantSeed.ParentId));

        var pg = await ExpectPostgresErrorAsync(async () =>
        {
            await using var ctx = NewContext();
            var parent = await ctx.Tenants.SingleAsync(t => t.Id == TenantSeed.ParentId);
            parent.IsGroupParent = false;
            await ctx.SaveChangesAsync();
        });

        pg.SqlState.Should().Be(CheckViolation);
        pg.MessageText.Should().Contain("tiene hijos vinculados; no puede dejar de ser cabeza de grupo ni recibir un padre");
    }

    /// <summary>Regla (c) por la otra puerta: una cabeza con hijos tampoco puede recibir padre (evita la profundidad 3 en dos pasos).</summary>
    [PostgresFact]
    public async Task Cabeza_de_grupo_con_hijos_no_puede_recibir_padre()
    {
        await SeedAsync(TenantSeed.GroupParent());
        await SeedAsync(TenantSeed.ChildOf(TenantSeed.ParentId));
        await SeedAsync(TenantSeed.GroupParent(id: TenantSeed.LoneId, code: "IT-OTHER-HEAD"));

        var pg = await ExpectPostgresErrorAsync(async () =>
        {
            await using var ctx = NewContext();
            var parent = await ctx.Tenants.SingleAsync(t => t.Id == TenantSeed.ParentId);
            parent.ParentTenantId = TenantSeed.LoneId;
            await ctx.SaveChangesAsync();
        });

        pg.SqlState.Should().Be(CheckViolation);
        pg.MessageText.Should().Contain("una cabeza de grupo (is_group_parent) no puede tener padre");
    }

    // ── fk_tenants_parent_tenant (23503) ────────────────────────────────────────

    [PostgresFact]
    public async Task Borrar_padre_con_hijos_es_rechazado_por_la_FK_RESTRICT()
    {
        await SeedAsync(TenantSeed.GroupParent());
        await SeedAsync(TenantSeed.ChildOf(TenantSeed.ParentId));

        var pg = await ExpectPostgresErrorAsync(async () =>
        {
            await using var ctx = NewContext();
            await ctx.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM identity.tenants WHERE id = {TenantSeed.ParentId}");
        });

        pg.SqlState.Should().Be(ForeignKeyViolation);
        pg.ConstraintName.Should().Be("fk_tenants_parent_tenant");
        pg.TableName.Should().Be("tenants");

        await using var check = NewContext();
        (await check.Tenants.CountAsync(t => t.Id == TenantSeed.ParentId)).Should().Be(1, "RESTRICT deja la fila intacta");
    }

    // ── ck_tenants_parent_not_self (23514) ──────────────────────────────────────

    /// <summary>
    /// El CHECK es la segunda línea de defensa: en el flujo normal el trigger BEFORE rechaza antes
    /// (el «padre» es la propia fila y no es cabeza de grupo / ya es cabeza). Para probar que el CHECK
    /// existe y actúa por sí mismo se deshabilita el trigger DENTRO de una transacción que se revierte.
    /// </summary>
    [PostgresFact]
    public async Task Autorreferencia_es_rechazada_por_el_CHECK_aunque_el_trigger_no_este()
    {
        await SeedAsync(TenantSeed.Lone());

        await using var connection = await Fixture.OpenConnectionAsync();
        await using var tx = await connection.BeginTransactionAsync();
        await Exec(connection, tx, "ALTER TABLE identity.tenants DISABLE TRIGGER tr_tenants_hierarchy");

        await using var update = new NpgsqlCommand(
            "UPDATE identity.tenants SET parent_tenant_id = id WHERE id = @id", connection, tx);
        update.Parameters.AddWithValue("id", TenantSeed.LoneId);

        var pg = (await update.Invoking(c => c.ExecuteNonQueryAsync()).Should().ThrowAsync<PostgresException>()).Which;
        await tx.RollbackAsync();

        pg.SqlState.Should().Be(CheckViolation);
        pg.ConstraintName.Should().Be("ck_tenants_parent_not_self");
        pg.TableName.Should().Be("tenants");
    }

    /// <summary>En el flujo normal (trigger activo) la autorreferencia también se rechaza, con 23514.</summary>
    [PostgresFact]
    public async Task Autorreferencia_con_trigger_activo_tambien_es_rechazada()
    {
        await SeedAsync(TenantSeed.Lone());

        var pg = await ExpectPostgresErrorAsync(async () =>
        {
            await using var ctx = NewContext();
            var lone = await ctx.Tenants.SingleAsync(t => t.Id == TenantSeed.LoneId);
            lone.ParentTenantId = lone.Id;
            await ctx.SaveChangesAsync();
        });

        pg.SqlState.Should().Be(CheckViolation);
    }

    // ── helpers ─────────────────────────────────────────────────────────────────

    private async Task SeedAsync(Tenant tenant)
    {
        await using var ctx = NewContext();
        ctx.Tenants.Add(tenant);
        await ctx.SaveChangesAsync();
    }

    private static async Task Exec(NpgsqlConnection connection, NpgsqlTransaction tx, string sql)
    {
        await using var cmd = new NpgsqlCommand(sql, connection, tx);
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task<PostgresException> ExpectPostgresErrorAsync(Func<Task> action)
    {
        var caught = await action.Should().ThrowAsync<Exception>();
        var ex = caught.Which;
        var pg = ex as PostgresException ?? ex.InnerException as PostgresException;
        pg.Should().NotBeNull($"se esperaba PostgresException (directa o como InnerException de {ex.GetType().Name})");
        return pg!;
    }
}
