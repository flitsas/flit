using System.Data.Common;
using Flit.Analytics.Application.Abstractions;
using Flit.Analytics.Application.Dtos;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using NpgsqlTypes;

namespace Flit.Infrastructure.Persistence.Repositories;

/// <summary>
/// Lecturas en vivo del Inicio del Gestor sobre <c>tramites.*</c>, con el mismo patrón que
/// <see cref="AnalyticsReadRepository"/>: transacción con <c>app.current_tenant_id</c> (GUC local, parametrizado) y
/// filtro explícito por <c>tenant_id</c> en cada tabla. No hay vista global: el handler ya rechaza el tenant vacío.
/// </summary>
internal sealed class GestorDashboardReadRepository(FlitDbContext context) : IGestorDashboardReadRepository
{
    private const string CategoriaCase = AnalyticsReadRepository.CategoriaCase;
    private const string FamilyByType = AnalyticsFamilyFilter.ByType;

    // ── Cola de gestión ────────────────────────────────────────────────────────────────────────────────
    // Último movimiento = lo más reciente entre el último cambio de estado (historial), la última actualización
    // de la fila y su creación. Desempate por id para que el top sea estable.
    private const string ColaBaseCte = $"""
        WITH base AS (
            SELECT pi.id, pi.reference_number, pi.plate, pi.vin, pi.status,
                   {CategoriaCase} AS category,
                   GREATEST(pi.created_at, coalesce(pi.updated_at, pi.created_at), coalesce(h.ultimo, pi.created_at)) AS movimiento
            FROM tramites.procedure_instances pi
            JOIN tramites.procedure_types pt ON pt.id = pi.procedure_type_id
            LEFT JOIN LATERAL (
                SELECT max(h.changed_at) AS ultimo
                FROM tramites.procedure_instance_status_history h
                WHERE h.procedure_instance_id = pi.id AND h.tenant_id = @tenant
            ) h ON true
            WHERE pi.tenant_id = @tenant
              AND pi.deleted_at IS NULL
              AND pi.status = ANY(@estados::text[])
              AND {FamilyByType}
        )
        """;

    private const string ColaPageSql = ColaBaseCte + """
        SELECT id, reference_number, plate, vin, status, category,
               GREATEST(0, floor(extract(epoch FROM (now() - movimiento)) / 86400))::int AS dias
        FROM base
        ORDER BY movimiento ASC, id ASC
        LIMIT @limit;
        """;

    private const string ColaCountSql = ColaBaseCte + " SELECT count(*)::int FROM base;";

    public Task<GestorDashboardColaDto> GetColaInactividadAsync(
        Guid tenantId, IReadOnlyList<string> estados, IReadOnlyList<string>? families, int limit,
        CancellationToken ct = default) =>
        ExecuteWithTenantAsync(tenantId, async (conn, tx) =>
        {
            await using var countCmd = CreateCommand(conn, tx, ColaCountSql);
            AddColaParams(countCmd, tenantId, estados, families);
            var total = Convert.ToInt32(await countCmd.ExecuteScalarAsync(ct).ConfigureAwait(false));

            await using var pageCmd = CreateCommand(conn, tx, ColaPageSql);
            AddColaParams(pageCmd, tenantId, estados, families);
            AddParam(pageCmd, "limit", limit);

            var items = new List<GestorDashboardColaItemDto>();
            await using var reader = await pageCmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                items.Add(new GestorDashboardColaItemDto(
                    reader.GetGuid(0),
                    reader.IsDBNull(1) ? null : reader.GetString(1),
                    reader.IsDBNull(2) ? null : reader.GetString(2),
                    reader.IsDBNull(3) ? null : reader.GetString(3),
                    reader.GetString(4),
                    reader.GetString(5),
                    reader.GetInt32(6)));
            }

            return new GestorDashboardColaDto(items, total);
        }, ct);

    private static void AddColaParams(DbCommand cmd, Guid tenantId, IReadOnlyList<string> estados, IReadOnlyList<string>? families)
    {
        AddParam(cmd, "tenant", tenantId);
        var p = cmd.CreateParameter();
        p.ParameterName = "estados";
        p.Value = estados.ToArray();
        if (p is NpgsqlParameter np)
            np.NpgsqlDbType = NpgsqlDbType.Array | NpgsqlDbType.Text;
        cmd.Parameters.Add(p);
        AnalyticsFamilyFilter.AddParam(cmd, families);
    }

    // ── Infraestructura (misma que AnalyticsReadRepository) ─────────────────────────────────────────────

    private async Task<T> ExecuteWithTenantAsync<T>(
        Guid tenantId, Func<DbConnection, DbTransaction, Task<T>> body, CancellationToken ct)
    {
        var strategy = context.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            var transaction = await context.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
            await using (transaction.ConfigureAwait(false))
            {
                await context.Database.ExecuteSqlInterpolatedAsync(
                    $"SELECT set_config('app.current_tenant_id', {tenantId.ToString()}, true)", ct)
                    .ConfigureAwait(false);

                var result = await body(context.Database.GetDbConnection(), transaction.GetDbTransaction())
                    .ConfigureAwait(false);

                await transaction.CommitAsync(ct).ConfigureAwait(false);
                return result;
            }
        }).ConfigureAwait(false);
    }

    private static DbCommand CreateCommand(DbConnection conn, DbTransaction tx, string sql)
    {
        var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;
        return cmd;
    }

    private static void AddParam(DbCommand cmd, string name, object value)
    {
        var p = cmd.CreateParameter();
        p.ParameterName = name;
        p.Value = value;
        cmd.Parameters.Add(p);
    }
}
