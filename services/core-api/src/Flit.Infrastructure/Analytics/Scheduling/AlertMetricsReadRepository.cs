using System.Data.Common;
using Flit.Analytics.Application.Scheduling;
using Flit.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Flit.Infrastructure.Analytics.Scheduling;

/// <summary>
/// Evaluación de las métricas de alerta (Reportes 2.0, HU-D §8) con SQL propio sobre las tablas
/// operacionales. Mismo patrón de tenant que <c>AnalyticsReadRepository</c>: se fija el GUC
/// <c>app.current_tenant_id</c> (RLS) dentro de una transacción Y la consulta filtra
/// explícitamente por <c>tenant_id</c>.
/// </summary>
internal sealed class AlertMetricsReadRepository(FlitDbContext db) : IAlertMetricsReadRepository
{
    /// <summary>stuck_count: días sin transición para considerar una instancia atascada (§8: fijo).
    /// Const string para poder interpolarlo en el SQL const (C# solo permite const strings ahí).</summary>
    private const string StuckDays = "7";

    /// <summary>Estados biométricos "pendiente / en proceso" (estado actual, la ventana no aplica).</summary>
    private const string PendingBiometricStatuses = "'enviado','en_proceso','pendiente_envio'";

    // rechazados / (aprobados + rechazados) * 100 sobre transiciones dentro de la ventana; 0 sin decididos.
    private const string RejectionRateSql = $"""
        SELECT CASE
                 WHEN count(*) FILTER (WHERE to_status IN ('aprobado','rechazado')) = 0 THEN 0::numeric
                 ELSE round(
                   count(*) FILTER (WHERE to_status = 'rechazado')::numeric * 100
                     / count(*) FILTER (WHERE to_status IN ('aprobado','rechazado')), 2)
               END
        FROM tramites.procedure_instance_status_history
        WHERE tenant_id = @tenant
          AND changed_at >= now() - make_interval(mins => @window);
        """;

    // Instancias en estado NO final sin transición hace > 7 días (o desde su creación si nunca transicionó).
    private const string StuckCountSql = $"""
        SELECT count(*)::numeric
        FROM tramites.procedure_instances pi
        WHERE pi.tenant_id = @tenant
          AND pi.deleted_at IS NULL
          AND pi.status NOT IN ('aprobado','anulado')
          AND COALESCE(
                (SELECT max(h.changed_at)
                 FROM tramites.procedure_instance_status_history h
                 WHERE h.procedure_instance_id = pi.id),
                pi.created_at) < now() - make_interval(days => {StuckDays});
        """;

    // Errores de integraciones OT en la ventana (misma regla que §4.4: response_code >= 400 o error_message).
    private const string ExternalApiErrorsSql = """
        SELECT count(*)::numeric
        FROM admin.ot_api_call_logs
        WHERE tenant_id = @tenant
          AND called_at >= now() - make_interval(mins => @window)
          AND (response_code >= 400 OR error_message IS NOT NULL);
        """;

    // Validaciones biométricas cuyo estado ACTUAL es pendiente/en proceso.
    private const string PendingIdentityValidationsSql = $"""
        SELECT count(*)::numeric
        FROM tramites.procedure_instance_biometric_validations
        WHERE tenant_id = @tenant
          AND status IN ({PendingBiometricStatuses});
        """;

    public async Task<decimal> GetMetricValueAsync(
        Guid tenantId, string metric, int windowMinutes, CancellationToken ct)
    {
        var sql = metric switch
        {
            "rejection_rate_pct" => RejectionRateSql,
            "stuck_count" => StuckCountSql,
            "external_api_errors" => ExternalApiErrorsSql,
            "pending_identity_validations" => PendingIdentityValidationsSql,
            _ => throw new ArgumentOutOfRangeException(nameof(metric), metric,
                "Métrica de alerta desconocida."),
        };

        var strategy = db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            var transaction = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
            await using (transaction.ConfigureAwait(false))
            {
                await db.Database.ExecuteSqlInterpolatedAsync(
                    $"SELECT set_config('app.current_tenant_id', {tenantId.ToString()}, true)", ct)
                    .ConfigureAwait(false);

                var conn = db.Database.GetDbConnection();
                await using var cmd = conn.CreateCommand();
                cmd.Transaction = transaction.GetDbTransaction();
                cmd.CommandText = sql;
                AddParam(cmd, "tenant", tenantId);
                if (sql.Contains("@window", StringComparison.Ordinal))
                    AddParam(cmd, "window", windowMinutes);

                var result = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
                await transaction.CommitAsync(ct).ConfigureAwait(false);
                return result is null or DBNull ? 0m : Convert.ToDecimal(result, System.Globalization.CultureInfo.InvariantCulture);
            }
        }).ConfigureAwait(false);
    }

    private static void AddParam(DbCommand cmd, string name, object value)
    {
        var p = cmd.CreateParameter();
        p.ParameterName = name;
        p.Value = value;
        cmd.Parameters.Add(p);
    }
}
