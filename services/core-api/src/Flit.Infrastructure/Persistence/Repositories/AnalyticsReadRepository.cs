using System.Data.Common;
using Flit.Analytics.Application.Abstractions;
using Flit.Analytics.Application.Dtos;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Flit.Infrastructure.Persistence.Repositories;

/// <summary>
/// Lectura analítica en vivo desde las tablas operacionales del módulo de trámites (schema
/// <c>tramites</c>) e <c>identity</c> — HU #10430 / ADR-0021 (Opción C: una sola fuente de verdad,
/// se eliminaron los agregados <c>analytics.*</c>). Cada consulta fija <c>app.current_tenant_id</c>
/// con <c>set_config(..., is_local := true)</c> (parametrizado, sin concatenar SQL) dentro de una
/// transacción para respetar RLS — incluido el caso SuperAdmin consultando otro tenant. La
/// normalización de categoría (family → matriculas/traspasos/otros) se hace en SQL para que el
/// GROUP BY sea consistente con el contrato del frontend.
/// </summary>
internal sealed class AnalyticsReadRepository : IAnalyticsReadRepository
{
    // Overview en vivo: agrega procedure_instances (estado actual) por categoría y estado en el rango
    // de creación. Reemplaza la lectura del agregado analytics.procedure_metrics_daily.
    private const string OverviewSql = """
        SELECT
            CASE
                WHEN upper(pt.family) = 'MATRICULAS' THEN 'matriculas'
                WHEN upper(pt.family) = 'TRASPASO'  THEN 'traspasos'
                ELSE 'otros'
            END AS category,
            pi.status,
            count(*)::int AS total
        FROM tramites.procedure_instances pi
        JOIN tramites.procedure_types pt ON pt.id = pi.procedure_type_id
        WHERE pi.tenant_id = @tenant
          AND pi.deleted_at IS NULL
          AND pi.created_at::date BETWEEN @from AND @to
        GROUP BY 1, pi.status
        ORDER BY 1, pi.status;
        """;

    // Top productores en vivo desde status_history (misma semántica que el extinto refresh:
    // submitted; approved = approved_ot|completed; rejected = rejected_ot|cancelled). El HAVING
    // preserva el contrato previo (solo usuarios con al menos una radicación en el rango).
    private const string TopProducersSql = """
        SELECT h.changed_by AS user_id, u.display_name,
               count(*) FILTER (WHERE h.to_status = 'submitted')::int                      AS submitted,
               count(*) FILTER (WHERE h.to_status IN ('approved_ot', 'completed'))::int     AS approved,
               count(*) FILTER (WHERE h.to_status IN ('rejected_ot', 'cancelled'))::int     AS rejected
        FROM tramites.procedure_instance_status_history h
        JOIN identity.users u ON u.id = h.changed_by
        WHERE h.tenant_id = @tenant
          AND h.changed_by IS NOT NULL
          AND h.changed_at::date BETWEEN @from AND @to
        GROUP BY h.changed_by, u.display_name
        HAVING count(*) FILTER (WHERE h.to_status = 'submitted') > 0
        ORDER BY count(*) FILTER (WHERE h.to_status = 'submitted') DESC, u.display_name ASC
        LIMIT @limit;
        """;

    // CTE compartida: proyecta el detalle de trámites con la categoría normalizada y aplica
    // los filtros opcionales (category/status). Los cast ::text permiten parámetros NULL tipados.
    private const string DetailsBaseCte = """
        WITH base AS (
            SELECT pi.id, pi.reference_number,
                   pt.name AS procedure_type_name,
                   CASE
                       WHEN upper(pt.family) = 'MATRICULAS' THEN 'matriculas'
                       WHEN upper(pt.family) = 'TRASPASO'  THEN 'traspasos'
                       ELSE 'otros'
                   END AS category,
                   pi.status,
                   u.display_name AS created_by_display_name,
                   pi.submitted_at, pi.completed_at, pi.created_at
            FROM tramites.procedure_instances pi
            JOIN tramites.procedure_types pt ON pt.id = pi.procedure_type_id
            JOIN identity.users u ON u.id = pi.created_by_user_id
            WHERE pi.tenant_id = @tenant
              AND pi.deleted_at IS NULL
              AND pi.created_at::date BETWEEN @from AND @to
        )
        """;

