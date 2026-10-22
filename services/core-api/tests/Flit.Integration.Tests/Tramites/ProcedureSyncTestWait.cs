using Flit.Integration.Tests.Postgres;
using Npgsql;
using Xunit;

namespace Flit.Integration.Tests.Tramites;

/// <summary>
/// Épica #12737 — el feed solo entrega transacciones anteriores a <c>pg_snapshot_xmin</c>, que es del
/// SERVIDOR: en CI otros proyectos de prueba corren en paralelo contra el mismo Postgres y una transacción
/// suya retrasa ese límite. Antes de leer lo que se espera entregado, se espera a que el último sello quede
/// por debajo del límite (como le pasaría a Flito en su siguiente lectura).
/// </summary>
internal static class ProcedureSyncTestWait
{
    private static readonly TimeSpan Limite = TimeSpan.FromSeconds(30);

    public static async Task EsperarFeedEstableAsync(PostgresDatabaseFixture fixture)
    {
        var hasta = DateTime.UtcNow + Limite;
        await using var conn = await fixture.OpenConnectionAsync();
        while (true)
        {
            await using (var cmd = new NpgsqlCommand(
                "SELECT COALESCE(max(sync_xact), '0'::xid8) < pg_snapshot_xmin(pg_current_snapshot()) FROM tramites.procedure_instances",
                conn))
            {
                if ((bool)(await cmd.ExecuteScalarAsync(TestContext.Current.CancellationToken))!)
                {
                    return;
                }
            }

            if (DateTime.UtcNow > hasta)
            {
                throw new TimeoutException(
                    $"El feed no se estabilizó en {Limite.TotalSeconds} s: una transacción abierta retiene pg_snapshot_xmin.");
            }

            await Task.Delay(50, TestContext.Current.CancellationToken);
        }
    }
}
