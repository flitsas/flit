using System.Text;
using Flit.Infrastructure.Persistence;
using Flit.Integration.Tests.Postgres;
using Flit.Tramites.Domain.Entities.ConsolidadoLotes;
using FluentAssertions;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Npgsql;
using Xunit;

namespace Flit.Integration.Tests.Tramites;

/// <summary>
/// HU #13368 (Feature #13306, épica #13216, ADR-0070) — DDL 134 contra PostgreSQL real con TODAS las migraciones:
/// partes (tenant heredado del lote por trigger), ítems (coherencia por estado, los diez códigos de omisión, un trámite
/// por lote, FK compuesta a la parte), auditoría append-only, estados de cancelación de #13307 y un Down que retira
/// las tres tablas y sus funciones sin tocar las de #13367.
/// </summary>
public sealed class ConsolidadoExportItemsSchemaMigrationTests(PostgresDatabaseFixture fixture) : PostgresTestBase(fixture)
{
    private const string MigrationName = "HU13368_ConsolidadoExportItems";

    private static readonly Guid Company = new("b3000000-0000-7000-8000-0000000013c1");
    private static readonly Guid OtherCompany = new("b3100000-0000-7000-8000-0000000013c2");
    private static readonly Guid UserU = new("0199c300-0000-7000-8000-0000000013d1");
    private static readonly Guid TramiteT = new("0199c300-0000-7000-8000-0000000013e1");
    private static readonly Guid TramiteT2 = new("0199c300-0000-7000-8000-0000000013e2");
    private static readonly Guid LoteL = new("0199c300-0000-7000-8000-0000000013f1");
    private static readonly Guid LoteSa = new("0199c300-0000-7000-8000-0000000013f2");

    private static readonly string[] Tablas =
        ["consolidado_export_batch_parts", "consolidado_export_batch_items", "consolidado_export_audit"];

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task ExecAsync(NpgsqlConnection cn, string sql, params (string Name, object? Value)[] args)
    {
        await using var cmd = new NpgsqlCommand(sql, cn);
        foreach (var (name, value) in args)
        {
            cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);
        }

