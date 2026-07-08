using System.Data.Common;
using Flit.Analytics.Application.Abstractions;
using Flit.Analytics.Application.Scheduling;
using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Entities.Analytics;
using Flit.Modules.Security.Domain.Auth;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Flit.Infrastructure.Analytics.Scheduling;

/// <summary>
/// Scheduler de Reportes 2.0 — HU-D (§8 del contrato). Cada 60 s:
/// (a) INFORMES: evalúa los <c>analytics.report_schedules</c> activos con
/// <see cref="ScheduleDueEvaluator"/> en hora America/Bogota; los vencidos se reclaman con
/// <c>FOR UPDATE SKIP LOCKED</c> y se sella <c>last_sent_at</c> ANTES de enviar (dentro de la
/// transacción) — con varias réplicas cada schedule lo envía exactamente una. El correo lleva
/// el RESUMEN HTML de KPIs del periodo vencido (<see cref="IAnalyticsReadRepository"/>):
/// <c>IEmailSender</c> no soporta adjuntos, limitación documentada en el contrato.
/// (b) ALERTAS: por regla activa evalúa su métrica (<see cref="IAlertMetricsReadRepository"/>)
/// en su ventana; si el operador se cumple y el cooldown venció (<see cref="AlertRuleEvaluator"/>),
/// registra el <c>alert_event</c> y sella <c>last_triggered_at</c> en la misma transacción, y
/// después notifica por correo (best-effort: <c>notified</c> se marca tras enviar).
/// Un fallo en un schedule/regla NO tumba el ciclo (try/catch por elemento). Mismo patrón que
/// <c>IdentityValidationOutboxProcessor</c>, con rama LINQ para providers no relacionales
/// (tests InMemory) como <c>ProcedureStateChangeOutboxProcessor</c>.
/// </summary>
internal sealed class AnalyticsSchedulerProcessor(
    IServiceScopeFactory scopeFactory,
    ILogger<AnalyticsSchedulerProcessor> logger) : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(15);
    private const int TopProducersLimit = 5;

    /// <summary>Zona horaria de negocio (§0 del contrato): send_hour/day_of_* son hora Bogotá.
    /// Resuelta con fallback multiplataforma en <see cref="ScheduleDueEvaluator.BogotaTimeZone"/>.</summary>
    internal static readonly TimeZoneInfo BogotaTimeZone = ScheduleDueEvaluator.BogotaTimeZone;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Retraso inicial: deja terminar migraciones/seed del arranque antes de sondear.
        try { await Task.Delay(StartupDelay, stoppingToken); }
        catch (OperationCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            await RunCycleAsync(stoppingToken);

            try { await Task.Delay(PollInterval, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }

    /// <summary>Un ciclo completo (informes + alertas); cada mitad aislada en su try/catch.</summary>
    internal async Task RunCycleAsync(CancellationToken ct)
    {
        var nowUtc = DateTimeOffset.UtcNow;

        try
        {
            await ProcessSchedulesAsync(nowUtc, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { return; }
        catch (Exception ex)
        {
            SchedulerLog.SchedulesCycleError(logger, ex);
        }

        try
        {
            await ProcessAlertRulesAsync(nowUtc, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { return; }
        catch (Exception ex)
        {
            SchedulerLog.AlertsCycleError(logger, ex);
        }
    }

    // ──────────────────────────────────────────────────────────────────────────
    // (a) Informes programados
    // ──────────────────────────────────────────────────────────────────────────

    internal async Task ProcessSchedulesAsync(DateTimeOffset nowUtc, CancellationToken ct)
    {
        List<Guid> dueIds;
        await using (var scope = scopeFactory.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FlitDbContext>();
            var candidates = await db.Set<ReportSchedule>().AsNoTracking()
                .Where(s => s.IsActive && s.DeletedAt == null)
                .ToListAsync(ct);

            dueIds = candidates
                .Where(s => ScheduleDueEvaluator.IsDue(
                    s.Frequency, s.DayOfWeek, s.DayOfMonth, s.SendHour,
                    s.LastSentAt, nowUtc, BogotaTimeZone))
                .Select(s => s.Id)
                .ToList();
        }

        foreach (var scheduleId in dueIds)
        {
            if (ct.IsCancellationRequested)
                break;
            try
            {
                await ProcessOneScheduleAsync(scheduleId, nowUtc, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // Un schedule con fallo (BD/SMTP) no bloquea el resto del ciclo.
                SchedulerLog.ScheduleError(logger, scheduleId, ex);
            }
        }
    }

    private async Task ProcessOneScheduleAsync(Guid scheduleId, DateTimeOffset nowUtc, CancellationToken ct)
    {
        ReportSchedule? sealedSchedule;

        await using (var scope = scopeFactory.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FlitDbContext>();
            if (db.Database.IsRelational())
            {
                var strategy = db.Database.CreateExecutionStrategy();
                sealedSchedule = await strategy.ExecuteAsync(
                    async () => await ClaimAndSealScheduleAsync(db, scheduleId, nowUtc, ct));
            }
            else
            {
                // Tests (InMemory): sin transacciones ni locking — sellado directo por LINQ.
                sealedSchedule = await SealScheduleInMemoryAsync(db, scheduleId, nowUtc, ct);
            }
        }

        if (sealedSchedule is null)
            return; // otra réplica lo tomó, o dejó de estar vencido

        await SendScheduledReportAsync(sealedSchedule, nowUtc, ct);
    }

    /// <summary>
    /// Reclama el schedule con <c>FOR UPDATE SKIP LOCKED</c>, re-verifica el vencimiento con la
    /// fila fresca (otra réplica pudo sellarla hace un instante) y sella <c>last_sent_at</c>
    /// ANTES de enviar, dentro de la transacción (§8). Null = no reclamado o ya no vencido.
    /// </summary>
    private static async Task<ReportSchedule?> ClaimAndSealScheduleAsync(
        FlitDbContext db, Guid scheduleId, DateTimeOffset nowUtc, CancellationToken ct)
    {
        db.ChangeTracker.Clear();
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var claimed = await TryClaimRowAsync(
            db, "analytics.report_schedules", scheduleId, ct);
        if (!claimed)
        {
            await tx.CommitAsync(ct);
            return null;
        }

        var row = await db.Set<ReportSchedule>().FirstAsync(s => s.Id == scheduleId, ct);
        if (!ScheduleDueEvaluator.IsDue(
                row.Frequency, row.DayOfWeek, row.DayOfMonth, row.SendHour,
                row.LastSentAt, nowUtc, BogotaTimeZone))
        {
            await tx.CommitAsync(ct);
            return null;
        }

        row.LastSentAt = nowUtc;
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return row;
    }

    private static async Task<ReportSchedule?> SealScheduleInMemoryAsync(
        FlitDbContext db, Guid scheduleId, DateTimeOffset nowUtc, CancellationToken ct)
    {
        var row = await db.Set<ReportSchedule>()
            .FirstOrDefaultAsync(s => s.Id == scheduleId && s.IsActive && s.DeletedAt == null, ct);
        if (row is null || !ScheduleDueEvaluator.IsDue(
                row.Frequency, row.DayOfWeek, row.DayOfMonth, row.SendHour,
                row.LastSentAt, nowUtc, BogotaTimeZone))
            return null;

        row.LastSentAt = nowUtc;
        await db.SaveChangesAsync(ct);
        return row;
    }

    /// <summary>Arma el resumen HTML de KPIs del periodo vencido y lo envía a cada destinatario.</summary>
    private async Task SendScheduledReportAsync(
        ReportSchedule schedule, DateTimeOffset nowUtc, CancellationToken ct)
    {
        // Scope PROPIO para lectura/envío: la lectura analítica abre su propia transacción (GUC RLS)
        // y no debe compartir el contexto que sostuvo el claim.
        await using var scope = scopeFactory.CreateAsyncScope();
        var analytics = scope.ServiceProvider.GetRequiredService<IAnalyticsReadRepository>();
        var emailSender = scope.ServiceProvider.GetRequiredService<IEmailSender>();

        var nowLocal = TimeZoneInfo.ConvertTime(nowUtc, BogotaTimeZone);
        var (from, to) = ScheduleDueEvaluator.GetElapsedPeriod(schedule.Frequency, nowLocal);
        var periodLabel = ScheduleDueEvaluator.DescribePeriod(schedule.Frequency, from, to);

        var overview = await analytics.GetOverviewAsync(schedule.TenantId, from, to, ct);
        var topProducers = await analytics.GetTopProducersAsync(
            schedule.TenantId, from, to, TopProducersLimit, ct);

        var subject = $"[FLIT] {schedule.Name} — {periodLabel}";
        var html = SchedulerEmailComposer.BuildScheduledReportHtml(
            schedule.Name, schedule.ReportType, periodLabel, overview, topProducers);

        foreach (var recipient in schedule.Recipients)
        {
            try
            {
                await emailSender.SendAsync(new EmailMessage(recipient, recipient, subject, html), ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // last_sent_at ya quedó sellado: el fallo de UN destinatario no repite el envío
                // a los demás ni re-dispara la ventana (trade-off documentado del patrón §8).
                SchedulerLog.ScheduleEmailError(logger, schedule.Id, recipient, ex);
            }
        }

        SchedulerLog.ScheduleSent(logger, schedule.Id, schedule.Name, schedule.Recipients.Count);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // (b) Alertas por umbral
    // ──────────────────────────────────────────────────────────────────────────

    internal async Task ProcessAlertRulesAsync(DateTimeOffset nowUtc, CancellationToken ct)
    {
        List<AlertRule> rules;
        await using (var scope = scopeFactory.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FlitDbContext>();
            rules = await db.Set<AlertRule>().AsNoTracking()
                .Where(r => r.IsActive && r.DeletedAt == null)
                .ToListAsync(ct);
        }

        foreach (var rule in rules)
        {
            if (ct.IsCancellationRequested)
                break;
            try
            {
                await EvaluateOneRuleAsync(rule, nowUtc, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // Una regla con fallo (SQL/SMTP) no bloquea la evaluación del resto.
                SchedulerLog.AlertRuleError(logger, rule.Id, ex);
            }
        }
    }

    private async Task EvaluateOneRuleAsync(AlertRule rule, DateTimeOffset nowUtc, CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var metrics = scope.ServiceProvider.GetRequiredService<IAlertMetricsReadRepository>();

        var value = await metrics.GetMetricValueAsync(
            rule.TenantId, rule.Metric, rule.WindowMinutes, ct);

        // Pre-filtro barato con la instantánea; la decisión definitiva se repite sobre la fila
        // fresca dentro de la transacción del claim (cooldown puede haber cambiado).
        if (!AlertRuleEvaluator.ShouldTrigger(
                rule.Operator, value, rule.Threshold, rule.LastTriggeredAt, rule.CooldownMinutes, nowUtc))
            return;

        AlertEvent? alertEvent;
        var db = scope.ServiceProvider.GetRequiredService<FlitDbContext>();
        if (db.Database.IsRelational())
        {
            var strategy = db.Database.CreateExecutionStrategy();
            alertEvent = await strategy.ExecuteAsync(
                async () => await ClaimAndRecordTriggerAsync(db, rule.Id, value, nowUtc, ct));
        }
        else
        {
            alertEvent = await RecordTriggerInMemoryAsync(db, rule.Id, value, nowUtc, ct);
        }

        if (alertEvent is null)
            return; // otra réplica disparó primero (cooldown recién sellado)

        await NotifyAlertAsync(scope.ServiceProvider, db, rule, alertEvent, nowUtc, ct);
    }

    /// <summary>
    /// Reclama la regla con <c>FOR UPDATE SKIP LOCKED</c>, re-verifica el cooldown con la fila
    /// fresca, registra el <c>alert_event</c> (notified = false) y sella <c>last_triggered_at</c>
    /// en la MISMA transacción. Null = no reclamada o cooldown vigente.
    /// </summary>
    private static async Task<AlertEvent?> ClaimAndRecordTriggerAsync(
        FlitDbContext db, Guid ruleId, decimal value, DateTimeOffset nowUtc, CancellationToken ct)
    {
        db.ChangeTracker.Clear();
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var claimed = await TryClaimRowAsync(db, "analytics.alert_rules", ruleId, ct);
        if (!claimed)
        {
            await tx.CommitAsync(ct);
            return null;
        }

        var rule = await db.Set<AlertRule>().FirstAsync(r => r.Id == ruleId, ct);
        if (AlertRuleEvaluator.IsCooldownActive(rule.LastTriggeredAt, rule.CooldownMinutes, nowUtc)
            || !AlertRuleEvaluator.Matches(rule.Operator, value, rule.Threshold))
        {
            await tx.CommitAsync(ct);
            return null;
        }

        var alertEvent = NewAlertEvent(rule, value, nowUtc);
        db.Set<AlertEvent>().Add(alertEvent);
        rule.LastTriggeredAt = nowUtc;
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return alertEvent;
    }

    private static async Task<AlertEvent?> RecordTriggerInMemoryAsync(
        FlitDbContext db, Guid ruleId, decimal value, DateTimeOffset nowUtc, CancellationToken ct)
    {
        var rule = await db.Set<AlertRule>()
            .FirstOrDefaultAsync(r => r.Id == ruleId && r.IsActive && r.DeletedAt == null, ct);
        if (rule is null
            || AlertRuleEvaluator.IsCooldownActive(rule.LastTriggeredAt, rule.CooldownMinutes, nowUtc)
            || !AlertRuleEvaluator.Matches(rule.Operator, value, rule.Threshold))
            return null;

        var alertEvent = NewAlertEvent(rule, value, nowUtc);
        db.Set<AlertEvent>().Add(alertEvent);
        rule.LastTriggeredAt = nowUtc;
        await db.SaveChangesAsync(ct);
        return alertEvent;
    }

    private static AlertEvent NewAlertEvent(AlertRule rule, decimal value, DateTimeOffset nowUtc) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = rule.TenantId,
        AlertRuleId = rule.Id,
        TriggeredAt = nowUtc,
        MetricValue = decimal.Round(value, 2),
        Threshold = rule.Threshold,
        Notified = false,
        Recipients = rule.Recipients.ToList(),
        Message = SchedulerEmailComposer.BuildAlertMessage(
            rule.Metric, rule.Operator, rule.Threshold, value, rule.WindowMinutes),
    };

    /// <summary>Envía el correo de alerta y marca <c>notified</c> (best-effort tras el envío).</summary>
    private async Task NotifyAlertAsync(
        IServiceProvider services, FlitDbContext db, AlertRule rule, AlertEvent alertEvent,
        DateTimeOffset nowUtc, CancellationToken ct)
    {
        var emailSender = services.GetRequiredService<IEmailSender>();
        var subject = $"[FLIT] Alerta: {rule.Name}";
        var html = SchedulerEmailComposer.BuildAlertHtml(
            rule.Name, rule.Metric, rule.Operator, rule.Threshold, alertEvent.MetricValue,
            rule.WindowMinutes, nowUtc, BogotaTimeZone);

        var anySent = false;
        foreach (var recipient in alertEvent.Recipients)
        {
            try
            {
                await emailSender.SendAsync(new EmailMessage(recipient, recipient, subject, html), ct);
                anySent = true;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                SchedulerLog.AlertEmailError(logger, rule.Id, recipient, ex);
            }
        }

        if (anySent)
        {
            var tracked = await db.Set<AlertEvent>().FirstOrDefaultAsync(e => e.Id == alertEvent.Id, ct);
            if (tracked is not null)
            {
                tracked.Notified = true;
                await db.SaveChangesAsync(ct);
            }
        }

        SchedulerLog.AlertTriggered(logger, rule.Id, rule.Name, alertEvent.MetricValue, rule.Threshold);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Claim compartido
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Reclama UNA fila por id con <c>FOR UPDATE SKIP LOCKED</c> sobre la transacción actual
    /// (multi-réplica: si otra instancia la tiene bloqueada, se salta sin esperar). SQL crudo
    /// porque EF Core no expone el locking clause en LINQ. El nombre de tabla es una constante
    /// interna (nunca input de usuario).
    /// </summary>
    private static async Task<bool> TryClaimRowAsync(
        FlitDbContext db, string qualifiedTable, Guid id, CancellationToken ct)
    {
        var connection = db.Database.GetDbConnection();
        var transaction = db.Database.CurrentTransaction!.GetDbTransaction();

        await using var cmd = connection.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText =
            $"SELECT id FROM {qualifiedTable} WHERE id = @id AND is_active AND deleted_at IS NULL " +
            "FOR UPDATE SKIP LOCKED";

        var pId = cmd.CreateParameter();
        pId.ParameterName = "id";
        pId.Value = id;
        cmd.Parameters.Add(pId);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct);
    }
}

/// <summary>Logging source-generated (CA1848) del scheduler de informes y alertas.</summary>
internal static partial class SchedulerLog
{
    [LoggerMessage(Level = LogLevel.Error,
        Message = "Scheduler Reportes2: error en el ciclo de informes programados; se reintentará.")]
    public static partial void SchedulesCycleError(ILogger logger, Exception ex);

    [LoggerMessage(Level = LogLevel.Error,
        Message = "Scheduler Reportes2: error en el ciclo de alertas; se reintentará.")]
    public static partial void AlertsCycleError(ILogger logger, Exception ex);

    [LoggerMessage(Level = LogLevel.Error,
        Message = "Scheduler Reportes2: error procesando el informe programado {ScheduleId}.")]
    public static partial void ScheduleError(ILogger logger, Guid scheduleId, Exception ex);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Scheduler Reportes2: informe {ScheduleId} ('{Name}') enviado a {RecipientCount} destinatario(s).")]
    public static partial void ScheduleSent(ILogger logger, Guid scheduleId, string name, int recipientCount);

    [LoggerMessage(Level = LogLevel.Error,
        Message = "Scheduler Reportes2: fallo el envío del informe {ScheduleId} al destinatario {Recipient}.")]
    public static partial void ScheduleEmailError(ILogger logger, Guid scheduleId, string recipient, Exception ex);

    [LoggerMessage(Level = LogLevel.Error,
        Message = "Scheduler Reportes2: error evaluando la regla de alerta {RuleId}.")]
    public static partial void AlertRuleError(ILogger logger, Guid ruleId, Exception ex);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Scheduler Reportes2: alerta {RuleId} ('{Name}') disparada — valor {MetricValue}, umbral {Threshold}.")]
    public static partial void AlertTriggered(ILogger logger, Guid ruleId, string name, decimal metricValue, decimal threshold);

    [LoggerMessage(Level = LogLevel.Error,
        Message = "Scheduler Reportes2: fallo el correo de la alerta {RuleId} al destinatario {Recipient}.")]
    public static partial void AlertEmailError(ILogger logger, Guid ruleId, string recipient, Exception ex);
}
