using Flit.Ict.Domain.Abstractions;
using Flit.Ict.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Flit.Ict.Infrastructure.Logging;

/// <summary>
/// Calcula las métricas de alerta ICT sobre el schema ict. Comparte Postgres con core-api, por lo
/// que el AnalyticsSchedulerProcessor de core-api puede evaluar estas mismas métricas cross-schema
/// (ver plan §A.10). jobs_out_of_sla se deriva de ict.job_runs (registro que escribe IctPollingJob).
/// </summary>
public sealed class IctAlertMetricsQuery(IctDbContext db) : IIctAlertMetricsQuery
{
    private const int StuckMinutes = 30;

    public async Task<IctAlertMetrics> GetAsync(Guid? tenantId, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var stuckThreshold = now.AddMinutes(-StuckMinutes);
        var since = now.AddHours(-24);

        var masters = db.Masters.AsNoTracking().Where(m => m.DeletedAt == null);
        if (tenantId is { } tid)
        {
            masters = masters.Where(m => m.TenantId == tid);
        }

        var stuck = await masters.CountAsync(
            m => m.ProcedureInstanceId == null
                 && (m.ProcessStatusId == 1 || m.ProcessStatusId == 2)
                 && m.CreatedAt < stuckThreshold,
            ct);

        var recent = await masters.CountAsync(m => m.CreatedAt >= since, ct);
        var novelties = await masters.CountAsync(m => m.CreatedAt >= since && m.ProcessStatusId == 4, ct);
        var noveltyRate = recent == 0 ? 0d : Math.Round(novelties * 100d / recent, 2);

        var webhookFailures = await db.Database
            .SqlQuery<long>($"""
                SELECT count(*)::bigint AS "Value"
                FROM ict.external_integration_webhook_master
                WHERE is_notified = true AND response_ok = false AND created_at >= {since}
                """)
            .FirstAsync(ct);

        // job_runs es GLOBAL (jobs cross-tenant): la métrica de SLA es platform-wide, no por tenant.
        var jobsOutOfSla = await db.Database
            .SqlQuery<long>($"""
                SELECT count(*)::bigint AS "Value"
                FROM ict.job_runs
                WHERE breached_sla = true AND started_at >= {since}
                """)
            .FirstAsync(ct);

        return new IctAlertMetrics(stuck, noveltyRate, webhookFailures, jobsOutOfSla);
    }
}
