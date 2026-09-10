using System.Data.Common;

namespace Flit.Ict.Infrastructure.Jobs;

/// <summary>
/// Advisory lock de Postgres para serializar un ciclo de job entre RÉPLICAS: con &gt;1 instancia del
/// servicio, solo la que adquiere el lock corre el ciclo; las demás lo saltan (evita doble-procesamiento
/// del mismo pre-trámite/fuente). Es lock de SESIÓN sobre la conexión del ciclo, liberado explícitamente
/// al terminar; si el proceso muere, Postgres lo libera al cerrarse la sesión (y el reset de pool de
/// Npgsql ejecuta pg_advisory_unlock_all como respaldo). Reemplaza la necesidad de FOR UPDATE SKIP LOCKED
/// para estos jobs de lote.
/// </summary>
internal static class IctAdvisoryLock
{
    /// <summary>Claves (bigint) distintas por job. Namespace ICT arbitrario para no chocar con otros locks.</summary>
    public static class Keys
    {
        public const long Business = 4810001;
        public const long External = 4810002;
        public const long Orchestrator = 4810003;
        public const long SendToCoreApi = 4810004;
        public const long Webhook = 4810005;
    }

    public static async Task<bool> TryLockAsync(DbConnection connection, long key, CancellationToken ct)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT pg_try_advisory_lock(@k)";
        AddKey(cmd, key);
        var result = await cmd.ExecuteScalarAsync(ct);
        return result is true;
    }

    public static async Task UnlockAsync(DbConnection connection, long key, CancellationToken ct)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT pg_advisory_unlock(@k)";
        AddKey(cmd, key);
        await cmd.ExecuteScalarAsync(ct);
    }

    private static void AddKey(DbCommand cmd, long key)
    {
        var p = cmd.CreateParameter();
        p.ParameterName = "k";
        p.Value = key;
        cmd.Parameters.Add(p);
    }
}
