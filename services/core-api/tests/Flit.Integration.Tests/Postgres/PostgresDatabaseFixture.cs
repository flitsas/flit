using System.Globalization;
using System.Security.Cryptography;
using Flit.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;
using Xunit;

namespace Flit.Integration.Tests.Postgres;

/// <summary>
/// HU #12319 — ciclo de vida de la base efímera del arnés (AC1):
/// <list type="bullet">
///   <item>Resuelve la conexión administrativa (<see cref="PostgresConnectionResolver"/>).</item>
///   <item>Crea <c>flit_it_&lt;yyyyMMddHHmmss&gt;_&lt;4hex&gt;</c> y le aplica TODAS las migraciones EF
///   con el <see cref="FlitDbContext"/> real y Npgsql antes de la primera prueba.</item>
///   <item>Entre pruebas, <see cref="ResetAsync"/> deja la base como recién migrada (AC2).</item>
///   <item>Al terminar la colección, <c>DROP DATABASE … WITH (FORCE)</c>.</item>
/// </list>
/// Nunca toca la base de trabajo (<c>flit_local</c> / <c>flit_dev</c>): la conexión administrativa
/// apunta a <c>postgres</c> y solo sirve para crear y borrar la efímera.
/// <para>
/// <b>Estrategia de reset elegida: <c>TRUNCATE … RESTART IDENTITY CASCADE</c></b> de todas las
/// tablas de usuario salvo <c>__EFMigrationsHistory</c> y la lista blanca
/// <see cref="PreservedSeededTables"/>. Se descartó la transacción con rollback por prueba porque
/// (a) deja invisibles los datos a cualquier conexión distinta a la de la prueba (repositorios que
/// abren la suya, <c>WebApplicationFactory</c>), (b) tras el primer error de constraint la
/// transacción queda abortada y cada aserción de AC3 necesitaría un savepoint, y (c) los triggers
/// de inmutabilidad (<c>tr_tenant_hierarchy_audit_immutable</c>, bitácoras append-only) rechazan
/// <c>DELETE</c> fila a fila pero NO <c>TRUNCATE</c> (solo disparan triggers <c>ON TRUNCATE</c>, que
/// el esquema no define), así que el truncado sí es viable.
/// </para>
/// </summary>
public sealed class PostgresDatabaseFixture : IAsyncLifetime
{
    /// <summary>
    /// Lista blanca de tablas que <see cref="ResetAsync"/> NO trunca porque las siembra una
    /// migración (sin condición de entorno) y el producto las trata como catálogo: sin
    /// <c>tenant_id</c>, sin FK hacia datos de trabajo. Formato <c>schema.tabla</c>.
    /// <para>
    /// Es una constante documentada (AC2) y se verifica en dos sitios: al arrancar, el fixture falla
    /// si una tabla preservada referencia por FK a una truncada (el <c>CASCADE</c> la vaciaría en
    /// silencio); y <c>HarnessBootstrapTests</c> comprueba que <b>lo sembrado tras migrar</b> es
    /// exactamente esta lista más <see cref="KnownNonCatalogSeededTables"/>. Una migración nueva que
    /// siembre algo obliga a clasificarlo a conciencia.
    /// </para>
    /// <para>
    /// Los <b>dev-seeds</b> condicionados a <c>ASPNETCORE_ENVIRONMENT=Development</c> /
    /// <c>FLIT_DEV_SEED</c> (<c>12-HU10200</c>, <c>16-HU10133</c>, <c>21-HU10240</c>, <c>45-ricaurte</c>…)
    /// NO corren en la efímera: el fixture neutraliza esas variables mientras migra para que la base
    /// nazca igual en el portátil de cualquiera y en CI. <c>catalogs.transit_offices</c> tampoco está
    /// aquí porque su seed (<c>27-HU10659</c>) no está enlazado a ninguna migración: nace vacía y
    /// cada prueba siembra las OT que necesita.
    /// </para>
    /// </summary>
    public static readonly IReadOnlySet<string> PreservedSeededTables = new HashSet<string>(StringComparer.Ordinal)
    {
        // Catálogos globales (checklist §9.1: sin tenant_id, sin RLS).
        "catalogs.vehicle_colors",
        "catalogs.vehicle_service_types",
        "catalogs.vehicle_bodyworks",
        "catalogs.rejection_reasons",
        // Catálogo de tipos de trámite y su configuración (F08, seeds 38-F08 y sucesivos).
        "tramites.procedure_types",
        "tramites.procedure_steps",
        "tramites.procedure_sections",
        "tramites.form_fields",
        "tramites.document_types",
        "tramites.procedure_document_requirements",
        "tramites.procedure_type_sources",
        "tramites.external_data_sources",
        "tramites.consultation_templates",
        "tramites.procedure_entities",
        "tramites.conformation_rules",
        "tramites.vehicle_classification_fur",
        // Interruptores globales de la jerarquía (HU #12323): nacen encendidos por seed.
        "identity.hierarchy_switches",
    };

