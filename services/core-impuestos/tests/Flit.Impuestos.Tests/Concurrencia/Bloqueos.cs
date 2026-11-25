using Npgsql;

namespace Flit.Impuestos.Tests.Concurrencia;

/// <summary>
/// Ayudas para las pruebas de carreras (HU #14694, revisión previa al PR): sesiones de la base efímera que esperan un bloqueo y
/// una transacción propia de la prueba que retiene uno.
/// </summary>
/// <example>
/// <code>
/// (await Bloqueos.EsperarSesionesBloqueadasAsync(api.BaseDeDatos, 1, TimeSpan.FromSeconds(10), ct)).Should().BeTrue();
/// </code>
/// </example>
internal static class Bloqueos
{
    /// <summary>Candado global del crédito (el mismo texto que usa <c>DespachadorCredito</c>).</summary>
    public const string SqlCandadoCredito = "SELECT pg_advisory_xact_lock(hashtextextended('impuestos:credito', 0))";

    /// <summary>¿Llegan a haber <paramref name="sesiones"/> sesiones de esta base esperando un bloqueo antes de <paramref name="maximo"/>?</summary>
    public static async Task<bool> EsperarSesionesBloqueadasAsync(string conexion, int sesiones, TimeSpan maximo, CancellationToken ct)
    {
        await using var c = new NpgsqlConnection(conexion);
        await c.OpenAsync(ct);
        var limite = DateTime.UtcNow + maximo;
        while (true)
        {
            if (await SesionesBloqueadasAsync(c, ct) >= sesiones)
                return true;
            if (DateTime.UtcNow > limite)
                return false;
            await Task.Delay(50, ct);
        }
    }

    private static async Task<int> SesionesBloqueadasAsync(NpgsqlConnection c, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand(
            "SELECT count(*)::int FROM pg_stat_activity WHERE datname = current_database() AND wait_event_type = 'Lock' AND pid <> pg_backend_pid()", c);
        return (int)(await cmd.ExecuteScalarAsync(ct))!;
    }

    /// <summary>Abre una conexión con una transacción y ejecuta <paramref name="sql"/> dentro (el bloqueo queda hasta el commit).</summary>
    public static async Task<(NpgsqlConnection Conexion, NpgsqlTransaction Transaccion)> RetenerAsync(string conexion, string sql, CancellationToken ct, params object[] parametros)
    {
        var c = new NpgsqlConnection(conexion);
        await c.OpenAsync(ct);
        var tx = await c.BeginTransactionAsync(ct);
        await using var cmd = new NpgsqlCommand(sql, c, tx);
        foreach (var p in parametros)
            cmd.Parameters.Add(new NpgsqlParameter { Value = p });
        await cmd.ExecuteNonQueryAsync(ct);
        return (c, tx);
    }
}
