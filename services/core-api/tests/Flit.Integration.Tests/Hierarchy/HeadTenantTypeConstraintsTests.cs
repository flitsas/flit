using Flit.Infrastructure.Persistence.Entities.Identity;
using Flit.Integration.Tests.Postgres;
using Flit.Queries.Domain.Tenancy;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace Flit.Integration.Tests.Hierarchy;

/// <summary>
/// HU #12406 (AC1, AC2, AC3, AC8) — la clase de la cabeza de grupo es su <c>tenant_type</c> y la
/// fuerza el MOTOR: CHECK <c>ck_tenants_tenant_type</c> ampliado, CHECK
/// <c>ck_tenants_group_parent_by_type</c> (<c>is_group_parent = tipo es de cabeza</c>, fail-closed) y la
/// rama (d) de <c>tr_tenants_hierarchy</c> (DDL 109), contra PostgreSQL real. Se afirma sobre
/// <see cref="PostgresException.SqlState"/>, <see cref="PostgresException.ConstraintName"/> y el texto
/// del <c>RAISE EXCEPTION</c>.
/// <para>
/// Uso de ejemplo: <c>TenantSeed.New(id, "IT-HEAD", isGroupParent: true, parentId: null, tenantType: "CONCESIONARIO")</c>
/// sembrado con el <c>FlitDbContext</c> real debe fallar con 23514 y <c>ck_tenants_group_parent_by_type</c>.
/// </para>
/// </summary>
public sealed class HeadTenantTypeConstraintsTests(PostgresDatabaseFixture fixture) : PostgresTestBase(fixture)
{
    private const string CheckViolation = "23514";
    private const string Catalog = "ck_tenants_tenant_type";
    private const string Coupling = "ck_tenants_group_parent_by_type";

    // ── AC1 · toda cabeza declara su clase (tipo de cabeza + marca en la misma fila) ────

    [PostgresTheory]
    [InlineData(GroupKindCodes.Concesion)]
    [InlineData(GroupKindCodes.MarcaBlanca)]
    public async Task AC1_Cabeza_con_tipo_de_cabeza_se_acepta_y_se_lee(string tenantType)
    {
        await SeedAsync(TenantSeed.New(TenantSeed.ParentId, "IT-PARENT", isGroupParent: true, parentId: null, tenantType));

        await using var ctx = NewContext();
        var head = await ctx.Tenants.AsNoTracking().SingleAsync(t => t.Id == TenantSeed.ParentId);

        head.IsGroupParent.Should().BeTrue();
        head.TenantType.Should().Be(tenantType);
    }

    [PostgresTheory]
    [InlineData("CONCESIONARIO")]
    [InlineData("RENTING")]
    [InlineData("FLIT")]
    public async Task AC1_Cabeza_marcada_con_tipo_que_no_es_de_cabeza_es_rechazada_por_el_CHECK(string tenantType)
    {
        var pg = await ExpectPostgresErrorAsync(() =>
            SeedAsync(TenantSeed.New(TenantSeed.ParentId, "IT-PARENT", isGroupParent: true, parentId: null, tenantType)));

        pg.SqlState.Should().Be(CheckViolation);
        pg.ConstraintName.Should().Be(Coupling);
        pg.TableName.Should().Be("tenants");

        await using var check = NewContext();
        (await check.Tenants.AnyAsync(t => t.Id == TenantSeed.ParentId)).Should().BeFalse();
    }

    [PostgresTheory]
    [InlineData("FRANQUICIA")]
    [InlineData("concesion")]
    [InlineData("")]
    public async Task AC1_Tipo_fuera_del_catalogo_es_rechazado_por_el_CHECK_del_catalogo(string tenantType)
    {
        // Sin marcar cabeza: así el único CHECK que puede fallar es el del catálogo (con la marca puesta,
        // PostgreSQL evalúa antes ck_tenants_group_parent_by_type y también rechaza).
        var pg = await ExpectPostgresErrorAsync(() =>
            SeedAsync(TenantSeed.New(TenantSeed.ParentId, "IT-PARENT", isGroupParent: false, parentId: null, tenantType)));

        pg.SqlState.Should().Be(CheckViolation);
        pg.ConstraintName.Should().Be(Catalog);
    }

