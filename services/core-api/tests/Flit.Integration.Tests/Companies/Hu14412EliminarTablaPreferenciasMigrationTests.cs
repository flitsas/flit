using Flit.Infrastructure.Persistence;
using Flit.Integration.Tests.Postgres;
using FluentAssertions;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Npgsql;
using Xunit;

namespace Flit.Integration.Tests.Companies;

/// <summary>
/// HU #14412 (Feature #14408) — eliminar <c>admin.company_document_params</c>, contra PostgreSQL real.
/// El arnés ya aplicó todas las migraciones en orden sobre una base nueva, así que el Down se ejecuta dentro de la
/// prueba y un <c>finally</c> vuelve a aplicar el Up: la colección comparte la base y otra prueba no debe heredar la tabla.
/// <para>La migración se busca por nombre para que sus pruebas fallen con su motivo real antes de existir.</para>
/// <para>Uso de ejemplo: <c>await EjecutarAsync(conn, Migracion().DownOperations)</c> recrea la tabla vacía.</para>
/// </summary>
public sealed class Hu14412EliminarTablaPreferenciasMigrationTests(PostgresDatabaseFixture fixture)
    : PostgresTestBase(fixture)
{
    private const string MigrationId = "20261106120000_HU14412_EliminarTablaPreferenciasDocumentos";
    private const string ClaseMigracion = "Flit.Infrastructure.Migrations.HU14412_EliminarTablaPreferenciasDocumentos";
    private const string Tabla = "company_document_params";

    /// <summary>Tablas de la base, sin la de esta HU: ninguna se crea ni desaparece con el Up.</summary>
    private static readonly string ConsultaTablas =
        $"""
        SELECT table_schema || '.' || table_name
          FROM information_schema.tables
         WHERE table_schema NOT IN ('pg_catalog', 'information_schema')
           AND table_name <> '{Tabla}'
         ORDER BY 1
        """;

    /// <summary>Columnas de las tablas vecinas: todo <c>admin</c> y los requisitos por organismo.</summary>
    private static readonly string ConsultaColumnasVecinas =
        $"""
        SELECT table_schema || '.' || table_name || '.' || column_name || ' ' || data_type || ' ' || is_nullable
          FROM information_schema.columns
         WHERE (table_schema = 'admin' AND table_name <> '{Tabla}')
            OR (table_schema = 'tramites' AND table_name = 'document_requirement_overrides')
         ORDER BY 1
        """;

    /// <summary>Índices de las mismas tablas vecinas.</summary>
    private static readonly string ConsultaIndicesVecinos =
        $"""
        SELECT indexdef
          FROM pg_indexes
         WHERE (schemaname = 'admin' AND tablename <> '{Tabla}')
            OR (schemaname = 'tramites' AND tablename = 'document_requirement_overrides')
         ORDER BY indexname
        """;

    // ── AC1: la tabla desaparece ────────────────────────────────────────────────────────────
    [PostgresFact]
    public async Task AC1_TrasMigrar_LaTablaNoExiste()
    {
        await using var conn = await Fixture.OpenConnectionAsync();

        (await ExisteTablaAsync(conn)).Should().BeFalse();
    }

    // ── AC2: las filas se van sin exportarse ────────────────────────────────────────────────
    [PostgresFact]
    public async Task AC2_ConFilasEnLaTabla_ElUp_LasElimina_SinCrearOtraTabla()
    {
        var migracion = Migracion();
        await using var conn = await Fixture.OpenConnectionAsync();
        try
        {
            await EjecutarAsync(conn, migracion.DownOperations);
            await ComandoAsync(conn,
                $"INSERT INTO admin.{Tabla} (tenant_id, document_type_code, state) VALUES (uuidv7(), 'SOAT', 'OCULTO')");
            (await EscalarAsync(conn, $"SELECT count(*) FROM admin.{Tabla}")).Should().Be(1L);
            var tablasAntes = await ListaAsync(conn, ConsultaTablas);

            await EjecutarAsync(conn, migracion.UpOperations);

            (await ExisteTablaAsync(conn)).Should().BeFalse();
            (await ListaAsync(conn, ConsultaTablas)).Should().Equal(tablasAntes, "las filas no se copian a otro mecanismo");
        }
        finally
        {
            await EjecutarAsync(conn, migracion.UpOperations);
        }
    }

    // ── AC3: base nueva y base sin la tabla ─────────────────────────────────────────────────
    [PostgresFact]
    public async Task AC3_BaseNueva_AplicaTodasLasMigraciones_YRegistraEstaEnElHistorial()
    {
        await using var conn = await Fixture.OpenConnectionAsync();

        var registrada = await EscalarAsync(conn,
            $"""SELECT count(*) FROM "__EFMigrationsHistory" WHERE migration_id = '{MigrationId}'""");

        registrada.Should().Be(1L, "sin [DbContext] y [Migration] EF no la descubre y nunca se aplicaría");
        (await ExisteTablaAsync(conn)).Should().BeFalse();
    }

    [PostgresFact]
    public async Task AC3_SinLaTabla_ReaplicarElUp_TerminaSinError()
    {
        var migracion = Migracion();
        await using var conn = await Fixture.OpenConnectionAsync();

        var reaplicar = async () => await EjecutarAsync(conn, migracion.UpOperations);

        await reaplicar.Should().NotThrowAsync();
        (await ExisteTablaAsync(conn)).Should().BeFalse();
    }

    // ── Sin CASCADE: una dependencia inesperada falla visible en vez de borrarse en silencio ─
    [PostgresFact]
    public async Task SinCascade_ConUnaVistaDependiente_ElUp_FallaYNoBorraNada()
    {
        var migracion = Migracion();
        await using var conn = await Fixture.OpenConnectionAsync();
        try
        {
            await EjecutarAsync(conn, migracion.DownOperations);
            await ComandoAsync(conn, $"CREATE VIEW admin.v_it_14412 AS SELECT id FROM admin.{Tabla}");

            var up = async () => await EjecutarAsync(conn, migracion.UpOperations);

            (await up.Should().ThrowAsync<PostgresException>()).Which.SqlState
                .Should().Be(PostgresErrorCodes.DependentObjectsStillExist);
            (await ExisteTablaAsync(conn)).Should().BeTrue();
        }
        finally
        {
            await ComandoAsync(conn, "DROP VIEW IF EXISTS admin.v_it_14412");
            await EjecutarAsync(conn, migracion.UpOperations);
        }
    }

    // ── AC4: el Down recrea según el DDL 24, sin filas ──────────────────────────────────────
    [PostgresFact]
    public async Task AC4_ElDown_RecreaTablaRestriccionesEIndices_SegunElDdl24_SinFilas()
    {
        var migracion = Migracion();
        await using var conn = await Fixture.OpenConnectionAsync();
        try
        {
            await EjecutarAsync(conn, migracion.DownOperations);

            (await EscalarAsync(conn, $"SELECT count(*) FROM admin.{Tabla}")).Should().Be(0L);
            (await ListaAsync(conn,
                $"""
                SELECT column_name || ' ' || data_type || ' ' || is_nullable
                  FROM information_schema.columns
                 WHERE table_schema = 'admin' AND table_name = '{Tabla}'
                 ORDER BY ordinal_position
                """)).Should().Equal(
                "id uuid NO",
                "tenant_id uuid NO",
                "document_type_code character varying NO",
                "state character varying NO",
                "created_at timestamp with time zone NO",
                "created_by uuid YES",
                "updated_at timestamp with time zone YES",
                "updated_by uuid YES");
            (await ListaAsync(conn,
                $"SELECT indexdef FROM pg_indexes WHERE schemaname = 'admin' AND tablename = '{Tabla}' ORDER BY indexname"))
                .Should().Equal(
                    $"CREATE INDEX ix_{Tabla}_tenant ON admin.{Tabla} USING btree (tenant_id)",
                    $"CREATE UNIQUE INDEX pk_{Tabla} ON admin.{Tabla} USING btree (id)",
                    $"CREATE UNIQUE INDEX uq_{Tabla} ON admin.{Tabla} USING btree (tenant_id, document_type_code)");
            (await ListaAsync(conn,
                $"SELECT conname || ' ' || contype::text FROM pg_constraint WHERE conrelid = 'admin.{Tabla}'::regclass ORDER BY conname"))
                .Should().Equal($"ck_{Tabla}_state c", $"pk_{Tabla} p", $"uq_{Tabla} u");

            var estadoInvalido = async () => await ComandoAsync(conn,
                $"INSERT INTO admin.{Tabla} (tenant_id, document_type_code, state) VALUES (uuidv7(), 'SOAT', 'OTRO')");
            (await estadoInvalido.Should().ThrowAsync<PostgresException>()).Which.SqlState
                .Should().Be(PostgresErrorCodes.CheckViolation);
        }
        finally
        {
            await EjecutarAsync(conn, migracion.UpOperations);
        }
    }

    // ── AC9: las tablas vecinas no cambian ──────────────────────────────────────────────────
    [PostgresFact]
    public async Task AC9_ElUp_NoToca_LosRequisitosPorOrganismo_NiLasDemasTablasDeAdmin()
    {
        var migracion = Migracion();
        var organismo = Guid.CreateVersion7();
        await using var conn = await Fixture.OpenConnectionAsync();
        try
        {
            await EjecutarAsync(conn, migracion.DownOperations);
            await ComandoAsync(conn,
                $"""
                INSERT INTO tramites.document_requirement_overrides
                    (procedure_type_id, document_type_id, transit_office_id, requirement_state)
                SELECT (SELECT id FROM tramites.procedure_types ORDER BY code LIMIT 1),
                       (SELECT id FROM tramites.document_types ORDER BY code LIMIT 1),
                       '{organismo}', 'OPTIONAL'
                """);
            var columnas = await ListaAsync(conn, ConsultaColumnasVecinas);
            var indices = await ListaAsync(conn, ConsultaIndicesVecinos);
            columnas.Should().NotBeEmpty();
            indices.Should().NotBeEmpty();

            await EjecutarAsync(conn, migracion.UpOperations);

            (await ListaAsync(conn, ConsultaColumnasVecinas)).Should().Equal(columnas);
            (await ListaAsync(conn, ConsultaIndicesVecinos)).Should().Equal(indices);
            (await EscalarAsync(conn,
                $"SELECT count(*) FROM tramites.document_requirement_overrides WHERE transit_office_id = '{organismo}'"))
                .Should().Be(1L, "los requisitos por organismo conservan sus filas");
        }
        finally
        {
            await ComandoAsync(conn,
                $"DELETE FROM tramites.document_requirement_overrides WHERE transit_office_id = '{organismo}'");
            await EjecutarAsync(conn, migracion.UpOperations);
        }
    }

    private static Migration Migracion()
    {
        var tipo = typeof(FlitDbContext).Assembly.GetType(ClaseMigracion);
        tipo.Should().NotBeNull($"la migración {ClaseMigracion} debe existir");
        return (Migration)Activator.CreateInstance(tipo!)!;
    }

    private static async Task EjecutarAsync(NpgsqlConnection conn, IReadOnlyList<MigrationOperation> operaciones)
    {
        foreach (var sql in operaciones.OfType<SqlOperation>().Select(o => o.Sql))
        {
            await ComandoAsync(conn, sql);
        }
    }

    private static async Task ComandoAsync(NpgsqlConnection conn, string sql)
    {
        await using var cmd = new NpgsqlCommand(sql, conn);
        await cmd.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }

    private static async Task<object?> EscalarAsync(NpgsqlConnection conn, string sql)
    {
        await using var cmd = new NpgsqlCommand(sql, conn);
        return await cmd.ExecuteScalarAsync(TestContext.Current.CancellationToken);
    }

    private static async Task<bool> ExisteTablaAsync(NpgsqlConnection conn) =>
        await EscalarAsync(conn, $"SELECT to_regclass('admin.{Tabla}') IS NOT NULL") is true;

    private static async Task<List<string>> ListaAsync(NpgsqlConnection conn, string sql)
    {
        await using var cmd = new NpgsqlCommand(sql, conn);
        await using var reader = await cmd.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        var filas = new List<string>();
        while (await reader.ReadAsync(TestContext.Current.CancellationToken))
        {
            filas.Add(reader.GetString(0));
        }

        return filas;
    }
}