    private const string DetailsFilter =
        " WHERE (@category::text IS NULL OR category = @category::text)" +
        "   AND (@status::text IS NULL OR status = @status::text)";

    private readonly FlitDbContext _context;

    public AnalyticsReadRepository(FlitDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public Task<IReadOnlyList<CategoryMetricsDto>> GetOverviewAsync(
        Guid tenantId, DateOnly fromDate, DateOnly toDate, CancellationToken ct = default) =>
        ExecuteWithTenantAsync(tenantId, async (conn, tx) =>
        {
            await using var cmd = CreateCommand(conn, tx, OverviewSql);
            AddParam(cmd, "tenant", tenantId);
            AddParam(cmd, "from", fromDate);
            AddParam(cmd, "to", toDate);

            // Acumula filas (category, status, total) agrupando por categoría preservando el orden SQL.
            var byCategory = new Dictionary<string, (int Total, List<StatusCountDto> Statuses)>(StringComparer.Ordinal);
            var order = new List<string>();
            await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                var category = reader.GetString(0);
                var status = reader.GetString(1);
                var count = reader.GetInt32(2);
                if (!byCategory.TryGetValue(category, out var acc))
                {
                    acc = (0, new List<StatusCountDto>());
                    byCategory[category] = acc;
                    order.Add(category);
                }

                acc.Statuses.Add(new StatusCountDto(status, count));
                byCategory[category] = (acc.Total + count, acc.Statuses);
            }

            return (IReadOnlyList<CategoryMetricsDto>)order
                .Select(c => new CategoryMetricsDto(c, byCategory[c].Total, byCategory[c].Statuses))
                .ToList();
        }, ct);