    /// <summary>(b) del nuevo contrato: UPDATE del tipo a cabeza sin tocar is_group_parent → CHECK rechaza (fail-closed).</summary>
    [PostgresFact]
    public async Task AC1_Cambiar_el_tipo_a_cabeza_sin_marcar_is_group_parent_es_rechazado()
    {
        await SeedAsync(TenantSeed.Lone());

        var pg = await ExpectPostgresErrorAsync(async () =>
        {
            await using var ctx = NewContext();
            var lone = await ctx.Tenants.SingleAsync(t => t.Id == TenantSeed.LoneId);
            lone.TenantType = GroupKindCodes.Concesion; // sin IsGroupParent = true
            await ctx.SaveChangesAsync();
        });

        pg.SqlState.Should().Be(CheckViolation);
        pg.ConstraintName.Should().Be(Coupling);

        await using var check = NewContext();
        var tenant = await check.Tenants.AsNoTracking().SingleAsync(t => t.Id == TenantSeed.LoneId);
        tenant.TenantType.Should().Be("CONCESIONARIO");
        tenant.IsGroupParent.Should().BeFalse("la fila queda intacta: no hay corrección silenciosa");
    }

    [PostgresFact]
    public async Task AC1_Marcar_como_cabeza_sin_cambiar_el_tipo_es_rechazado()
    {
        await SeedAsync(TenantSeed.Lone());

        var pg = await ExpectPostgresErrorAsync(async () =>
        {
            await using var ctx = NewContext();
            var lone = await ctx.Tenants.SingleAsync(t => t.Id == TenantSeed.LoneId);
            lone.IsGroupParent = true; // tipo sigue CONCESIONARIO
            await ctx.SaveChangesAsync();
        });

        pg.SqlState.Should().Be(CheckViolation);
        pg.ConstraintName.Should().Be(Coupling);
    }

    [PostgresFact]
    public async Task AC1_Cambiar_tipo_y_marca_de_cabeza_en_la_misma_operacion_es_aceptado()
    {
        await SeedAsync(TenantSeed.Lone());

        await using (var ctx = NewContext())
        {
            var lone = await ctx.Tenants.SingleAsync(t => t.Id == TenantSeed.LoneId);
            lone.TenantType = GroupKindCodes.MarcaBlanca;
            lone.IsGroupParent = true;
            await ctx.SaveChangesAsync();
        }

        await using var check = NewContext();
        var head = await check.Tenants.AsNoTracking().SingleAsync(t => t.Id == TenantSeed.LoneId);
        head.IsGroupParent.Should().BeTrue();
        head.TenantType.Should().Be(GroupKindCodes.MarcaBlanca);
    }

    // ── AC2 · quien no es cabeza no lleva clase ─────────────────────────────────

    [PostgresFact]
    public async Task AC2_Cliente_sin_padre_no_marcado_no_puede_llevar_tipo_de_cabeza()
    {
        var pg = await ExpectPostgresErrorAsync(() =>
            SeedAsync(TenantSeed.New(TenantSeed.LoneId, "IT-LONE", isGroupParent: false, parentId: null, GroupKindCodes.Concesion)));

        pg.SqlState.Should().Be(CheckViolation);
        pg.ConstraintName.Should().Be(Coupling);
    }

    [PostgresFact]
    public async Task AC2_Hijo_con_padre_no_puede_llevar_tipo_de_cabeza()
    {
        await SeedAsync(TenantSeed.GroupParent());

        // Con is_group_parent = false → CHECK de acoplamiento; con true → (b) del trigger: en ninguna
        // de las dos puertas un hijo lleva tipo de cabeza.
        var pg = await ExpectPostgresErrorAsync(() =>
            SeedAsync(TenantSeed.New(TenantSeed.ChildId, "IT-CHILD", isGroupParent: false, parentId: TenantSeed.ParentId, GroupKindCodes.Concesion)));
        pg.SqlState.Should().Be(CheckViolation);
        pg.ConstraintName.Should().Be(Coupling);

        pg = await ExpectPostgresErrorAsync(() =>
            SeedAsync(TenantSeed.New(TenantSeed.ChildId, "IT-CHILD", isGroupParent: true, parentId: TenantSeed.ParentId, GroupKindCodes.Concesion)));
        pg.SqlState.Should().Be(CheckViolation);
        pg.MessageText.Should().Contain("una cabeza de grupo (is_group_parent) no puede tener padre");
    }

    /// <summary>Desmarcar una cabeza sin hijos = volver a un tipo que no es de cabeza, en la misma operación.</summary>
    [PostgresFact]
    public async Task AC2_Desmarcar_cabeza_sin_hijos_cambiando_el_tipo_en_la_misma_operacion_es_aceptado()
    {
        await SeedAsync(TenantSeed.GroupParent());

        await using (var ctx = NewContext())
        {
            var head = await ctx.Tenants.SingleAsync(t => t.Id == TenantSeed.ParentId);
            head.IsGroupParent = false;
            head.TenantType = "CONCESIONARIO";
            await ctx.SaveChangesAsync();
        }

        await using var check = NewContext();
        var tenant = await check.Tenants.AsNoTracking().SingleAsync(t => t.Id == TenantSeed.ParentId);
        tenant.IsGroupParent.Should().BeFalse();
        tenant.TenantType.Should().Be("CONCESIONARIO");
    }

