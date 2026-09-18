using Flit.Admin.Domain.Companies.Settings;
using Flit.Infrastructure.Notifications;
using Flit.Infrastructure.Notifications.Routing;
using Flit.Infrastructure.Notifications.Tramites;
using Flit.Infrastructure.Persistence;
using Flit.Modules.Security.Domain.Auth;
using Flit.Tramites.Domain.RevocationRequests;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Flit.Infrastructure.Messaging;

/// <summary>
/// HU #12579 (Feature #12565, ADR-0046 Opción B extendido) — worker que consume
/// <c>tramites.revocation_request_email_dispatches</c>: reclama filas <c>pendiente</c>, compone el
/// cuerpo del hito (<c>row.Milestone</c>) según la marca del canal del tenant cliente y envía por
/// <see cref="IEmailSender"/>. Mismo patrón de reclamo/reintentos/kill-switch que
/// <see cref="PlateAssignmentEmailDispatchProcessor"/> (el gemelo de ADR-0046); la resolución de
/// marca reutiliza <see cref="INotificationChannelResolver"/> directamente — igual que
/// <see cref="ProcedureStateChangeEmailDispatchProcessor"/> — en vez de un wrapper dedicado: aquí no
/// hay una regla de marca distinta que envolver.
/// </summary>
internal sealed class RevocationRequestEmailDispatchProcessor(
    IServiceScopeFactory scopeFactory,
    ILogger<RevocationRequestEmailDispatchProcessor> logger) : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(5);
    private const int BatchSize = 50;
    private const int MaxAttempts = RevocationRequestEmailDispatch.MaxDeliveryAttempts;

    public const string StatusPendiente = "pendiente";
    public const string StatusEnviado = "enviado";
    public const string StatusFallido = "fallido";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try { await Task.Delay(StartupDelay, stoppingToken); }
        catch (OperationCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessPendingAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                RevocationRequestEmailDispatchLog.CycleError(logger, ex);
            }

            try { await Task.Delay(PollInterval, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }

    /// <summary>Procesa hasta <see cref="BatchSize"/> filas pendientes (una transacción por fila).</summary>
    internal async Task ProcessPendingAsync(CancellationToken ct)
    {
        var seen = new HashSet<Guid>();
        for (var i = 0; i < BatchSize; i++)
        {
            if (ct.IsCancellationRequested)
                break;
            var claimedId = await ProcessNextClaimedAsync(seen, ct);
            if (claimedId is null)
                break;
            seen.Add(claimedId.Value);
        }
    }

    private async Task<Guid?> ProcessNextClaimedAsync(IReadOnlySet<Guid> excludeIds, CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FlitDbContext>();
        var emailSender = scope.ServiceProvider.GetRequiredService<IEmailSender>();
        var channelResolver = scope.ServiceProvider.GetRequiredService<INotificationChannelResolver>();
        var themeResolver = scope.ServiceProvider.GetRequiredService<IEmailThemeResolver>();
        var assets = scope.ServiceProvider.GetRequiredService<IOptions<NotificationEmailAssetsOptions>>().Value;

        if (!db.Database.IsRelational())
        {
            return await ProcessOneInMemoryAsync(db, emailSender, channelResolver, themeResolver, assets, excludeIds, ct);
        }

        var strategy = db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(
            async () => await ProcessOneAsync(db, emailSender, channelResolver, themeResolver, assets, excludeIds, ct));
    }

    private async Task<Guid?> ProcessOneAsync(
        FlitDbContext db,
        IEmailSender emailSender,
        INotificationChannelResolver channelResolver,
        IEmailThemeResolver themeResolver,
        NotificationEmailAssetsOptions assets,
        IReadOnlySet<Guid> excludeIds,
        CancellationToken ct)
    {
        db.ChangeTracker.Clear();
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var claimedId = await ClaimNextIdAsync(db, excludeIds, ct);
        if (claimedId is null)
        {
            await tx.CommitAsync(ct);
            return null;
        }

        var row = await db.RevocationRequestEmailDispatches.FirstAsync(d => d.Id == claimedId.Value, ct);
        await DispatchAsync(row, db, emailSender, channelResolver, themeResolver, assets, ct);

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return claimedId;
    }

    private async Task<Guid?> ProcessOneInMemoryAsync(
        FlitDbContext db,
        IEmailSender emailSender,
        INotificationChannelResolver channelResolver,
        IEmailThemeResolver themeResolver,
        NotificationEmailAssetsOptions assets,
        IReadOnlySet<Guid> excludeIds,
        CancellationToken ct)
    {
        var query = db.RevocationRequestEmailDispatches
            .Where(d => d.Status == StatusPendiente && d.Attempts < MaxAttempts);
        if (excludeIds.Count > 0)
            query = query.Where(d => !excludeIds.Contains(d.Id));

        var row = await query.OrderBy(d => d.QueuedAt).FirstOrDefaultAsync(ct);
        if (row is null)
            return null;

        await DispatchAsync(row, db, emailSender, channelResolver, themeResolver, assets, ct);
        await db.SaveChangesAsync(ct);
        return row.Id;
    }

    private async Task DispatchAsync(
        RevocationRequestEmailDispatch row,
        FlitDbContext db,
        IEmailSender emailSender,
        INotificationChannelResolver channelResolver,
        IEmailThemeResolver themeResolver,
        NotificationEmailAssetsOptions assets,
        CancellationToken ct)
    {
        // Kill-switch: reutiliza el mismo interruptor operativo de correos de trámite del tenant
        // cliente que PlateAssignmentEmailDispatchProcessor/ProcedureStateChangeEmailDispatchProcessor
        // (TenantOperationalPolicies.TramiteApprovedEmailsEnabled) — no existe (aún) un interruptor
        // específico de revocatoria; crear uno nuevo por 3 plantillas más es sobre-diseño para v1 (ver
        // reporte de la HU #12579). Sin incrementar attempts si está apagado.
        if (!await IsTramiteStateEmailsEnabledAsync(db, row.TenantId, ct).ConfigureAwait(false))
        {
            RevocationRequestEmailDispatchLog.PausedByKillSwitch(
                logger, row.TenantId, row.ProcedureInstanceId, row.Milestone);
            return;
        }

        if (string.IsNullOrWhiteSpace(row.Recipient))
        {
            row.Status = StatusFallido;
            row.FailureReason = "Destinatario vacío";
            row.ProcessedAt = DateTimeOffset.UtcNow;
            return;
        }

        row.Attempts += 1;

        try
        {
            var instance = await db.ProcedureInstances
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    i => i.Id == row.ProcedureInstanceId && i.TenantId == row.TenantId,
                    ct)
                .ConfigureAwait(false);

            if (instance is null)
            {
                row.Status = StatusFallido;
                row.FailureReason = "Instancia de trámite no encontrada";
                row.ProcessedAt = DateTimeOffset.UtcNow;
                RevocationRequestEmailDispatchLog.InstanceMissing(
                    logger, row.ProcedureInstanceId, row.TenantId);
                return;
            }

            var model = new RevocationRequestEmailModel(
                DestinatarioNombre: row.RecipientName ?? string.Empty,
                Placa: instance.Plate?.Trim() ?? string.Empty,
                Radicado: instance.ReferenceNumber?.Trim() ?? string.Empty,
                Motivo: row.DecisionReason);

            var channel = await channelResolver.ResolveAsync(row.TenantId, ct).ConfigureAwait(false);
            var assetsBaseUrl = assets.BaseUrl;
            // HU #12428 AC1/AC8 — el canal Renting no cambia (mismo criterio que
            // ProcedureStateChangeEmailDispatchProcessor): solo la variante FLIT resuelve tema.
            var theme = channel == NotificationChannel.TenantApi
                ? EmailTheme.Flit
                : await themeResolver.ResolveAsync(row.TenantId, ct).ConfigureAwait(false);
            var (subject, html) = channel == NotificationChannel.TenantApi
                ? RevocationRequestEmailComposer.ComposeRenting(row.Milestone, model, assetsBaseUrl)
                : RevocationRequestEmailComposer.ComposeFlit(row.Milestone, model, assetsBaseUrl, theme);

            var message = new EmailMessage(
                row.TenantId,
                row.TemplateKey,
                row.Recipient,
                row.RecipientName ?? string.Empty,
                subject,
                html)
            {
                ThemeKind = channel == NotificationChannel.TenantApi ? null : theme.KindWireValue,
                ThemeVersion = channel == NotificationChannel.TenantApi ? null : (theme.IsBrand ? theme.Version : null),
                // HU #12430 AC1/AC2 — TenantApi nunca resuelve marca (theme queda
                // EmailTheme.Flit arriba), así que IsBrand ya es false en ese caso.
                SenderDisplayName = theme.IsBrand ? theme.PlatformName : null,
            };

            var result = await emailSender.SendAsync(message, ct).ConfigureAwait(false);
            if (result.Success)
            {
                row.Status = StatusEnviado;
                row.FailureReason = null;
                row.ProcessedAt = DateTimeOffset.UtcNow;
                RevocationRequestEmailDispatchLog.Sent(
                    logger, row.ProcedureInstanceId, row.Milestone, row.RecipientKind);
                return;
            }

            row.FailureReason = Truncate(result.Message, 1000);
            if (row.Attempts >= MaxAttempts)
            {
                row.Status = StatusFallido;
                row.ProcessedAt = DateTimeOffset.UtcNow;
                RevocationRequestEmailDispatchLog.DeadLettered(
                    logger, row.ProcedureInstanceId, row.Milestone, row.Attempts);
            }
            else
            {
                RevocationRequestEmailDispatchLog.SendFailed(
                    logger, row.ProcedureInstanceId, row.Milestone, result.Outcome.ToString());
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            row.FailureReason = Truncate(ex.Message, 1000);
            if (row.Attempts >= MaxAttempts)
            {
                row.Status = StatusFallido;
                row.ProcessedAt = DateTimeOffset.UtcNow;
                RevocationRequestEmailDispatchLog.DeadLettered(
                    logger, row.ProcedureInstanceId, row.Milestone, row.Attempts);
            }
            else
            {
                RevocationRequestEmailDispatchLog.SendError(
                    logger, row.ProcedureInstanceId, row.Milestone, ex);
            }
        }
    }

    private static async Task<bool> IsTramiteStateEmailsEnabledAsync(
        FlitDbContext db, Guid tenantId, CancellationToken ct)
    {
        var enabled = await db.TenantOperationalPolicies.AsNoTracking()
            .Where(p => p.TenantId == tenantId)
            .Select(p => (bool?)p.TramiteApprovedEmailsEnabled)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);
        return enabled ?? true;
    }

    private static string? Truncate(string? value, int max)
    {
        if (string.IsNullOrEmpty(value))
            return value;
        return value.Length <= max ? value : value[..max];
    }

    private static async Task<Guid?> ClaimNextIdAsync(
        FlitDbContext db, IReadOnlySet<Guid> excludeIds, CancellationToken ct)
    {
        var connection = db.Database.GetDbConnection();
        var transaction = db.Database.CurrentTransaction!.GetDbTransaction();

        await using var cmd = connection.CreateCommand();
        cmd.Transaction = transaction;

        if (excludeIds.Count == 0)
        {
            cmd.CommandText = """
                SELECT id
                FROM tramites.revocation_request_email_dispatches
                WHERE status = 'pendiente'
                  AND attempts < @max_attempts
                ORDER BY queued_at
                LIMIT 1
                FOR UPDATE SKIP LOCKED
                """;
        }
        else
        {
            var ids = string.Join(",", excludeIds.Select(id => $"'{id:D}'"));
            cmd.CommandText = $"""
                SELECT id
                FROM tramites.revocation_request_email_dispatches
                WHERE status = 'pendiente'
                  AND attempts < @max_attempts
                  AND id NOT IN ({ids})
                ORDER BY queued_at
                LIMIT 1
                FOR UPDATE SKIP LOCKED
                """;
        }

        var pMax = cmd.CreateParameter();
        pMax.ParameterName = "max_attempts";
        pMax.Value = MaxAttempts;
        cmd.Parameters.Add(pMax);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? reader.GetGuid(0) : null;
    }
}

