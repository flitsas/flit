using System.Data.Common;
using Flit.Analytics.Application.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Flit.Infrastructure.Persistence.Repositories;

/// <summary>
/// Lectura agregada de <c>analytics.app_usage_events</c> (HU-A Reportes 2.0, contrato §6).
/// SQL ADO.NET con el patrón <c>ExecuteWithTenantAsync</c> de <see cref="AnalyticsReadRepository"/>:
/// transacción + <c>set_config('app.current_tenant_id', ..., true)</c> (RLS) + WHERE explícito por
/// tenant. Agrupaciones por día/hora en zona de negocio America/Bogota
/// (<c>occurred_at AT TIME ZONE 'America/Bogota'</c>).
/// </summary>
internal sealed class UsageMetricsReadRepository : IUsageMetricsReadRepository
{
    // ── Wizard steps ──────────────────────────────────────────────────────────────────────────────
    // Views = wizard_step_view; Completions = wizard_step_complete; duraciones desde duration_ms de
    // wizard_step_complete. AbandonmentPct se calcula en C# ((1 - completions/views) * 100; 0 si views=0).
    private const string WizardStepsSql = """
        SELECT step_key,
               count(*) FILTER (WHERE event_type = 'wizard_step_view')::int      AS views,
               count(*) FILTER (WHERE event_type = 'wizard_step_complete')::int AS completions,
               avg(duration_ms) FILTER (WHERE event_type = 'wizard_step_complete')::float8 AS avg_ms,
               (percentile_cont(0.5) WITHIN GROUP (ORDER BY duration_ms)
                   FILTER (WHERE event_type = 'wizard_step_complete' AND duration_ms IS NOT NULL))::float8 AS median_ms
        FROM analytics.app_usage_events
        WHERE tenant_id = @tenant
          AND event_type IN ('wizard_step_view', 'wizard_step_complete')
          AND step_key IS NOT NULL
          AND (occurred_at AT TIME ZONE 'America/Bogota')::date BETWEEN @from AND @to
        GROUP BY step_key
        ORDER BY step_key;
        """;

    // ── Uso por módulo ────────────────────────────────────────────────────────────────────────────
    // module_view (front) + api_module_access (middleware), agrupado por módulo.
    private const string ModuleUsageSql = """
        SELECT module,
               count(*)::int                 AS events,
               count(DISTINCT user_id)::int  AS unique_users
        FROM analytics.app_usage_events
        WHERE tenant_id = @tenant
          AND event_type IN ('module_view', 'api_module_access')
          AND module IS NOT NULL
          AND (occurred_at AT TIME ZONE 'America/Bogota')::date BETWEEN @from AND @to
        GROUP BY module
        ORDER BY events DESC, module;
        """;

    // ── Horas pico ────────────────────────────────────────────────────────────────────────────────
    // TODOS los eventos, día de semana (0=domingo … 6=sábado) y hora en America/Bogota.
    private const string PeakHoursSql = """
        SELECT EXTRACT(DOW  FROM occurred_at AT TIME ZONE 'America/Bogota')::int AS day_of_week,
               EXTRACT(HOUR FROM occurred_at AT TIME ZONE 'America/Bogota')::int AS hour,
               count(*)::int AS events
        FROM analytics.app_usage_events
        WHERE tenant_id = @tenant
          AND (occurred_at AT TIME ZONE 'America/Bogota')::date BETWEEN @from AND @to
        GROUP BY 1, 2
        ORDER BY 1, 2;
        """;

    // ── Duración total del wizard ─────────────────────────────────────────────────────────────────
    // Solo wizard_complete con duration_ms (duración total del trámite radicado desde el wizard).
    private const string WizardDurationSql = """
        SELECT avg(duration_ms)::float8 AS avg_ms,
               (percentile_cont(0.5) WITHIN GROUP (ORDER BY duration_ms))::float8 AS median_ms
        FROM analytics.app_usage_events
        WHERE tenant_id = @tenant
          AND event_type = 'wizard_complete'
          AND duration_ms IS NOT NULL
          AND (occurred_at AT TIME ZONE 'America/Bogota')::date BETWEEN @from AND @to;
        """;