    /// <summary>
    /// Tablas que quedan NO vacías tras migrar pero que NO son catálogo y por eso SÍ se truncan:
    /// <c>identity.tenants</c> (compañías mock de <c>SeedMockCompanies</c>, datos de trabajo),
    /// <c>admin.notification_test_settings</c> (fila de configuración de pruebas de correo) y
    /// <c>audit.audit_logs</c> (rastro que dejan los triggers de auditoría al sembrar). Documentadas
    /// para que <c>HarnessBootstrapTests</c> detecte cualquier tabla sembrada nueva sin clasificar.
    /// </summary>
    public static readonly IReadOnlySet<string> KnownNonCatalogSeededTables = new HashSet<string>(StringComparer.Ordinal)
    {
        "identity.tenants",
        "admin.notification_test_settings",
        "audit.audit_logs",
    };

    private const string MigrationsHistoryTable = "__EFMigrationsHistory";

    private PostgresConnectionSource? _source;
    private string? _resetSql;

    /// <summary>Nombre de la base efímera (<c>flit_it_…</c>).</summary>
    public string DatabaseName { get; private set; } = string.Empty;

    /// <summary>Connection string a la base efímera (uso interno del arnés; no imprimir).</summary>
    public string ConnectionString { get; private set; } = string.Empty;

    /// <summary>Host/puerto/usuario/base sin contraseña, para logs e informes.</summary>
    public string SafeDescription => PostgresConnectionResolver.Describe(ConnectionString);

    /// <summary>De dónde salió la conexión administrativa (env var o appsettings).</summary>
    public string ConnectionOrigin => _source?.Origin ?? "(sin resolver)";

    /// <summary>Tablas (<c>schema.tabla</c>) que <see cref="ResetAsync"/> trunca.</summary>
    public IReadOnlyList<string> ResettableTables { get; private set; } = [];

    /// <summary>Tablas (<c>schema.tabla</c>) que quedaron NO vacías justo después de migrar.</summary>
    public IReadOnlyList<string> SeededTablesAfterMigration { get; private set; } = [];

    /// <summary>Última migración registrada en <c>__EFMigrationsHistory</c>.</summary>
    public string LastAppliedMigration { get; private set; } = string.Empty;

    /// <summary>Cuántas veces se ha ejecutado <see cref="ResetAsync"/> (diagnóstico).</summary>
    public int ResetCount { get; private set; }

    public bool IsInitialized { get; private set; }

