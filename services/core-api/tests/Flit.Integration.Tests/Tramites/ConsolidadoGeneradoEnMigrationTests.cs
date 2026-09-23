using Flit.Infrastructure.Migrations;
using Flit.Integration.Tests.Postgres;
using FluentAssertions;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Npgsql;
using Xunit;

namespace Flit.Integration.Tests.Tramites;

/// <summary>
/// HU #12790 (Épica #12760) AC1, contra Postgres real: tras aplicar TODAS las migraciones (el
/// fixture migra la base efímera) <c>tramites.procedure_instances</c> tiene
/// <c>consolidado_wizard_generado_en</c> y <c>consolidado_maestro_generado_en</c> como
/// <c>timestamp with time zone</c> nulables y sin default, y reaplicar el Up de la migración no
/// produce error.
/// <para>
/// Uso de ejemplo: <c>new HU12790_ConsolidadoGeneradoEn().UpOperations.OfType&lt;SqlOperation&gt;().Single().Sql</c>
/// ejecutado dos veces sobre la misma base.
/// </para>
/// </summary>
public sealed class ConsolidadoGeneradoEnMigrationTests(PostgresDatabaseFixture fixture)
    : PostgresTestBase(fixture)
{
    private static readonly string[] Columnas = ["consolidado_maestro_generado_en", "consolidado_wizard_generado_en"];

    [PostgresFact]
    public async Task AC1_TrasMigrar_LasColumnasExistenComoTimestamptzNulablesSinDefault()
    {
        await using var conn = await Fixture.OpenConnectionAsync();

        var columnas = await LeerColumnasAsync(conn);

        columnas.Select(c => c.Nombre).Should().BeEquivalentTo(Columnas);
        columnas.Should().AllSatisfy(c =>
        {
            c.Tipo.Should().Be("timestamp with time zone");
            c.Nulable.Should().Be("YES");
            c.Default.Should().BeNull("AC5: los históricos quedan en null");
        });
    }

    [PostgresFact]
    public async Task AC1_ReaplicarElUp_NoProduceError()
    {
        var up = new HU12790_ConsolidadoGeneradoEn().UpOperations.OfType<SqlOperation>().Single().Sql;
        await using var conn = await Fixture.OpenConnectionAsync();

        for (var i = 0; i < 2; i++)
        {
            await using var cmd = new NpgsqlCommand(up, conn);
            var reaplicar = async () => await cmd.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
            await reaplicar.Should().NotThrowAsync();
        }

        (await LeerColumnasAsync(conn)).Should().HaveCount(2);
    }

    private static async Task<List<(string Nombre, string Tipo, string Nulable, string? Default)>> LeerColumnasAsync(
        NpgsqlConnection conn)
    {
        const string sql = """
            SELECT column_name, data_type, is_nullable, column_default
              FROM information_schema.columns
             WHERE table_schema = 'tramites'
               AND table_name = 'procedure_instances'
               AND column_name = ANY(@cols)
             ORDER BY column_name
            """;
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("cols", Columnas);
        await using var reader = await cmd.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        var result = new List<(string, string, string, string?)>();
        while (await reader.ReadAsync(TestContext.Current.CancellationToken))
        {
            result.Add((reader.GetString(0), reader.GetString(1), reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3)));
        }
        return result;
    }
}