    private readonly FlitDbContext _context;

    public UsageMetricsReadRepository(FlitDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public Task<IReadOnlyList<WizardStepMetricDto>> GetWizardStepMetricsAsync(
        Guid tenantId, DateOnly from, DateOnly to, CancellationToken ct) =>
        ExecuteWithTenantAsync<IReadOnlyList<WizardStepMetricDto>>(tenantId, async (conn, tx) =>
        {
            await using var cmd = CreateCommand(conn, tx, WizardStepsSql);
            AddParam(cmd, "tenant", tenantId);
            AddParam(cmd, "from", from);
            AddParam(cmd, "to", to);

            var items = new List<WizardStepMetricDto>();
            await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                var views = reader.GetInt32(1);
                var completions = reader.GetInt32(2);
                var abandonment = views == 0 ? 0d : (1d - (double)completions / views) * 100d;
                items.Add(new WizardStepMetricDto(
                    reader.GetString(0),
                    views,
                    completions,
                    Math.Round(abandonment, 1),
                    await reader.IsDBNullAsync(3, ct).ConfigureAwait(false) ? null : reader.GetDouble(3),
                    await reader.IsDBNullAsync(4, ct).ConfigureAwait(false) ? null : reader.GetDouble(4)));
            }

            return items;
        }, ct);

    public Task<IReadOnlyList<ModuleUsageDto>> GetModuleUsageAsync(
        Guid tenantId, DateOnly from, DateOnly to, CancellationToken ct) =>
        ExecuteWithTenantAsync<IReadOnlyList<ModuleUsageDto>>(tenantId, async (conn, tx) =>
        {
            await using var cmd = CreateCommand(conn, tx, ModuleUsageSql);
            AddParam(cmd, "tenant", tenantId);
            AddParam(cmd, "from", from);
            AddParam(cmd, "to", to);

            var items = new List<ModuleUsageDto>();
            await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                items.Add(new ModuleUsageDto(
                    reader.GetString(0),
                    reader.GetInt32(1),
                    reader.GetInt32(2)));
            }

            return items;
        }, ct);

    public Task<IReadOnlyList<PeakHourDto>> GetPeakHoursAsync(
        Guid tenantId, DateOnly from, DateOnly to, CancellationToken ct) =>
        ExecuteWithTenantAsync<IReadOnlyList<PeakHourDto>>(tenantId, async (conn, tx) =>
        {
            await using var cmd = CreateCommand(conn, tx, PeakHoursSql);
            AddParam(cmd, "tenant", tenantId);
            AddParam(cmd, "from", from);
            AddParam(cmd, "to", to);

            var items = new List<PeakHourDto>();
            await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                items.Add(new PeakHourDto(
                    reader.GetInt32(0),
                    reader.GetInt32(1),
                    reader.GetInt32(2)));
            }

            return items;
        }, ct);

    public Task<WizardDurationDto> GetWizardDurationAsync(
        Guid tenantId, DateOnly from, DateOnly to, CancellationToken ct) =>
        ExecuteWithTenantAsync(tenantId, async (conn, tx) =>
        {
            await using var cmd = CreateCommand(conn, tx, WizardDurationSql);
            AddParam(cmd, "tenant", tenantId);
            AddParam(cmd, "from", from);
            AddParam(cmd, "to", to);

            await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            if (!await reader.ReadAsync(ct).ConfigureAwait(false))
                return new WizardDurationDto(null, null);

            return new WizardDurationDto(
                await reader.IsDBNullAsync(0, ct).ConfigureAwait(false) ? null : reader.GetDouble(0),
                await reader.IsDBNullAsync(1, ct).ConfigureAwait(false) ? null : reader.GetDouble(1));
        }, ct);

    // ── Infraestructura (patrón AnalyticsReadRepository) ──────────────────────────────────────────

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