    [PostgresFact]
    public async Task AC2_Desmarcar_cabeza_dejando_el_tipo_de_cabeza_es_rechazado_por_el_CHECK()
    {
        await SeedAsync(TenantSeed.GroupParent());

        await using var connection = await Fixture.OpenConnectionAsync();
        await using var update = new NpgsqlCommand(
            "UPDATE identity.tenants SET is_group_parent = false WHERE id = @id", connection);
        update.Parameters.AddWithValue("id", TenantSeed.ParentId);

        var pg = (await update.Invoking(c => c.ExecuteNonQueryAsync()).Should().ThrowAsync<PostgresException>()).Which;
        pg.SqlState.Should().Be(CheckViolation);
        pg.ConstraintName.Should().Be(Coupling, "fail-closed: la clase no se borra en silencio, se exige cambiar el tipo");
    }

    // ── AC3 · clase inmutable con hijos vigentes (rama (d)) ─────────────────────

    [PostgresFact]
    public async Task AC3_Cambiar_la_clase_de_una_cabeza_con_hijos_es_rechazado_por_el_trigger()
    {
        await SeedAsync(TenantSeed.GroupParent());
        await SeedAsync(TenantSeed.ChildOf(TenantSeed.ParentId));

        var pg = await ExpectPostgresErrorAsync(async () =>
        {
            await using var ctx = NewContext();
            var head = await ctx.Tenants.SingleAsync(t => t.Id == TenantSeed.ParentId);
            head.TenantType = GroupKindCodes.MarcaBlanca; // is_group_parent sigue true: cambio de clase puro
            await ctx.SaveChangesAsync();
        });

        pg.SqlState.Should().Be(CheckViolation);
        pg.ConstraintName.Should().BeNull("es el RAISE del trigger, no un CHECK");
        pg.MessageText.Should().Be(
            $"tenant {TenantSeed.ParentId}: tiene hijos vinculados; la clase de la cabeza de grupo (tenant_type) no puede cambiar de CONCESION a MARCA_BLANCA");

        await using var check = NewContext();
        (await check.Tenants.AsNoTracking().SingleAsync(t => t.Id == TenantSeed.ParentId)).TenantType
            .Should().Be(GroupKindCodes.Concesion, "la fila queda intacta");
    }

    [PostgresFact]
    public async Task AC3_Cabeza_con_hijos_no_puede_pasar_a_un_tipo_que_no_es_de_cabeza()
    {
        await SeedAsync(TenantSeed.GroupParent());
        await SeedAsync(TenantSeed.ChildOf(TenantSeed.ParentId));

        var pg = await ExpectPostgresErrorAsync(async () =>
        {
            await using var ctx = NewContext();
            var head = await ctx.Tenants.SingleAsync(t => t.Id == TenantSeed.ParentId);
            head.TenantType = "CONCESIONARIO";
            head.IsGroupParent = false;
            await ctx.SaveChangesAsync();
        });

        pg.SqlState.Should().Be(CheckViolation);
        pg.MessageText.Should().Contain("la clase de la cabeza de grupo (tenant_type) no puede cambiar de CONCESION a CONCESIONARIO");
    }

    [PostgresFact]
    public async Task AC3_Sin_hijos_vigentes_el_cambio_de_clase_es_aceptado()
    {
        await SeedAsync(TenantSeed.GroupParent());

        await using (var ctx = NewContext())
        {
            var head = await ctx.Tenants.SingleAsync(t => t.Id == TenantSeed.ParentId);
            head.TenantType = GroupKindCodes.MarcaBlanca;
            await ctx.SaveChangesAsync();
        }

        await using var check = NewContext();
        (await check.Tenants.AsNoTracking().SingleAsync(t => t.Id == TenantSeed.ParentId)).TenantType
            .Should().Be(GroupKindCodes.MarcaBlanca);
    }

    [PostgresFact]
    public async Task AC3_Tras_desvincular_al_ultimo_hijo_la_clase_vuelve_a_ser_modificable()
    {
        await SeedAsync(TenantSeed.GroupParent());
        await SeedAsync(TenantSeed.ChildOf(TenantSeed.ParentId));

        await using (var ctx = NewContext())
        {
            var child = await ctx.Tenants.SingleAsync(t => t.Id == TenantSeed.ChildId);
            child.ParentTenantId = null;
            await ctx.SaveChangesAsync();
        }

        await using (var ctx = NewContext())
        {
            var head = await ctx.Tenants.SingleAsync(t => t.Id == TenantSeed.ParentId);
            head.TenantType = GroupKindCodes.MarcaBlanca;
            await ctx.SaveChangesAsync();
        }

        await using var check = NewContext();
        (await check.Tenants.AsNoTracking().SingleAsync(t => t.Id == TenantSeed.ParentId)).TenantType
            .Should().Be(GroupKindCodes.MarcaBlanca);
    }

