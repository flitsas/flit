using System.Text;
using Flit.Infrastructure.Migrations;
using Flit.Infrastructure.Persistence;
using Flit.Integration.Tests.Postgres;
using Flit.Tramites.Domain.Entities.ConsolidadoLotes;
using FluentAssertions;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Npgsql;
using Xunit;

namespace Flit.Integration.Tests.Tramites;

/// <summary>
/// HU #13367 (Feature #13306, épica #13216, ADR-0070) — DDL 133 contra PostgreSQL real con TODAS las migraciones:
/// fila única de parámetros sembrada con los valores del AC y protegida por CHECK nombrados, un lote activo por
/// usuario, <c>tenant_id</c> nulo solo en origen superadmin (E5), organismo solo y siempre en origen ot_bandeja (R-d)
/// y un Down que retira las dos tablas sin tocar ninguna preexistente.
/// </summary>
public sealed class ConsolidadoExportSchemaMigrationTests(PostgresDatabaseFixture fixture) : PostgresTestBase(fixture)
{
    private const string MigrationId = "20261007165951_HU13367_ConsolidadoExportBatches";

    private static readonly Guid Company = new("b2000000-0000-7000-8000-0000000013a1");
    private static readonly Guid Office = new("0199c200-0000-7000-8000-0000000013a1");
    private static readonly Guid UserU = new("0199c200-0000-7000-8000-0000000013b1");

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

    private static async Task<string?> ConstraintOfViolationAsync(Func<Task> act)
    {
        try
        {
            await act();
            return null;
        }
        catch (PostgresException ex)
        {
            return ex.ConstraintName ?? ex.SqlState;
        }
    }