internal static partial class RevocationRequestEmailDispatchLog
{
    [LoggerMessage(Level = LogLevel.Information,
        Message = "Cola correo revocatoria: instancia {ProcedureInstanceId} hito {Milestone} enviado (kind {RecipientKind}).")]
    public static partial void Sent(
        ILogger logger, Guid procedureInstanceId, string milestone, string recipientKind);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Cola correo revocatoria: fallo de envío para instancia {ProcedureInstanceId} hito {Milestone} ({Outcome}); se reintentará.")]
    public static partial void SendFailed(
        ILogger logger, Guid procedureInstanceId, string milestone, string outcome);

    [LoggerMessage(Level = LogLevel.Error,
        Message = "Cola correo revocatoria: error al enviar instancia {ProcedureInstanceId} hito {Milestone}; se reintentará.")]
    public static partial void SendError(
        ILogger logger, Guid procedureInstanceId, string milestone, Exception ex);

    [LoggerMessage(Level = LogLevel.Critical,
        Message = "Cola correo revocatoria: instancia {ProcedureInstanceId} hito {Milestone} agotó los reintentos ({Attempts}); queda fallido. Requiere revisión manual.")]
    public static partial void DeadLettered(
        ILogger logger, Guid procedureInstanceId, string milestone, int attempts);

    [LoggerMessage(Level = LogLevel.Error,
        Message = "Cola correo revocatoria: error en el ciclo de sondeo; se reintentará.")]
    public static partial void CycleError(ILogger logger, Exception ex);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Cola correo revocatoria: instancia {ProcedureInstanceId} (tenant {TenantId}) no encontrada.")]
    public static partial void InstanceMissing(
        ILogger logger, Guid procedureInstanceId, Guid tenantId);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Cola correo revocatoria: envíos pausados por kill-switch (tenant {TenantId}, instancia {ProcedureInstanceId}, hito {Milestone}).")]
    public static partial void PausedByKillSwitch(
        ILogger logger, Guid tenantId, Guid procedureInstanceId, string milestone);
}
