using Flit.Admin.Domain.Companies.Settings;
using Flit.Infrastructure.Notifications;
using Flit.Infrastructure.Notifications.Routing;
using Flit.Infrastructure.Notifications.Tramites;
using Flit.Infrastructure.Persistence;
using Flit.Modules.Security.Domain.Auth;
using Flit.Tramites.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Flit.Infrastructure.Messaging;

/// <summary>
/// HU #11467 (Feature #11459, ADR-0045) — worker que consume
/// <c>tramites.procedure_state_change_email_dispatches</c>: reclama filas <c>pendiente</c>,
/// compone el cuerpo según el canal del tenant y envía por <see cref="IEmailSender"/>.
/// Reintentos propios (no consume el <c>attempts</c> del outbox de estados).
/// </summary>
internal sealed class ProcedureStateChangeEmailDispatchProcessor(
    IServiceScopeFactory scopeFactory,
    ILogger<ProcedureStateChangeEmailDispatchProcessor> logger) : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(5);
    private const int BatchSize = 50;
    private const int MaxAttempts = ProcedureStateChangeEmailDispatch.MaxDeliveryAttempts;

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
                EmailDispatchLog.CycleError(logger, ex);
            }

            try { await Task.Delay(PollInterval, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }

    /// <summary>Procesa hasta <see cref="BatchSize"/> filas pendientes (una transacción por fila).</summary>
    internal async Task ProcessPendingAsync(CancellationToken ct)
    {
        // No re-reclamar la misma fila en este ciclo: un fallo transitorio debe esperar el
        // próximo poll (AC HU-G), no quemar MaxAttempts en un solo ProcessPendingAsync.
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
        var assets = scope.ServiceProvider.GetRequiredService<IOptions<NotificationEmailAssetsOptions>>().Value;

        if (!db.Database.IsRelational())
        {
            return await ProcessOneInMemoryAsync(db, emailSender, channelResolver, assets, excludeIds, ct);
        }

        var strategy = db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(
            async () => await ProcessOneAsync(db, emailSender, channelResolver, assets, excludeIds, ct));
    }

    private async Task<Guid?> ProcessOneAsync(
        FlitDbContext db,
        IEmailSender emailSender,
        INotificationChannelResolver channelResolver,
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

        var row = await db.ProcedureStateChangeEmailDispatches.FirstAsync(d => d.Id == claimedId.Value, ct);
        await DispatchAsync(row, db, emailSender, channelResolver, assets, ct);

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return claimedId;
    }

    private async Task<Guid?> ProcessOneInMemoryAsync(
        FlitDbContext db,
        IEmailSender emailSender,
        INotificationChannelResolver channelResolver,
        NotificationEmailAssetsOptions assets,
        IReadOnlySet<Guid> excludeIds,
        CancellationToken ct)
    {
        var query = db.ProcedureStateChangeEmailDispatches
            .Where(d => d.Status == StatusPendiente && d.Attempts < MaxAttempts);
        if (excludeIds.Count > 0)
            query = query.Where(d => !excludeIds.Contains(d.Id));

        var row = await query.OrderBy(d => d.QueuedAt).FirstOrDefaultAsync(ct);
        if (row is null)
            return null;

        await DispatchAsync(row, db, emailSender, channelResolver, assets, ct);
        await db.SaveChangesAsync(ct);
        return row.Id;
    }

    private async Task DispatchAsync(
        ProcedureStateChangeEmailDispatch row,
        FlitDbContext db,
        IEmailSender emailSender,
        INotificationChannelResolver channelResolver,
        NotificationEmailAssetsOptions assets,
        CancellationToken ct)
    {
        // Kill-switch (HU #11469): si está apagado, NO incrementar attempts — la fila queda pendiente.
        if (!await IsTramiteStateEmailsEnabledAsync(db, row.TenantId, ct).ConfigureAwait(false))
        {
            EmailDispatchLog.PausedByKillSwitch(logger, row.TenantId, row.ProcedureInstanceId);
            return;
        }

        if (string.IsNullOrWhiteSpace(row.Recipient))
        {
            // Defensivo: las omitidas no se reclaman; si llegara una, no reintentar.
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
                .Include(i => i.Actors)
                .Include(i => i.FieldValues)
                .FirstOrDefaultAsync(
                    i => i.Id == row.ProcedureInstanceId && i.TenantId == row.TenantId,
                    ct)
                .ConfigureAwait(false);

            if (instance is null)
            {
                row.Status = StatusFallido;
                row.FailureReason = "Instancia de trámite no encontrada";
                row.ProcessedAt = DateTimeOffset.UtcNow;
                EmailDispatchLog.InstanceMissing(logger, row.ProcedureInstanceId, row.TenantId);
                return;
            }

            var fieldValues = instance.FieldValues
                .ToDictionary(fv => fv.FieldKey, fv => fv.ValueText, StringComparer.OrdinalIgnoreCase);
            var estado = EstadoFromTemplateKey(row.TemplateKey);

            IReadOnlyList<string>? causales = null;
            string? observacion = null;
            if (string.Equals(row.TemplateKey, TramiteCambioEstadoEmailComposer.TemplateIdRechazado, StringComparison.Ordinal))
            {
                (causales, observacion) = await TramiteRechazoEmailDataLoader
                    .LoadAsync(db, row.TenantId, row.ProcedureInstanceId, ct)
                    .ConfigureAwait(false);
            }

            var typeName = await db.ProcedureTypes
                .AsNoTracking()
                .Where(t => t.Id == instance.ProcedureTypeId)
                .Select(t => t.Name)
                .FirstOrDefaultAsync(ct)
                .ConfigureAwait(false);

            var model = TramiteCambioEstadoEmailProjector.Project(
                instance,
                instance.Actors.ToList(),
                fieldValues,
                estado,
                causales,
                observacion,
                typeName);

            var channel = await channelResolver.ResolveAsync(row.TenantId, ct).ConfigureAwait(false);
            var assetsBaseUrl = assets.BaseUrl;
            var (subject, html) = channel == NotificationChannel.TenantApi
                ? TramiteCambioEstadoEmailComposer.ComposeRenting(model, assetsBaseUrl)
                : TramiteCambioEstadoEmailComposer.ComposeFlit(model, assetsBaseUrl);

            var message = new EmailMessage(
                row.TenantId,
                row.TemplateKey,
                row.Recipient,
                row.RecipientName ?? string.Empty,
                subject,
                html);

            var result = await emailSender.SendAsync(message, ct).ConfigureAwait(false);
            if (result.Success)
            {
                row.Status = StatusEnviado;
                row.FailureReason = null;
                row.ProcessedAt = DateTimeOffset.UtcNow;
                EmailDispatchLog.Sent(logger, row.ProcedureInstanceId, row.RecipientKind);
                return;
            }

            row.FailureReason = Truncate(result.Message, 1000);
            if (row.Attempts >= MaxAttempts)
            {
                row.Status = StatusFallido;
                row.ProcessedAt = DateTimeOffset.UtcNow;
                EmailDispatchLog.DeadLettered(logger, row.ProcedureInstanceId, row.Attempts);
            }
            else
            {
                EmailDispatchLog.SendFailed(logger, row.ProcedureInstanceId, result.Outcome.ToString());
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            row.FailureReason = Truncate(ex.Message, 1000);
            if (row.Attempts >= MaxAttempts)
            {
                row.Status = StatusFallido;
                row.ProcessedAt = DateTimeOffset.UtcNow;
                EmailDispatchLog.DeadLettered(logger, row.ProcedureInstanceId, row.Attempts);
            }
            else
            {
                EmailDispatchLog.SendError(logger, row.ProcedureInstanceId, ex);
            }
        }
    }

    /// <summary>
    /// HU #11469 — interruptor operativo. Sin fila de política o columna ausente ⇒ <c>true</c>
    /// (avisos encendidos; default de despliegue).
    /// </summary>
    private static async Task<bool> IsTramiteStateEmailsEnabledAsync(
        FlitDbContext db, Guid tenantId, CancellationToken ct)
    {
        var enabled = await db.TenantOperationalPolicies.AsNoTracking()
            .Where(p => p.TenantId == tenantId)
            .Select(p => (bool?)p.TramiteStateEmailsEnabled)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);
        return enabled ?? true;
    }

    private static string EstadoFromTemplateKey(string templateKey)
    {
        if (string.Equals(templateKey, TramiteCambioEstadoEmailComposer.TemplateIdAprobado, StringComparison.Ordinal))
            return "aprobado";
        if (string.Equals(templateKey, TramiteCambioEstadoEmailComposer.TemplateIdRechazado, StringComparison.Ordinal))
            return "rechazado";
        return templateKey;
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
                FROM tramites.procedure_state_change_email_dispatches
                WHERE status = 'pendiente'
                  AND attempts < @max_attempts
                ORDER BY queued_at
                LIMIT 1
                FOR UPDATE SKIP LOCKED
                """;
        }
        else
        {
            // Exclusión por ciclo: evita quemar MaxAttempts sobre la misma fila en un solo poll.
            var ids = string.Join(",", excludeIds.Select(id => $"'{id:D}'"));
            cmd.CommandText = $"""
                SELECT id
                FROM tramites.procedure_state_change_email_dispatches
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

internal static partial class EmailDispatchLog
{
    [LoggerMessage(Level = LogLevel.Information,
        Message = "Cola correo estados: instancia {ProcedureInstanceId} enviada (kind {RecipientKind}).")]
    public static partial void Sent(ILogger logger, Guid procedureInstanceId, string recipientKind);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Cola correo estados: fallo de envío para instancia {ProcedureInstanceId} ({Outcome}); se reintentará.")]
    public static partial void SendFailed(ILogger logger, Guid procedureInstanceId, string outcome);

    [LoggerMessage(Level = LogLevel.Error,
        Message = "Cola correo estados: error al enviar instancia {ProcedureInstanceId}; se reintentará.")]
    public static partial void SendError(ILogger logger, Guid procedureInstanceId, Exception ex);

    [LoggerMessage(Level = LogLevel.Critical,
        Message = "Cola correo estados: la instancia {ProcedureInstanceId} agotó los reintentos ({Attempts}); queda fallido. Requiere revisión manual.")]
    public static partial void DeadLettered(ILogger logger, Guid procedureInstanceId, int attempts);

    [LoggerMessage(Level = LogLevel.Error,
        Message = "Cola correo estados: error en el ciclo de sondeo; se reintentará.")]
    public static partial void CycleError(ILogger logger, Exception ex);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Cola correo estados: instancia {ProcedureInstanceId} (tenant {TenantId}) no encontrada.")]
    public static partial void InstanceMissing(ILogger logger, Guid procedureInstanceId, Guid tenantId);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Cola correo estados: envíos pausados por kill-switch (tenant {TenantId}, instancia {ProcedureInstanceId}).")]
    public static partial void PausedByKillSwitch(ILogger logger, Guid tenantId, Guid procedureInstanceId);
}