    /// <summary>La ampliación no rompe las ramas (a)(b)(c) de 107.</summary>
    [PostgresFact]
    public async Task AC3_Las_ramas_anteriores_del_trigger_siguen_vigentes()
    {
        await SeedAsync(TenantSeed.GroupParent());
        await SeedAsync(TenantSeed.ChildOf(TenantSeed.ParentId));

        // (c): con hijos, desmarcar sin tocar el tipo lo para el trigger antes que el CHECK.
        var pg = await ExpectPostgresErrorAsync(async () =>
        {
            await using var ctx = NewContext();
            var head = await ctx.Tenants.SingleAsync(t => t.Id == TenantSeed.ParentId);
            head.IsGroupParent = false;
            await ctx.SaveChangesAsync();
        });
        pg.SqlState.Should().Be(CheckViolation);
        pg.MessageText.Should().Contain("tiene hijos vinculados; no puede dejar de ser cabeza de grupo ni recibir un padre");

        // (b): una cabeza tampoco puede recibir padre.
        await SeedAsync(TenantSeed.GroupParent(id: TenantSeed.LoneId, code: "IT-OTHER-HEAD"));
        pg = await ExpectPostgresErrorAsync(async () =>
        {
            await using var ctx = NewContext();
            var other = await ctx.Tenants.SingleAsync(t => t.Id == TenantSeed.LoneId);
            other.ParentTenantId = TenantSeed.ParentId;
            await ctx.SaveChangesAsync();
        });
        pg.MessageText.Should().Contain("una cabeza de grupo (is_group_parent) no puede tener padre");

        // (a2): un CONCESIONARIO suelto (no cabeza) no puede ser padre.
        await SeedAsync(TenantSeed.Lone(id: TenantSeed.GrandchildId, code: "IT-LONE"));
        pg = await ExpectPostgresErrorAsync(() =>
            SeedAsync(TenantSeed.ChildOf(TenantSeed.GrandchildId, id: new Guid("99999999-9999-4999-8999-999999999999"), code: "IT-CHILD-2")));
        pg.MessageText.Should().Contain("no es cabeza de grupo (is_group_parent = false)");
    }

    // ── AC8 · artefactos presentes en el catálogo de PostgreSQL ─────────────────

    [PostgresFact]
    public async Task AC8_Los_CHECK_y_los_triggers_existen_en_el_catalogo()
    {
        await using var connection = await Fixture.OpenConnectionAsync();

        await using (var cmd = new NpgsqlCommand(
            "SELECT pg_get_constraintdef(oid) FROM pg_constraint WHERE conname = 'ck_tenants_tenant_type'", connection))
        {
            var def = (string?)await cmd.ExecuteScalarAsync();
            def.Should().NotBeNull();
            def.Should().Contain("'RENTING'").And.Contain("'CONCESIONARIO'").And.Contain("'FLIT'")
                .And.Contain("'CONCESION'").And.Contain("'MARCA_BLANCA'");
        }

        await using (var cmd = new NpgsqlCommand(
            "SELECT pg_get_constraintdef(oid) FROM pg_constraint WHERE conname = 'ck_tenants_group_parent_by_type'", connection))
        {
            var def = (string?)await cmd.ExecuteScalarAsync();
            def.Should().NotBeNull();
            def.Should().Contain("is_group_parent").And.Contain("'CONCESION'").And.Contain("'MARCA_BLANCA'");
        }

        await using (var cmd = new NpgsqlCommand(
            """
            SELECT string_agg(a.attname, ',' ORDER BY a.attnum)
              FROM pg_trigger t
              JOIN pg_attribute a ON a.attrelid = t.tgrelid AND a.attnum = ANY (t.tgattr)
             WHERE t.tgname = 'tr_tenants_hierarchy'
            """, connection))
        {
            var columnas = ((string?)await cmd.ExecuteScalarAsync())?.Split(',');
            columnas.Should().BeEquivalentTo(["parent_tenant_id", "is_group_parent", "tenant_type"], "UPDATE OF incluye el tipo");
        }

        await using (var cmd = new NpgsqlCommand(
            "SELECT tgenabled::text FROM pg_trigger WHERE tgname = 'tr_procedure_instances_parent_snapshot_immutable'", connection))
        {
            (await cmd.ExecuteScalarAsync()).Should().Be("O", "trigger presente y habilitado (origin)");
        }
    }

    // ── helpers ─────────────────────────────────────────────────────────────────

    private async Task SeedAsync(Tenant tenant)
    {
        await using var ctx = NewContext();
        ctx.Tenants.Add(tenant);
        await ctx.SaveChangesAsync();
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
