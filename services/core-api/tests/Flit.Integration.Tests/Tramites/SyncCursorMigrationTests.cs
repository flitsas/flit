using Flit.Infrastructure.Migrations;
using Flit.Integration.Tests.Postgres;
using FluentAssertions;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Npgsql;
using Xunit;

namespace Flit.Integration.Tests.Tramites;

/// <summary>
/// HU #13076 — DDL 126 contra Postgres real: el recorrido por (transacción, versión) y el arranque por
/// fecha tienen índice, y la migración se puede reaplicar y revertir. Como en la HU #13075, los planes se
/// piden con <c>enable_seqscan</c> y <c>enable_bitmapscan</c> desactivados: se prueba que el índice sirve a esa consulta exacta.
/// </summary>
public sealed class SyncCursorMigrationTests(PostgresDatabaseFixture fixture) : PostgresTestBase(fixture)
{
    [PostgresFact]
    public async Task ElRecorridoPorTransaccionYVersionUsaSuIndice()
    {
        var plan = await PlanAsync("""
            SELECT id FROM tramites.procedure_instances
             WHERE (COALESCE(sync_xact, '0'::xid8), sync_version) > ('10'::xid8, 5)
             ORDER BY COALESCE(sync_xact, '0'::xid8), sync_version LIMIT 500
            """);

        plan.Should().Contain("ix_procedure_instances_sync_cursor").And.NotContain("Sort");
    }

    [PostgresFact]
    public async Task ElArranquePorFechaUsaSuIndice()
    {
        var plan = await PlanAsync(
            "SELECT id FROM tramites.procedure_instances WHERE sync_changed_at >= now() - interval '1 hour'");

        plan.Should().Contain("ix_procedure_instances_sync_changed");
    }

    [PostgresFact]
    public async Task ElUpSeReaplicaYElDownQuitaLosIndices()
    {
        await EjecutarAsync(new HU13076_SyncCursor().UpOperations.OfType<SqlOperation>().Single().Sql);
        (await IndicesAsync()).Should().Be(2);

        await EjecutarAsync(new HU13076_SyncCursor().DownOperations.OfType<SqlOperation>().Single().Sql);
        (await IndicesAsync()).Should().Be(0);

        await EjecutarAsync(new HU13076_SyncCursor().UpOperations.OfType<SqlOperation>().Single().Sql);
        (await IndicesAsync()).Should().Be(2);
    }

    private async Task<long> IndicesAsync()
    {
        await using var conn = await Fixture.OpenConnectionAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT count(*) FROM pg_indexes WHERE schemaname = 'tramites' AND indexname IN ('ix_procedure_instances_sync_cursor', 'ix_procedure_instances_sync_changed')",
            conn);
        return (long)(await cmd.ExecuteScalarAsync(TestContext.Current.CancellationToken))!;
    }

    private async Task<string> PlanAsync(string consulta)
    {
        await using var conn = await Fixture.OpenConnectionAsync();
        await using (var off = new NpgsqlCommand("SET enable_seqscan = off; SET enable_bitmapscan = off", conn))
            await off.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);

        await using var cmd = new NpgsqlCommand("EXPLAIN " + consulta, conn);
        var lineas = new List<string>();
        await using var r = await cmd.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        while (await r.ReadAsync(TestContext.Current.CancellationToken))
            lineas.Add(r.GetString(0));
        return string.Join('\n', lineas);
    }

    private async Task EjecutarAsync(string sql)
    {
        await using var conn = await Fixture.OpenConnectionAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        await cmd.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }
}
