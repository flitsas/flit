using Flit.Infrastructure.Analytics.Scheduling;
using Flit.Tramites.Application.UseCases.RuntConfirmation;
using Flit.Tramites.Domain.RuntConfirmation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Flit.Infrastructure.RuntConfirmation;

/// <summary>
/// El «cron» de la Confirmación RUNT (HU #12309 AC1): un <see cref="BackgroundService"/> dentro de
/// core-api —ADR-0024 rechaza cron/broker externo—, patrón de <c>QuipuxStatusPollProcessor</c> con
/// programación a hora fija como <c>AnalyticsSchedulerProcessor</c>.
///
/// Cada minuto relee la configuración de BD (encender, cambiar la hora o el proveedor surte efecto
/// sin reiniciar) y dispara UNA corrida programada por día local (America/Bogota) cuando la hora
/// local ya pasó <c>run_at_local</c> y la última corrida programada no es de hoy. Que «ya pasó» y
/// no «es exactamente» hace que una caída del proceso a las 02:00 no pierda el día: corre al volver.
/// Con el interruptor apagado la corrida deja igualmente su fila con <c>skipped_reason=disabled</c>,
/// que es la evidencia de que el cron sí miró; esa fila no cuenta como «la de hoy»: al encender con la
/// hora ya pasada, la corrida real sale en el siguiente tick.
/// </summary>
internal sealed class RuntConfirmationSchedulerProcessor(
    IServiceScopeFactory scopeFactory,
    ILogger<RuntConfirmationSchedulerProcessor> logger) : BackgroundService
{
    private static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(25);
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(60);

    internal static readonly TimeZoneInfo BogotaTimeZone = ScheduleDueEvaluator.BogotaTimeZone;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try { await Task.Delay(StartupDelay, stoppingToken).ConfigureAwait(false); }
        catch (OperationCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await TickAsync(DateTimeOffset.UtcNow, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                SchedulerLog.TickFailed(logger, ex);
            }

            try { await Task.Delay(PollInterval, stoppingToken).ConfigureAwait(false); }
            catch (OperationCanceledException) { break; }
        }
    }

    internal async Task TickAsync(DateTimeOffset nowUtc, CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var settingsRepo = scope.ServiceProvider.GetRequiredService<IRuntConfirmationSettingsRepository>();
        var store = scope.ServiceProvider.GetRequiredService<IRuntConfirmationStore>();

        var settings = await settingsRepo.GetAsync(ct).ConfigureAwait(false);
        var last = await store.GetLastScheduledRunStartedAtAsync(ct).ConfigureAwait(false);

        if (!IsDue(settings.RunAtLocal, last, nowUtc, BogotaTimeZone))
            return;

        var runner = scope.ServiceProvider.GetRequiredService<RuntConfirmationRunner>();
        await runner.RunAsync(new RuntConfirmationRunRequest(RuntConfirmationRunTriggers.Scheduled), ct).ConfigureAwait(false);
    }

    /// <summary>Decisión PURA: hoy (local) ya pasó la hora configurada y la última corrida programada no es de hoy.</summary>
    internal static bool IsDue(string runAtLocal, DateTimeOffset? lastScheduledUtc, DateTimeOffset nowUtc, TimeZoneInfo tz)
    {
        ArgumentNullException.ThrowIfNull(tz);

        if (!RuntConfirmationSettings.TryParseRunAt(runAtLocal, out var runAt))
            runAt = TimeOnly.ParseExact(RuntConfirmationSettings.DefaultRunAtLocal, "HH:mm", System.Globalization.CultureInfo.InvariantCulture);

        var nowLocal = TimeZoneInfo.ConvertTime(nowUtc, tz);
        if (TimeOnly.FromDateTime(nowLocal.DateTime) < runAt)
            return false;

        if (lastScheduledUtc is null)
            return true;

        var lastLocal = TimeZoneInfo.ConvertTime(lastScheduledUtc.Value, tz);
        return lastLocal.Date < nowLocal.Date;
    }
}

internal static partial class SchedulerLog
{
    [LoggerMessage(Level = LogLevel.Error, Message = "Confirmación RUNT: el ciclo del programador falló; se reintenta en el siguiente minuto.")]
    public static partial void TickFailed(ILogger logger, Exception ex);
}