    public async ValueTask InitializeAsync()
    {
        if (!PostgresAvailability.IsAvailable)
        {
            if (PostgresAvailability.IsCi)
            {
                throw new InvalidOperationException(
                    "CI sin PostgreSQL alcanzable: la suite de integración no puede omitirse en CI (AC5). " +
                    PostgresAvailability.UnavailableDetail);
            }

            return; // fuera de CI: [PostgresFact] ya omitió cada prueba con PostgresAvailability.SkipReason
        }

        _source = PostgresAvailability.Source!;
        DatabaseName = NewEphemeralName();
        ConnectionString = PostgresConnectionResolver.WithDatabase(_source.AdminConnectionString, DatabaseName);

        await ExecuteAdminAsync($"CREATE DATABASE \"{DatabaseName}\"");

        try
        {
            await MigrateWithoutDevSeedsAsync();

            await DiscoverSchemaAsync();
            IsInitialized = true;
        }
        catch
        {
            await DropDatabaseSafelyAsync();
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (string.IsNullOrEmpty(DatabaseName))
        {
            return;
        }

        await DropDatabaseSafelyAsync();
    }

    /// <summary>
    /// Contexto EF real sobre la base efímera, configurado como el de producto
    /// (<c>UseNpgsql</c> + <c>UseSnakeCaseNamingConvention</c>) menos el reintento automático, que
    /// enmascararía errores del motor que aquí se afirman.
    /// </summary>
    public FlitDbContext CreateDbContext()
    {
        EnsureInitialized();
        return BuildContext();
    }

    private FlitDbContext BuildContext()
    {
        var options = new DbContextOptionsBuilder<FlitDbContext>()
            .UseNpgsql(ConnectionString)
            .UseSnakeCaseNamingConvention()
            .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning))
            .Options;
        return new FlitDbContext(options);
    }

    /// <summary>Conexión Npgsql abierta a la base efímera, para SQL directo fuera de EF.</summary>
    public Task<NpgsqlConnection> OpenConnectionAsync()
    {
        EnsureInitialized();
        return OpenEphemeralAsync();
    }

    private async Task<NpgsqlConnection> OpenEphemeralAsync()
    {
        var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        return connection;
    }

    /// <summary>
    /// Deja la base como recién migrada (AC2): un solo <c>TRUNCATE … RESTART IDENTITY CASCADE</c>
    /// sobre <see cref="ResettableTables"/> y, en el mismo round-trip, los interruptores de
    /// <c>identity.hierarchy_switches</c> vuelven a <c>is_enabled = true</c> (su seed). Lo invoca
    /// <see cref="PostgresTestBase"/> antes de cada prueba; también puede llamarse a mano.
    /// <para>
    /// Las demás tablas de <see cref="PreservedSeededTables"/> se asumen de solo lectura para las
    /// pruebas: una prueba que las mute debe restaurarlas ella misma.
    /// </para>
    /// </summary>
    public async Task ResetAsync()
    {
        EnsureInitialized();
        await using var connection = await OpenConnectionAsync();
        await using var command = new NpgsqlCommand(_resetSql, connection);
        await command.ExecuteNonQueryAsync();
        ResetCount++;
    }

    /// <summary>
    /// Aplica todas las migraciones con los dev-seeds condicionales APAGADOS: varias migraciones
    /// miran <c>ASPNETCORE_ENVIRONMENT</c> / <c>FLIT_DEV_SEED</c> en tiempo de ejecución, y un
    /// portátil con <c>Development</c> exportado produciría una efímera distinta a la del CI.
    /// Las variables se restauran al terminar.
    /// </summary>
    private async Task MigrateWithoutDevSeedsAsync()
    {
        var previousEnvironment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");
        var previousDevSeed = Environment.GetEnvironmentVariable("FLIT_DEV_SEED");
        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "IntegrationTests");
        Environment.SetEnvironmentVariable("FLIT_DEV_SEED", null);
        try
        {
            await using var ctx = BuildContext();
            await ctx.Database.MigrateAsync();
        }
        finally
        {
            Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", previousEnvironment);
            Environment.SetEnvironmentVariable("FLIT_DEV_SEED", previousDevSeed);
        }
    }

    private static string NewEphemeralName()
    {
        var stamp = DateTime.UtcNow.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture);
        var hex = RandomNumberGenerator.GetHexString(4, lowercase: true);
        return $"flit_it_{stamp}_{hex}";
    }

    private void EnsureInitialized()
    {
        if (!IsInitialized)
        {
            throw new InvalidOperationException(
                "El arnés no está inicializado: no hay PostgreSQL alcanzable o la migración falló. " +
                (PostgresAvailability.UnavailableDetail ?? PostgresAvailability.SkipReason));
        }
    }

    private async Task DiscoverSchemaAsync()
    {
        await using var connection = await OpenEphemeralAsync();

        var allTables = new List<string>();
        await using (var cmd = new NpgsqlCommand(
            """
            SELECT n.nspname, c.relname
              FROM pg_class c
              JOIN pg_namespace n ON n.oid = c.relnamespace
             WHERE c.relkind IN ('r', 'p')
               AND NOT c.relispartition
               AND n.nspname NOT IN ('pg_catalog', 'information_schema')
               AND n.nspname NOT LIKE 'pg_toast%'
             ORDER BY 1, 2
            """,
            connection))
        await using (var reader = await cmd.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                var table = reader.GetString(1);
                if (table == MigrationsHistoryTable)
                {
                    continue;
                }

                allTables.Add($"{reader.GetString(0)}.{table}");
            }
        }

        // Guardia contra el CASCADE: si una tabla preservada referenciara por FK a una truncada,
        // TRUNCATE CASCADE la vaciaría en silencio. Mejor fallar al arrancar con nombre y apellido.
        var offending = new List<string>();
        await using (var cmd = new NpgsqlCommand(
            """
            SELECT n1.nspname || '.' || c1.relname AS referencing,
                   n2.nspname || '.' || c2.relname AS referenced
              FROM pg_constraint k
              JOIN pg_class c1 ON c1.oid = k.conrelid
              JOIN pg_namespace n1 ON n1.oid = c1.relnamespace
              JOIN pg_class c2 ON c2.oid = k.confrelid
              JOIN pg_namespace n2 ON n2.oid = c2.relnamespace
             WHERE k.contype = 'f'
            """,
            connection))
        await using (var reader = await cmd.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                var referencing = reader.GetString(0);
                var referenced = reader.GetString(1);
                if (PreservedSeededTables.Contains(referencing) && !PreservedSeededTables.Contains(referenced))
                {
                    offending.Add($"{referencing} -> {referenced}");
                }
            }
        }

        if (offending.Count > 0)
        {
            throw new InvalidOperationException(
                "PreservedSeededTables inconsistente: estas tablas preservadas referencian por FK a tablas que se truncan " +
                "(TRUNCATE CASCADE las vaciaría): " + string.Join(", ", offending));
        }

        var seeded = new List<string>();
        foreach (var table in allTables)
        {
            await using var cmd = new NpgsqlCommand($"SELECT EXISTS (SELECT 1 FROM {Quote(table)})", connection);
            if (await cmd.ExecuteScalarAsync() is true)
            {
                seeded.Add(table);
            }
        }

        SeededTablesAfterMigration = seeded;
        ResettableTables = allTables.Where(t => !PreservedSeededTables.Contains(t)).ToList();
        // Un solo round-trip, todo del lado del servidor: se truncan SOLO las tablas de trabajo que
        // tienen filas (TRUNCATE de ~150 tablas vacías cuesta segundos por los archivos que reescribe;
        // comprobar EXISTS es gratis) y las preservadas mutables vuelven a su seed (los interruptores
        // de HU #12323 nacen encendidos).
        var quotedList = string.Join(", ", ResettableTables.Select(t => "'" + Quote(t).Replace("'", "''", StringComparison.Ordinal) + "'"));
        _resetSql =
            $$"""
            DO $$
            DECLARE
                t text;
                has_rows boolean;
                nonempty text[] := '{}';
            BEGIN
                FOREACH t IN ARRAY ARRAY[{{quotedList}}] LOOP
                    EXECUTE format('SELECT EXISTS (SELECT 1 FROM %s)', t) INTO has_rows;
                    IF has_rows THEN
                        nonempty := nonempty || t;
                    END IF;
                END LOOP;
                IF array_length(nonempty, 1) > 0 THEN
                    EXECUTE 'TRUNCATE TABLE ' || array_to_string(nonempty, ', ') || ' RESTART IDENTITY CASCADE';
                END IF;
            END $$;
            UPDATE identity.hierarchy_switches
               SET is_enabled = true, updated_by = NULL
             WHERE NOT is_enabled OR updated_by IS NOT NULL;
            """;

        await using (var cmd = new NpgsqlCommand(
            $"SELECT migration_id FROM \"{MigrationsHistoryTable}\" ORDER BY migration_id DESC LIMIT 1",
            connection))
        {
            LastAppliedMigration = (string?)await cmd.ExecuteScalarAsync() ?? string.Empty;
        }
    }

    private static string Quote(string schemaDotTable)
    {
        var dot = schemaDotTable.IndexOf('.', StringComparison.Ordinal);
        return $"\"{schemaDotTable[..dot]}\".\"{schemaDotTable[(dot + 1)..]}\"";
    }

    private async Task ExecuteAdminAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(_source!.AdminConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    private async Task DropDatabaseSafelyAsync()
    {
        // Cierra las conexiones del pool a la efímera antes del DROP; FORCE remata las que queden.
        NpgsqlConnection.ClearPool(new NpgsqlConnection(ConnectionString));
        await ExecuteAdminAsync($"DROP DATABASE IF EXISTS \"{DatabaseName}\" WITH (FORCE)");
    }
}