    private static string LoadDdl()
    {
        const string resource = "Flit.Infrastructure.Persistence.Sql.Ddl.133-HU13367-consolidado-export-batches.sql";
        using var stream = typeof(FlitDbContext).Assembly.GetManifestResourceStream(resource);
        stream.Should().NotBeNull($"el DDL embebido {resource} debe existir");
        using var reader = new StreamReader(stream!, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    private async Task<NpgsqlConnection> SeedAsync()
    {
        await using (var ctx = NewContext())
        {
            ctx.Tenants.Add(TenantSeed.New(Company, "IT-CIA-13367", false, null));
            await ctx.SaveChangesAsync(Ct);
        }

        var cn = await Fixture.OpenConnectionAsync();
        await ExecAsync(cn,
            """
            INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code)
            VALUES (@o, 'IT13367', 'OT integración 13367', '05', '05001');
            INSERT INTO identity.users (id, email, display_name, status, created_at)
            VALUES (@u, 'lote@it.test', 'Usuario lote', 'active', now());
            """,
            ("o", Office), ("u", UserU));
        return cn;
    }

    // Lote mínimo válido: un activo lleva DEK y no tiene cierre; uno terminal tiene cierre y caducidad.
    private static Task InsertBatchAsync(
        NpgsqlConnection cn, string status, string origin = ConsolidadoExportOrigin.Tramites, Guid? tenantId = null,
        Guid? officeId = null, bool sinTenant = false)
    {
        var activo = ConsolidadoExportStatus.EsActivo(status);
        return ExecAsync(cn,
            """
            INSERT INTO tramites.consolidado_export_batches
              (tenant_id, requested_by_user_id, requested_role_code, origin, document_type, selection_mode, status,
               total_items, dek_wrapped, effects_acknowledged_at, finished_at, expires_at, ot_transit_office_id, created_by)
            VALUES (@t, @u, 'Radicador', @origin, 'consolidado', 'ids', @st,
                    0, @dek, now(), @fin, @exp, @o, @u)
            """,
            ("t", sinTenant ? null : tenantId ?? Company), ("u", UserU), ("origin", origin), ("st", status),
            ("dek", activo ? new byte[] { 1, 2, 3 } : null),
            ("fin", activo ? null : DateTimeOffset.UtcNow), ("exp", activo ? null : DateTimeOffset.UtcNow.AddHours(24)),
            ("o", officeId));
    }

    [PostgresFact]
    public async Task AC1_LaMigracionCreaLasTablasYSiembraUnaFilaDeParametros()
    {
        await using var cn = await SeedAsync();

        (await ScalarAsync<long>(cn,
            "SELECT count(*) FROM public.\"__EFMigrationsHistory\" WHERE migration_id = @m", ("m", MigrationId)))
            .Should().Be(1, "el fixture migra con Database.MigrateAsync(): la migración es descubrible");
        (await ScalarAsync<long>(cn,
            """
            SELECT count(*) FROM information_schema.tables
             WHERE table_schema = 'tramites' AND table_name IN ('consolidado_export_settings', 'consolidado_export_batches')
            """)).Should().Be(2);
        Fixture.SeededTablesAfterMigration.Should().Contain("tramites.consolidado_export_settings",
            "la migración siembra la fila de parámetros");

        // El reset entre pruebas trunca la fila (configuración, no catálogo): se reaplica el DDL dos veces para
        // probar además que el sembrado es reproducible y no duplica.
        await ExecAsync(cn, LoadDdl());
        await ExecAsync(cn, LoadDdl());

        (await ScalarAsync<long>(cn, "SELECT count(*) FROM tramites.consolidado_export_settings")).Should().Be(1);
        (await ScalarAsync<string>(cn,
            """
            SELECT concat_ws(',', max_pdfs_per_part, max_mb_per_part, item_slots, retry_delay_seconds,
                             item_timeout_seconds, item_lease_seconds, retention_hours, is_active::text)
              FROM tramites.consolidado_export_settings
            """)).Should().Be("500,250,2,30,300,600,24,true");
    }

    [PostgresFact]
    public async Task AC2_LaBaseImpideUnSegundoLoteActivoDelMismoUsuario()
    {
        await using var cn = await SeedAsync();
        await InsertBatchAsync(cn, ConsolidadoExportStatus.EnCola);

        (await ConstraintOfViolationAsync(() => InsertBatchAsync(cn, ConsolidadoExportStatus.EnProceso)))
            .Should().Be("uq_consolidado_export_batches_active_per_user");
        (await ConstraintOfViolationAsync(() => InsertBatchAsync(cn, ConsolidadoExportStatus.Completado)))
            .Should().BeNull("un lote terminal del mismo usuario no cuenta como activo");
    }

    [PostgresFact]
    public async Task AC3_LaBaseRechazaParametrosFueraDeRangoOIncoherentesYUnaSegundaFila()
    {
        await using var cn = await SeedAsync();
        await ExecAsync(cn, LoadDdl());

        var casos = new Dictionary<string, string>
        {
            ["max_pdfs_per_part = 0"] = "ck_consolidado_export_settings_max_pdfs",
            ["item_slots = 7"] = "ck_consolidado_export_settings_item_slots",
            ["retry_delay_seconds = 4"] = "ck_consolidado_export_settings_retry_delay",
            ["item_lease_seconds = item_timeout_seconds"] = "ck_consolidado_export_settings_item_lease",
            ["item_lease_seconds = item_timeout_seconds - 1"] = "ck_consolidado_export_settings_item_lease",
        };
        foreach (var (set, check) in casos)
        {
            (await ConstraintOfViolationAsync(() => ExecAsync(cn, $"UPDATE tramites.consolidado_export_settings SET {set}")))
                .Should().Be(check, set);
        }

        (await ConstraintOfViolationAsync(() => ExecAsync(cn,
                "INSERT INTO tramites.consolidado_export_settings (id) VALUES (uuidv7())")))
            .Should().Be("uq_consolidado_export_settings_singleton");
    }

    [PostgresFact]
    public async Task AC4_TenantNuloSoloParaElOrigenSuperadmin()
    {
        await using var cn = await SeedAsync();

        (await ConstraintOfViolationAsync(() =>
                InsertBatchAsync(cn, ConsolidadoExportStatus.Completado, ConsolidadoExportOrigin.Tramites, sinTenant: true)))
            .Should().Be("ck_consolidado_export_batches_tenant_origin");
        (await ConstraintOfViolationAsync(() =>
                InsertBatchAsync(cn, ConsolidadoExportStatus.Completado, ConsolidadoExportOrigin.Superadmin)))
            .Should().Be("ck_consolidado_export_batches_tenant_origin", "el lote de Super Admin nunca lleva compañía");
        (await ConstraintOfViolationAsync(() =>
                InsertBatchAsync(cn, ConsolidadoExportStatus.Completado, ConsolidadoExportOrigin.Superadmin, sinTenant: true)))
            .Should().BeNull();
    }

    [PostgresFact]
    public async Task AC5_ElDownRetiraLasDosTablasSinTocarLasPreexistentes()
    {
        await using var cn = await SeedAsync();
        const string huella =
            """
            SELECT string_agg(f, ',' ORDER BY f) FROM (
              SELECT c.table_schema || '.' || c.table_name || '.' || c.column_name || ':' || c.data_type AS f
                FROM information_schema.columns c
               WHERE c.table_schema NOT IN ('pg_catalog', 'information_schema')
                 AND c.table_name NOT IN ('consolidado_export_settings', 'consolidado_export_batches')) x
            """;
        const string restricciones =
            """
            SELECT count(*) FROM pg_constraint k JOIN pg_namespace n ON n.oid = k.connamespace
             WHERE n.nspname NOT IN ('pg_catalog', 'information_schema')
            """;
        var down = new HU13367_ConsolidadoExportBatches().DownOperations.OfType<SqlOperation>().Single().Sql;

        await using var tx = await cn.BeginTransactionAsync(Ct);
        var antes = await ScalarAsync<string>(cn, huella);
        var restriccionesAntes = await ScalarAsync<long>(cn, restricciones);
        var restriccionesPropias = await ScalarAsync<long>(cn,
            "SELECT count(*) FROM pg_constraint WHERE conrelid IN ('tramites.consolidado_export_settings'::regclass, 'tramites.consolidado_export_batches'::regclass)");

        await ExecAsync(cn, down);

        (await ScalarAsync<long>(cn,
            """
            SELECT count(*) FROM information_schema.tables
             WHERE table_schema = 'tramites' AND table_name IN ('consolidado_export_settings', 'consolidado_export_batches')
            """)).Should().Be(0);
        (await ScalarAsync<long>(cn,
            "SELECT count(*) FROM pg_trigger WHERE tgname LIKE 'tr_consolidado_export_%'")).Should().Be(0);
        (await ScalarAsync<string>(cn, huella)).Should().Be(antes, "ninguna tabla preexistente cambia");
        (await ScalarAsync<long>(cn, restricciones)).Should().Be(restriccionesAntes - restriccionesPropias);

        // Reaplicar el Up tras el Down deja el esquema como estaba (reversible e idempotente).
        await ExecAsync(cn, LoadDdl());
        (await ScalarAsync<long>(cn, restricciones)).Should().Be(restriccionesAntes);
        (await ScalarAsync<long>(cn, "SELECT count(*) FROM tramites.consolidado_export_settings")).Should().Be(1);

        await tx.RollbackAsync(Ct);
    }

    [PostgresFact]
    public async Task AC6_ElOrganismoVaSoloYSiempreEnElOrigenOtBandeja()
    {
        await using var cn = await SeedAsync();

        (await ConstraintOfViolationAsync(() =>
                InsertBatchAsync(cn, ConsolidadoExportStatus.Completado, ConsolidadoExportOrigin.OtBandeja)))
            .Should().Be("ck_consolidado_export_batches_ot_origin", "un lote de la bandeja OT sin organismo");
        (await ConstraintOfViolationAsync(() =>
                InsertBatchAsync(cn, ConsolidadoExportStatus.Completado, ConsolidadoExportOrigin.Tramites, officeId: Office)))
            .Should().Be("ck_consolidado_export_batches_ot_origin", "otro origen con organismo");
        (await ConstraintOfViolationAsync(() =>
                InsertBatchAsync(cn, ConsolidadoExportStatus.Completado, ConsolidadoExportOrigin.OtBandeja, officeId: Office)))
            .Should().BeNull();
    }
}