    public Task<IReadOnlyList<TopProducerDto>> GetTopProducersAsync(
        Guid tenantId, DateOnly fromDate, DateOnly toDate, int limit, CancellationToken ct = default) =>
        ExecuteWithTenantAsync(tenantId, async (conn, tx) =>
        {
            await using var cmd = CreateCommand(conn, tx, TopProducersSql);
            AddParam(cmd, "tenant", tenantId);
            AddParam(cmd, "from", fromDate);
            AddParam(cmd, "to", toDate);
            AddParam(cmd, "limit", limit);

            var items = new List<TopProducerDto>();
            await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                items.Add(new TopProducerDto(
                    reader.GetGuid(0),
                    reader.GetString(1),
                    reader.GetInt32(2),
                    reader.GetInt32(3),
                    reader.GetInt32(4)));
            }

            return (IReadOnlyList<TopProducerDto>)items;
        }, ct);

    public Task<ProcedureDetailsPageDto> GetProcedureDetailsAsync(
        Guid tenantId, DateOnly fromDate, DateOnly toDate,
        string? category, string? status, int page, int pageSize, CancellationToken ct = default) =>
        ExecuteWithTenantAsync(tenantId, async (conn, tx) =>
        {
            // 1) Total del universo filtrado.
            await using var countCmd = CreateCommand(conn, tx, DetailsBaseCte + " SELECT count(*) FROM base" + DetailsFilter + ";");
            AddParam(countCmd, "tenant", tenantId);
            AddParam(countCmd, "from", fromDate);
            AddParam(countCmd, "to", toDate);
            AddParam(countCmd, "category", (object?)category ?? DBNull.Value);
            AddParam(countCmd, "status", (object?)status ?? DBNull.Value);
            var total = Convert.ToInt32(await countCmd.ExecuteScalarAsync(ct).ConfigureAwait(false));

            // 2) Página solicitada (más recientes primero).
            await using var pageCmd = CreateCommand(conn, tx, DetailsBaseCte +
                " SELECT id, reference_number, procedure_type_name, category, status," +
                " created_by_display_name, submitted_at, completed_at" +
                " FROM base" + DetailsFilter +
                // Desempate por id (PK única): ORDER BY created_at solo no es determinista
                // cuando hay timestamps repetidos → paginación estable sin filas duplicadas entre páginas.
                " ORDER BY created_at DESC, id DESC LIMIT @pageSize OFFSET @offset;");
            AddParam(pageCmd, "tenant", tenantId);
            AddParam(pageCmd, "from", fromDate);
            AddParam(pageCmd, "to", toDate);
            AddParam(pageCmd, "category", (object?)category ?? DBNull.Value);
            AddParam(pageCmd, "status", (object?)status ?? DBNull.Value);
            AddParam(pageCmd, "pageSize", pageSize);
            AddParam(pageCmd, "offset", (page - 1) * pageSize);

            var items = new List<ProcedureDetailDto>();
            await using var reader = await pageCmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                items.Add(new ProcedureDetailDto(
                    reader.GetGuid(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.GetString(3),
                    reader.GetString(4),
                    reader.GetString(5),
                    await reader.IsDBNullAsync(6, ct).ConfigureAwait(false) ? null : reader.GetFieldValue<DateTimeOffset>(6),
                    await reader.IsDBNullAsync(7, ct).ConfigureAwait(false) ? null : reader.GetFieldValue<DateTimeOffset>(7)));
            }

            return new ProcedureDetailsPageDto(items, total, page, pageSize);
        }, ct);

    public async Task ExportProcedureDetailsAsync(
        Guid tenantId, DateOnly fromDate, DateOnly toDate, string? category, string? status,
        Func<ProcedureDetailDto, CancellationToken, Task> onRowAsync, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(onRowAsync);
        await ExecuteWithTenantAsync(tenantId, async (conn, tx) =>
        {
            await using var cmd = CreateCommand(conn, tx, DetailsBaseCte +
                " SELECT id, reference_number, procedure_type_name, category, status," +
                " created_by_display_name, submitted_at, completed_at" +
                " FROM base" + DetailsFilter +
                " ORDER BY created_at DESC, id DESC;");
            AddParam(cmd, "tenant", tenantId);
            AddParam(cmd, "from", fromDate);
            AddParam(cmd, "to", toDate);
            AddParam(cmd, "category", (object?)category ?? DBNull.Value);
            AddParam(cmd, "status", (object?)status ?? DBNull.Value);

            // CommandBehavior.SequentialAccess: el lector no bufferiza filas → memoria acotada.
            await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                var dto = new ProcedureDetailDto(
                    reader.GetGuid(0), reader.GetString(1), reader.GetString(2), reader.GetString(3),
                    reader.GetString(4), reader.GetString(5),
                    await reader.IsDBNullAsync(6, ct).ConfigureAwait(false) ? null : reader.GetFieldValue<DateTimeOffset>(6),
                    await reader.IsDBNullAsync(7, ct).ConfigureAwait(false) ? null : reader.GetFieldValue<DateTimeOffset>(7));
                await onRowAsync(dto, ct).ConfigureAwait(false);
            }

            return true;
        }, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Abre una transacción reintentablemente, fija el GUC de tenant para RLS y ejecuta la consulta.
    /// El GUC es local a la transacción: se revierte al cerrar y no contamina la conexión del pool.
    /// </summary>
    private async Task<T> ExecuteWithTenantAsync<T>(
        Guid tenantId, Func<DbConnection, DbTransaction, Task<T>> body, CancellationToken ct)
    {
        var strategy = _context.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            var transaction = await _context.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
            await using (transaction.ConfigureAwait(false))
            {
                await _context.Database.ExecuteSqlInterpolatedAsync(
                    $"SELECT set_config('app.current_tenant_id', {tenantId.ToString()}, true)", ct)
                    .ConfigureAwait(false);

                var conn = _context.Database.GetDbConnection();
                var tx = transaction.GetDbTransaction();
                var result = await body(conn, tx).ConfigureAwait(false);

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