        await cmd.ExecuteNonQueryAsync(Ct);
    }

    private static async Task<T?> ScalarAsync<T>(NpgsqlConnection cn, string sql, params (string Name, object? Value)[] args)
    {
        await using var cmd = new NpgsqlCommand(sql, cn);
        foreach (var (name, value) in args)
        {
            cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);
        }

        var result = await cmd.ExecuteScalarAsync(Ct);
        return result is null or DBNull ? default : (T)result;
    }

    /// <summary>Ejecuta <paramref name="act"/> y devuelve la restricción violada (o el SQLSTATE), o null si no falla.</summary>
    private static async Task<string?> ConstraintOfViolationAsync(NpgsqlConnection cn, Func<Task> act)
    {
        // Savepoint: un fallo esperado no aborta la transacción de la prueba.
        await ExecAsync(cn, "SAVEPOINT sp");
        try
        {
            await act();
            await ExecAsync(cn, "RELEASE SAVEPOINT sp");
            return null;
        }
        catch (PostgresException ex)
        {
            await ExecAsync(cn, "ROLLBACK TO SAVEPOINT sp");
            return ex.ConstraintName ?? ex.SqlState;
        }
    }

    private static string LoadDdl(string archivo)
    {
        var resource = "Flit.Infrastructure.Persistence.Sql.Ddl." + archivo;
        using var stream = typeof(FlitDbContext).Assembly.GetManifestResourceStream(resource);
        stream.Should().NotBeNull($"el DDL embebido {resource} debe existir");
        using var reader = new StreamReader(stream!, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    private static string DownSql()
    {
        var tipo = typeof(FlitDbContext).Assembly.GetType("Flit.Infrastructure.Migrations." + MigrationName);
        tipo.Should().NotBeNull($"la migración {MigrationName} debe existir");
        return ((Migration)Activator.CreateInstance(tipo!)!).DownOperations.OfType<SqlOperation>().Single().Sql;
    }

    /// <summary>
    /// Compañías, usuario, dos trámites, un lote de Gestor (L, en proceso) y uno de Super Admin sin compañía. Todo en
    /// una transacción abierta que la prueba revierte al final.
    /// </summary>
    private async Task<(NpgsqlConnection Cn, NpgsqlTransaction Tx)> SeedAsync()
    {
        await using (var ctx = NewContext())
        {
            ctx.Tenants.Add(TenantSeed.New(Company, "IT-CIA-13368", false, null));
            ctx.Tenants.Add(TenantSeed.New(OtherCompany, "IT-CIB-13368", false, null));
            await ctx.SaveChangesAsync(Ct);
        }

        var cn = await Fixture.OpenConnectionAsync();
        var tx = await cn.BeginTransactionAsync(Ct);
        await ExecAsync(cn,
            """
            INSERT INTO identity.users (id, email, display_name, status, created_at)
            VALUES (@u, 'lote13368@it.test', 'Usuario lote', 'active', now());
            INSERT INTO tramites.procedure_instances
                (id, tenant_id, procedure_type_id, reference_number, status, vin, created_by_user_id, created_at)
            VALUES (@t1, @c, (SELECT id FROM tramites.procedure_types ORDER BY code LIMIT 1), 'IT-13368-1', 'borrador', 'SINTVIN13368A', @u, now()),
                   (@t2, @c, (SELECT id FROM tramites.procedure_types ORDER BY code LIMIT 1), 'IT-13368-2', 'borrador', 'SINTVIN13368B', @u, now());
            INSERT INTO tramites.consolidado_export_batches
              (id, tenant_id, requested_by_user_id, requested_role_code, origin, document_type, selection_mode, status,
               total_items, dek_wrapped, effects_acknowledged_at, created_by)
            VALUES (@l, @c, @u, 'Radicador', 'tramites', 'consolidado', 'ids', 'en_proceso', 2, '\x010203'::bytea, now(), @u);
            INSERT INTO tramites.consolidado_export_batches
              (id, tenant_id, requested_by_user_id, requested_role_code, origin, document_type, selection_mode, status,
               total_items, effects_acknowledged_at, finished_at, expires_at, created_by)
            VALUES (@sa, NULL, @u, 'SuperAdmin', 'superadmin', 'consolidado', 'filtro', 'completado', 0, now(), now(), now(), @u);
            """,
            ("u", UserU), ("c", Company), ("t1", TramiteT), ("t2", TramiteT2), ("l", LoteL), ("sa", LoteSa));
        return (cn, tx);
    }

    private static Task InsertItemAsync(NpgsqlConnection cn, Guid tramite, int position = 0) => ExecAsync(cn,
        """
        INSERT INTO tramites.consolidado_export_batch_items
            (tenant_id, batch_id, procedure_instance_id, position, reference_number, plate, created_by)
        VALUES (@c, @l, @t, @p, 'IT-13368', 'ABC123', @u)
        """,
        ("c", Company), ("l", LoteL), ("t", tramite), ("p", position), ("u", UserU));

    private static Task InsertAuditAsync(
        NpgsqlConnection cn, string evento, int? total = 2, int? incluidos = 1, int? omitidos = 1, int? generados = 0) =>
        ExecAsync(cn,
            """
            INSERT INTO tramites.consolidado_export_audit
                (event, origin, batch_id, actor_user_id, actor_tenant_id, actor_role_code, reached_tenant_ids,
                 document_type, selection_mode, total_items, included_count, omitted_count, generated_count, client_ip)
            VALUES (@e, 'tramites', @l, @u, @c, 'Radicador', ARRAY[@c]::uuid[],
                    'consolidado', 'ids', @tot, @inc, @omi, @gen, '10.0.0.1'::inet)
            """,
            ("e", evento), ("l", LoteL), ("u", UserU), ("c", Company),
            ("tot", total), ("inc", incluidos), ("omi", omitidos), ("gen", generados));

    [PostgresFact]
    public async Task AC1_LaMigracionCreaLasTresTablasYLaParteHeredaElTenantDelLote()
    {
        var (cn, tx) = await SeedAsync();
        await using var _ = cn;

        (await ScalarAsync<long>(cn,
            "SELECT count(*) FROM public.\"__EFMigrationsHistory\" WHERE migration_id LIKE @m", ("m", "%_" + MigrationName)))
            .Should().Be(1, "el fixture migra con Database.MigrateAsync(): la migración es descubrible");
        (await ScalarAsync<long>(cn,
            "SELECT count(*) FROM information_schema.tables WHERE table_schema = 'tramites' AND table_name = ANY (@n)",
            ("n", Tablas))).Should().Be(3);
        (await ScalarAsync<long>(cn,
            "SELECT count(*) FROM pg_trigger WHERE tgname = 'tr_consolidado_export_batch_parts_tenant' AND NOT tgisinternal"))
            .Should().Be(1);

        // El valor que envía la aplicación se ignora: la base copia el del lote (también NULL en Super Admin).
        await ExecAsync(cn,
            """
            INSERT INTO tramites.consolidado_export_batch_parts (batch_id, part_number, tenant_id) VALUES (@l, 1, NULL);
            INSERT INTO tramites.consolidado_export_batch_parts (batch_id, part_number, tenant_id) VALUES (@l, 2, @otra);
            INSERT INTO tramites.consolidado_export_batch_parts (batch_id, part_number, tenant_id) VALUES (@sa, 1, @c);
            """,
            ("l", LoteL), ("sa", LoteSa), ("c", Company), ("otra", OtherCompany));
        (await ScalarAsync<long>(cn,
            "SELECT count(*) FROM tramites.consolidado_export_batch_parts WHERE batch_id = @l AND tenant_id = @c",
            ("l", LoteL), ("c", Company))).Should().Be(2);
        (await ScalarAsync<long>(cn,
            "SELECT count(*) FROM tramites.consolidado_export_batch_parts WHERE batch_id = @sa AND tenant_id IS NULL",
            ("sa", LoteSa))).Should().Be(1);
        (await ConstraintOfViolationAsync(cn, () => ExecAsync(cn,
                "UPDATE tramites.consolidado_export_batch_parts SET tenant_id = @otra WHERE batch_id = @l AND part_number = 1",
                ("otra", OtherCompany), ("l", LoteL))))
            .Should().BeNull();
        (await ScalarAsync<Guid>(cn,
            "SELECT tenant_id FROM tramites.consolidado_export_batch_parts WHERE batch_id = @l AND part_number = 1",
            ("l", LoteL))).Should().Be(Company, "el UPDATE de tenant_id también lo recalcula el trigger");

        // Partes antes que ítems: un ítem no puede apuntar a una parte que no existe (FK compuesta).
        await InsertItemAsync(cn, TramiteT);
        (await ConstraintOfViolationAsync(cn, () => ExecAsync(cn,
                """
                UPDATE tramites.consolidado_export_batch_items
                   SET status = 'omitido', omission_code = 'error_tecnico', omission_reason = 'x', processed_at = now(),
                       part_number = 9
                 WHERE procedure_instance_id = @t
                """, ("t", TramiteT))))
            .Should().Be("fk_consolidado_export_batch_items_batch_parts");

        await tx.RollbackAsync(Ct);
    }

    [PostgresFact]
    public async Task AC2_LaAuditoriaEsAppendOnly()
    {
        var (cn, tx) = await SeedAsync();
        await using var _ = cn;
        await InsertAuditAsync(cn, ConsolidadoExportAuditEvent.LoteCreado);

        foreach (var sql in new[]
                 {
                     "UPDATE tramites.consolidado_export_audit SET actor_role_code = 'otro'",
                     "DELETE FROM tramites.consolidado_export_audit",
                 })
        {
            await ExecAsync(cn, "SAVEPOINT sp");
            var act = () => ExecAsync(cn, sql);
            var ex = (await act.Should().ThrowAsync<PostgresException>(sql)).Which;
            ex.SqlState.Should().Be(PostgresErrorCodes.CheckViolation);
            ex.MessageText.Should().Contain("append-only");
            ex.Where.Should().Contain("trg_consolidado_export_audit_immutable", "lo rechaza tr_consolidado_export_audit_immutable");
            await ExecAsync(cn, "ROLLBACK TO SAVEPOINT sp");
        }

        (await ScalarAsync<long>(cn, "SELECT count(*) FROM tramites.consolidado_export_audit WHERE actor_role_code = 'Radicador'"))
            .Should().Be(1);

        await tx.RollbackAsync(Ct);
    }

    [PostgresFact]
    public async Task AC3_LaCoherenciaDelItemPorEstadoLaGarantizaLaBase()
    {
        var (cn, tx) = await SeedAsync();
        await using var _ = cn;
        await InsertItemAsync(cn, TramiteT);

        const string incluido =
            """
            UPDATE tramites.consolidado_export_batch_items
               SET status = 'incluido', attachment_id = uuidv7(), size_bytes = 10, processed_at = now(),
                   storage_path = @sp, delivery_mode = @dm
             WHERE procedure_instance_id = @t
            """;
        (await ConstraintOfViolationAsync(cn, () => ExecAsync(cn, incluido, ("sp", null), ("dm", "existente"), ("t", TramiteT))))
            .Should().Be("ck_consolidado_export_batch_items_included", "incluido sin storage_path");
        (await ConstraintOfViolationAsync(cn, () => ExecAsync(cn, incluido, ("sp", "fm/1"), ("dm", null), ("t", TramiteT))))
            .Should().Be("ck_consolidado_export_batch_items_included", "incluido sin delivery_mode");
        (await ConstraintOfViolationAsync(cn, () => ExecAsync(cn, incluido, ("sp", "fm/1"), ("dm", "regenerado"), ("t", TramiteT))))
            .Should().Be("ck_consolidado_export_batch_items_delivery_mode");

        const string omitido =
            """
            UPDATE tramites.consolidado_export_batch_items
               SET status = 'omitido', omission_code = @oc, omission_reason = 'motivo', processed_at = now()
             WHERE procedure_instance_id = @t
            """;
        foreach (var fuera in new[] { "consolidado_no_generado", "en_regeneracion", "FUR_REQUERIDO" })
        {
            (await ConstraintOfViolationAsync(cn, () => ExecAsync(cn, omitido, ("oc", fuera), ("t", TramiteT))))
                .Should().Be("ck_consolidado_export_batch_items_omission_code", fuera);
        }

        // Los diez del vocabulario de #13371 se aceptan.
        foreach (var codigo in ConsolidadoLoteOmisiones.Todos)
        {
            (await ConstraintOfViolationAsync(cn, () => ExecAsync(cn, omitido, ("oc", codigo), ("t", TramiteT))))
                .Should().BeNull(codigo);
            await ExecAsync(cn,
                """
                UPDATE tramites.consolidado_export_batch_items
                   SET status = 'pendiente', omission_code = NULL, omission_reason = NULL, processed_at = NULL
                 WHERE procedure_instance_id = @t
                """, ("t", TramiteT));
        }

        (await ConstraintOfViolationAsync(cn, () => ExecAsync(cn,
                "UPDATE tramites.consolidado_export_batch_items SET status = 'omitido', omission_reason = 'x', processed_at = now() WHERE procedure_instance_id = @t",
                ("t", TramiteT))))
            .Should().Be("ck_consolidado_export_batch_items_omitted", "omitido sin código");
        (await ConstraintOfViolationAsync(cn, () => ExecAsync(cn, incluido, ("sp", "fm/1"), ("dm", "generado"), ("t", TramiteT))))
            .Should().BeNull("incluido con snapshot completo y modo de entrega");

        await tx.RollbackAsync(Ct);
    }

    [PostgresFact]
    public async Task AC4_UnTramiteNoSeRepiteDentroDelMismoLote()
    {
        var (cn, tx) = await SeedAsync();
        await using var _ = cn;
        await InsertItemAsync(cn, TramiteT);

        (await ConstraintOfViolationAsync(cn, () => InsertItemAsync(cn, TramiteT, position: 1)))
            .Should().Be("uq_consolidado_export_batch_items_batch_instance");
        (await ConstraintOfViolationAsync(cn, () => InsertItemAsync(cn, TramiteT2, position: 1)))
            .Should().BeNull("otro trámite del mismo lote");

        await tx.RollbackAsync(Ct);
    }

    [PostgresFact]
    public async Task AC5_ElDownRetiraLasTresTablasYSusFuncionesSinTocarLasDe13367_YElUpLasRepone()
    {
        var (cn, tx) = await SeedAsync();
        await using var _ = cn;
        await InsertItemAsync(cn, TramiteT);
        await InsertAuditAsync(cn, ConsolidadoExportAuditEvent.LoteCreado);

        const string huella =
            """
            SELECT string_agg(f, ',' ORDER BY f) FROM (
              SELECT c.table_schema || '.' || c.table_name || '.' || c.column_name || ':' || c.data_type AS f
                FROM information_schema.columns c
               WHERE c.table_schema NOT IN ('pg_catalog', 'information_schema')
                 AND c.table_name <> ALL (@n)) x
            """;
        const string restricciones =
            """
            SELECT count(*) FROM pg_constraint k JOIN pg_namespace n ON n.oid = k.connamespace
             WHERE n.nspname NOT IN ('pg_catalog', 'information_schema')
            """;
        const string funciones =
            """
            SELECT count(*) FROM pg_proc p JOIN pg_namespace n ON n.oid = p.pronamespace
             WHERE n.nspname = 'tramites'
               AND p.proname IN ('trg_consolidado_export_batch_parts_tenant', 'trg_consolidado_export_audit_immutable')
            """;
        const string tablas13367 =
            """
            SELECT string_agg(c.table_name || '.' || c.column_name, ',' ORDER BY c.table_name, c.ordinal_position)
              FROM information_schema.columns c
             WHERE c.table_schema = 'tramites' AND c.table_name IN ('consolidado_export_settings', 'consolidado_export_batches')
            """;
        var antes = await ScalarAsync<string>(cn, huella, ("n", Tablas));
        var restriccionesAntes = await ScalarAsync<long>(cn, restricciones);
        var restriccionesPropias = await ScalarAsync<long>(cn,
            "SELECT count(*) FROM pg_constraint WHERE conrelid = ANY (ARRAY['tramites.consolidado_export_batch_parts'::regclass, 'tramites.consolidado_export_batch_items'::regclass, 'tramites.consolidado_export_audit'::regclass])");
        var columnas13367 = await ScalarAsync<string>(cn, tablas13367);
        (await ScalarAsync<long>(cn, funciones)).Should().Be(2);

        await ExecAsync(cn, DownSql());

        (await ScalarAsync<long>(cn,
            "SELECT count(*) FROM information_schema.tables WHERE table_schema = 'tramites' AND table_name = ANY (@n)",
            ("n", Tablas))).Should().Be(0);
        (await ScalarAsync<long>(cn, funciones)).Should().Be(0, "las funciones de trigger desaparecen con el Down");
        (await ScalarAsync<long>(cn,
                """
                SELECT count(*) FROM pg_trigger
                 WHERE tgname IN ('tr_consolidado_export_batch_parts_tenant', 'tr_consolidado_export_batch_parts_row_version',
                                  'tr_consolidado_export_batch_items_row_version', 'tr_consolidado_export_audit_immutable')
                """))
            .Should().Be(0);
        (await ScalarAsync<long>(cn,
                "SELECT count(*) FROM pg_trigger WHERE tgrelid = 'tramites.consolidado_export_batches'::regclass AND NOT tgisinternal"))
            .Should().Be(2, "los triggers de #13367 (row_version y audit) siguen en el lote");
        (await ScalarAsync<string>(cn, huella, ("n", Tablas))).Should().Be(antes, "ninguna tabla preexistente cambia");
        (await ScalarAsync<string>(cn, tablas13367)).Should().Be(columnas13367, "las tablas de #13367 siguen intactas");
        (await ScalarAsync<long>(cn,
            "SELECT count(*) FROM tramites.consolidado_export_batches WHERE id = ANY (@ids)", ("ids", new[] { LoteL, LoteSa })))
            .Should().Be(2, "los lotes de #13367 conservan sus filas");
        (await ScalarAsync<long>(cn, restricciones)).Should().Be(restriccionesAntes - restriccionesPropias);

        // Up tras Down (reversible) y otra vez (idempotente).
        var up = LoadDdl("134-HU13368-consolidado-export-items.sql");
        await ExecAsync(cn, up);
        await ExecAsync(cn, up);
        (await ScalarAsync<long>(cn, restricciones)).Should().Be(restriccionesAntes);
        (await ScalarAsync<long>(cn, funciones)).Should().Be(2);
        (await ScalarAsync<long>(cn,
            "SELECT count(*) FROM pg_class WHERE relname = ANY (@n) AND relrowsecurity", ("n", Tablas))).Should().Be(3);

        await tx.RollbackAsync(Ct);
    }

    [PostgresFact]
    public async Task AC6_EstadosDeCancelacion_ParteDescartadaItemCanceladoYLoteCanceladoConConteos()
    {
        var (cn, tx) = await SeedAsync();
        await using var _ = cn;
        await InsertItemAsync(cn, TramiteT);
        await ExecAsync(cn,
            """
            INSERT INTO tramites.consolidado_export_batch_parts (batch_id, part_number, status, lease_until)
            VALUES (@l, 1, 'empaquetando', now() + interval '10 minutes');
            UPDATE tramites.consolidado_export_batch_items
               SET status = 'procesando', lease_until = now() + interval '10 minutes', claimed_by = 'it'
             WHERE procedure_instance_id = @t;
            """,
            ("l", LoteL), ("t", TramiteT));

        // Transacción de cancelación de #13307 (09-diseno §2.4): ítems vivos → cancelado; partes sin cerrar → descartada.
        (await ConstraintOfViolationAsync(cn, () => ExecAsync(cn,
                "UPDATE tramites.consolidado_export_batch_items SET status = 'cancelado', lease_until = NULL, updated_by = @u WHERE batch_id = @l AND status IN ('pendiente', 'procesando')",
                ("u", UserU), ("l", LoteL))))
            .Should().BeNull("el ítem admite 'cancelado'");
        (await ConstraintOfViolationAsync(cn, () => ExecAsync(cn,
                "UPDATE tramites.consolidado_export_batch_parts SET status = 'descartada', lease_until = NULL WHERE batch_id = @l AND status IN ('pendiente', 'empaquetando')",
                ("l", LoteL))))
            .Should().BeNull("la parte admite 'descartada'");
        (await ScalarAsync<string>(cn,
            "SELECT i.status || '/' || p.status FROM tramites.consolidado_export_batch_items i, tramites.consolidado_export_batch_parts p WHERE i.batch_id = @l AND p.batch_id = @l",
            ("l", LoteL))).Should().Be("cancelado/descartada");

        // El cierre en vuelo (cierre condicionado de #13376) ya no encuentra el ítem.
        (await ScalarAsync<long>(cn,
            "WITH u AS (UPDATE tramites.consolidado_export_batch_items SET status = 'incluido' WHERE batch_id = @l AND status = 'procesando' RETURNING 1) SELECT count(*) FROM u",
            ("l", LoteL))).Should().Be(0);

        foreach (var (total, incluidos, omitidos, generados, motivo) in new (int?, int?, int?, int?, string)[]
                 {
                     (null, 1, 1, 0, "sin total"),
                     (2, null, 1, 0, "sin incluidos"),
                     (2, 1, null, 0, "sin omitidos"),
                     (2, 1, 1, null, "sin generados"),
                     (2, 1, 1, 2, "generados > incluidos"),
                 })
        {
            (await ConstraintOfViolationAsync(cn, () =>
                    InsertAuditAsync(cn, ConsolidadoExportAuditEvent.LoteCancelado, total, incluidos, omitidos, generados)))
                .Should().Be("ck_consolidado_export_audit_cancelled", motivo);
        }

        (await ConstraintOfViolationAsync(cn, () => InsertAuditAsync(cn, ConsolidadoExportAuditEvent.LoteCancelado)))
            .Should().BeNull("lote_cancelado con sus conteos");

        await tx.RollbackAsync(Ct);
    }
}
