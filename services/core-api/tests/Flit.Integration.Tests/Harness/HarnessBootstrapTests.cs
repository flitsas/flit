using Flit.Integration.Tests.Postgres;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Flit.Integration.Tests.Harness;

/// <summary>
/// HU #12319 — AC1 (arranque del arnés) y AC7 (misma versión mayor que producto).
/// <para>
/// Uso de ejemplo: <c>dotnet test tests/Flit.Integration.Tests</c> con un Postgres 16 alcanzable
/// (ver README). Estas pruebas verifican que la base efímera existe, es la que está conectada,
/// tiene TODAS las migraciones aplicadas y que la lista blanca de catálogos coincide con lo que las
/// migraciones realmente siembran.
/// </para>
/// </summary>
public sealed class HarnessBootstrapTests(PostgresDatabaseFixture fixture) : PostgresTestBase(fixture)
{
    private static readonly string[] ProtectedDatabases = ["flit_local", "flit_dev", "postgres"];

    /// <summary>Producto corre <c>postgres:16-alpine</c> (docker-compose / CI): el arnés debe hablar con la misma versión mayor.</summary>
    [PostgresFact]
    public async Task Motor_es_PostgreSQL_16_como_producto()
    {
        await using var connection = await Fixture.OpenConnectionAsync();

        connection.PostgreSqlVersion.Major.Should().Be(16,
            "el CI y docker-compose levantan postgres:16-alpine; una versión mayor distinta invalidaría las evidencias");
    }

    [PostgresFact]
    public async Task Base_efimera_tiene_nombre_flit_it_y_es_la_conectada()
    {
        Fixture.DatabaseName.Should().MatchRegex(@"^flit_it_\d{14}_[0-9a-f]{4}$");
        ProtectedDatabases.Should().NotContain(Fixture.DatabaseName);

        await using var connection = await Fixture.OpenConnectionAsync();
        await using var cmd = new NpgsqlCommand("SELECT current_database()", connection);

        (await cmd.ExecuteScalarAsync()).Should().Be(Fixture.DatabaseName);
    }

    /// <summary>La última migración del ensamblado es la última registrada en <c>__EFMigrationsHistory</c> y no queda ninguna pendiente.</summary>
    [PostgresFact]
    public async Task Todas_las_migraciones_EF_estan_aplicadas_antes_de_la_primera_prueba()
    {
        await using var ctx = NewContext();

        var pending = await ctx.Database.GetPendingMigrationsAsync();
        var applied = (await ctx.Database.GetAppliedMigrationsAsync()).ToList();
        var declared = ctx.Database.GetMigrations().ToList();

        pending.Should().BeEmpty();
        applied.Should().BeEquivalentTo(declared);
        Fixture.LastAppliedMigration.Should().Be(declared[^1]);
        Fixture.LastAppliedMigration.Should().Be("20260910140000_HU12406_HeadTenantTypesAndParentSnapshot",
            "es la última migración de la Feature #12254 al escribir esta HU (actualizada deliberadamente por #12406); si cambia, actualizar la aserción es deliberado");
    }

    /// <summary>
    /// La lista blanca es una constante documentada (AC2): debe ser EXACTAMENTE el conjunto de
    /// catálogos sembrados por migración menos las tablas no-catálogo, que se truncan a propósito.
    /// Si una migración nueva siembra un catálogo, esta prueba obliga a decidir si se preserva.
    /// </summary>
    [PostgresFact]
    public void Lista_blanca_de_catalogos_coincide_con_lo_que_siembran_las_migraciones()
    {
        var seeded = Fixture.SeededTablesAfterMigration;

        // Toda tabla preservada debe existir y estar sembrada; si no, la lista blanca tiene basura.
        PostgresDatabaseFixture.PreservedSeededTables.Should().BeSubsetOf(seeded,
            "una tabla en la lista blanca que las migraciones no siembran es una entrada obsoleta");

        // Lo sembrado que NO se preserva son exactamente las tablas no-catálogo conocidas.
        var truncatedSeeds = seeded.Except(PostgresDatabaseFixture.PreservedSeededTables).ToList();
        truncatedSeeds.Should().BeEquivalentTo(PostgresDatabaseFixture.KnownNonCatalogSeededTables,
            "una tabla sembrada nueva debe clasificarse a conciencia como catálogo (PreservedSeededTables) o no-catálogo (KnownNonCatalogSeededTables)");
    }

    [PostgresFact]
    public void Reset_no_incluye_la_historia_de_migraciones_ni_los_catalogos()
    {
        Fixture.ResettableTables.Should().NotContain(t => t.EndsWith("__EFMigrationsHistory", StringComparison.Ordinal));
        Fixture.ResettableTables.Should().NotIntersectWith(PostgresDatabaseFixture.PreservedSeededTables);
        Fixture.ResettableTables.Should().Contain("identity.tenants");
        Fixture.ResettableTables.Should().Contain("identity.tenant_hierarchy_audit");
    }
}
