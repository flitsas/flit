using Flit.Admin.Domain.Companies.Settings;
using Flit.Infrastructure.Notifications;
using Flit.Infrastructure.Notifications.Routing;
using Flit.Infrastructure.Notifications.Tramites;
using Flit.Infrastructure.Persistence;
using Flit.Modules.Security.Domain.Auth;
using Flit.Tramites.Application.Notifications;
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
        var batch = await LoadBatchAsync(db, row.OutboxId, ct);
        await DispatchBatchAsync(batch, db, emailSender, channelResolver, assets, ct);

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

        var seed = await query.OrderBy(d => d.QueuedAt).FirstOrDefaultAsync(ct);
        if (seed is null)
            return null;

        var batch = await db.ProcedureStateChangeEmailDispatches
            .Where(d => d.OutboxId == seed.OutboxId
                        && d.Status == StatusPendiente
                        && d.Attempts < MaxAttempts)
            .ToListAsync(ct);

        await DispatchBatchAsync(batch, db, emailSender, channelResolver, assets, ct);
        await db.SaveChangesAsync(ct);
        return seed.Id;
    }

    private static async Task<List<ProcedureStateChangeEmailDispatch>> LoadBatchAsync(
        FlitDbContext db, Guid outboxId, CancellationToken ct) =>
        await db.ProcedureStateChangeEmailDispatches
            .Where(d => d.OutboxId == outboxId
                        && d.Status == StatusPendiente
                        && d.Attempts < MaxAttempts)
            .ToListAsync(ct)
            .ConfigureAwait(false);

    private async Task DispatchBatchAsync(
        List<ProcedureStateChangeEmailDispatch> batch,
        FlitDbContext db,
        IEmailSender emailSender,
        INotificationChannelResolver channelResolver,
        NotificationEmailAssetsOptions assets,
        CancellationToken ct)
    {
        if (batch.Count == 0)
            return;

        var seed = batch[0];
        if (!await IsTemplateEmailsEnabledAsync(db, seed.TenantId, seed.TemplateKey, ct).ConfigureAwait(false))
        {
            EmailDispatchLog.PausedByKillSwitch(logger, seed.TenantId, seed.ProcedureInstanceId);
            return;
        }

        var withEmail = batch.Where(r => !string.IsNullOrWhiteSpace(r.Recipient)).ToList();
        foreach (var empty in batch.Where(r => string.IsNullOrWhiteSpace(r.Recipient)))
        {
            empty.Status = StatusFallido;
            empty.FailureReason = "Destinatario vacío";
            empty.ProcessedAt = DateTimeOffset.UtcNow;
        }

        if (withEmail.Count == 0)
            return;

        var groups = BuildGroups(withEmail);

        foreach (var row in withEmail)
            row.Attempts += 1;

        try
        {
            var instance = await db.ProcedureInstances
                .AsNoTracking()
                .Include(i => i.Actors)
                .Include(i => i.FieldValues)
                // ADR-0050 dejó que la familia decidiera si hay parte vendedora, y esta consulta
                // ad-hoc —la única del código que no pasa por ProcedureInstanceRepository— se quedó
                // sin la navegación: llegaba null y todo traspaso se componía como si no lo fuera.
                .Include(i => i.ProcedureType)
                .FirstOrDefaultAsync(
                    i => i.Id == seed.ProcedureInstanceId && i.TenantId == seed.TenantId,
                    ct)
                .ConfigureAwait(false);

            if (instance is null)
            {
                foreach (var row in withEmail)
                {
                    row.Status = StatusFallido;
                    row.FailureReason = "Instancia de trámite no encontrada";
                    row.ProcessedAt = DateTimeOffset.UtcNow;
                }

                EmailDispatchLog.InstanceMissing(logger, seed.ProcedureInstanceId, seed.TenantId);
                return;
            }

            var fieldValues = instance.FieldValues
                .ToDictionary(fv => fv.FieldKey, fv => fv.ValueText, StringComparer.OrdinalIgnoreCase);
            var estado = EstadoFromTemplateKey(seed.TemplateKey);

            IReadOnlyList<string>? causales = null;
            string? observacion = null;
            if (string.Equals(seed.TemplateKey, TramiteCambioEstadoEmailComposer.TemplateIdRechazado, StringComparison.Ordinal))
            {
                (causales, observacion) = await TramiteRechazoEmailDataLoader
                    .LoadAsync(db, seed.TenantId, seed.ProcedureInstanceId, ct)
                    .ConfigureAwait(false);
            }

            var typeName = await db.ProcedureTypes
                .AsNoTracking()
                .Where(t => t.Id == instance.ProcedureTypeId)
                .Select(t => t.Name)
                .FirstOrDefaultAsync(ct)
                .ConfigureAwait(false);

            var baseModel = TramiteCambioEstadoEmailProjector.Project(
                instance,
                instance.Actors.ToList(),
                fieldValues,
                estado,
                causales,
                observacion,
                typeName);

            var channel = await channelResolver.ResolveAsync(seed.TenantId, ct).ConfigureAwait(false);
            var assetsBaseUrl = assets.BaseUrl;
            string? lastOutcome = null;

            foreach (var group in groups)
            {
                var carrier = group.To;
                var model = group.Personalize
                    ? baseModel with
                    {
                        DestinatarioNombre = carrier.RecipientName ?? string.Empty,
                        DestinatarioEsEmpresa = string.Equals(
                            carrier.RecipientKind, "empresa", StringComparison.OrdinalIgnoreCase),
                    }
                    : baseModel;

                var (subject, html) = channel == NotificationChannel.TenantApi
                    ? TramiteCambioEstadoEmailComposer.ComposeRenting(model, assetsBaseUrl)
                    : TramiteCambioEstadoEmailComposer.ComposeFlit(model, assetsBaseUrl);

                var message = new EmailMessage(
                    seed.TenantId,
                    seed.TemplateKey,
                    carrier.Recipient!,
                    carrier.RecipientName ?? string.Empty,
                    subject,
                    html)
                {
                    BccEmails = group.Bcc,
                };

                var result = await emailSender.SendAsync(message, ct).ConfigureAwait(false);
                if (result.Success)
                {
                    var now = DateTimeOffset.UtcNow;
                    foreach (var row in group.Rows)
                    {
                        row.Status = StatusEnviado;
                        row.FailureReason = null;
                        row.ProcessedAt = now;
                    }

                    EmailDispatchLog.Sent(logger, seed.ProcedureInstanceId, carrier.RecipientKind);
                    continue;
                }

                lastOutcome = result.Outcome.ToString();
                foreach (var row in group.Rows)
                {
                    row.FailureReason = Truncate(result.Message, 1000);
                    if (row.Attempts >= MaxAttempts)
                    {
                        row.Status = StatusFallido;
                        row.ProcessedAt = DateTimeOffset.UtcNow;
                    }
                }
            }

            // Cada grupo es un envío independiente: lo que salió queda enviado y no se reintenta,
            // y el próximo poll recarga solo las filas que siguen pendientes.
            var noEnviadas = withEmail.Where(r => r.Status != StatusEnviado).ToList();
            if (noEnviadas.Count == 0)
                return;

            if (noEnviadas[0].Attempts >= MaxAttempts)
                EmailDispatchLog.DeadLettered(logger, seed.ProcedureInstanceId, noEnviadas[0].Attempts);
            else
                EmailDispatchLog.SendFailed(logger, seed.ProcedureInstanceId, lastOutcome ?? "desconocido");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            foreach (var row in withEmail.Where(r => r.Status != StatusEnviado))
            {
                row.FailureReason = Truncate(ex.Message, 1000);
                if (row.Attempts >= MaxAttempts)
                {
                    row.Status = StatusFallido;
                    row.ProcessedAt = DateTimeOffset.UtcNow;
                }
            }

            if (withEmail[0].Attempts >= MaxAttempts)
                EmailDispatchLog.DeadLettered(logger, seed.ProcedureInstanceId, withEmail[0].Attempts);
            else
                EmailDispatchLog.SendError(logger, seed.ProcedureInstanceId, ex);
        }
    }

    /// <summary>
    /// Un envío por parte del trámite —comprador, locatario, vendedor—, cada una saludada con su
    /// propio nombre: el vendedor dejó de recibir el correo dirigido al comprador.
    ///
    /// <para>El gestor que radicó y el correo extra de «Destinatarios de avisos de estado» no son
    /// parte del trámite y no tienen un nombre propio que sostenga un saludo (el correo extra
    /// guarda la dirección misma como nombre), así que siguen viajando en copia oculta del envío
    /// de la parte principal, igual que antes de separar.</para>
    ///
    /// <para>Si no hay ninguna parte con correo —política que solo notifica al gestor, o partes sin
    /// dirección registrada— sale un único correo sin personalizar, que es exactamente el
    /// comportamiento anterior.</para>
    /// </summary>
    internal static List<EmailGroup> BuildGroups(
        IReadOnlyList<ProcedureStateChangeEmailDispatch> withEmail)
    {
        var partes = withEmail.Where(r => IsParte(r.RecipientRole)).ToList();
        var copias = withEmail.Where(r => !IsParte(r.RecipientRole)).ToList();

        if (partes.Count == 0)
        {
            if (copias.Count == 0)
                return [];
            var unico = PickPrimary(copias);
            return [new EmailGroup(unico, BccEmails(copias, unico), copias, Personalize: false)];
        }

        var portador = PickPrimary(partes);
        var groups = new List<EmailGroup>(partes.Count);
        foreach (var parte in partes
                     .OrderBy(r => RoleRank(r.RecipientRole))
                     .ThenBy(r => KindRank(r.RecipientKind)))
        {
            var esPortador = parte.Id == portador.Id;
            var rows = new List<ProcedureStateChangeEmailDispatch> { parte };
            if (esPortador)
                rows.AddRange(copias);

            groups.Add(new EmailGroup(
                parte,
                esPortador ? BccEmails(copias, parte) : [],
                rows,
                Personalize: true));
        }

        return groups;
    }

    private static bool IsParte(string? role) =>
        string.Equals(role, TramiteNotificationRecipientResolver.RoleComprador, StringComparison.OrdinalIgnoreCase)
        || string.Equals(role, TramiteNotificationRecipientResolver.RoleLocatario, StringComparison.OrdinalIgnoreCase)
        || string.Equals(role, TramiteNotificationRecipientResolver.RoleVendedor, StringComparison.OrdinalIgnoreCase);

    private static List<string> BccEmails(
        IEnumerable<ProcedureStateChangeEmailDispatch> rows,
        ProcedureStateChangeEmailDispatch destinatario) =>
        rows.Where(r => r.Id != destinatario.Id)
            .Select(r => r.Recipient!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    /// <summary>Un correo del lote: a quién se dirige, quién va en copia oculta y qué filas cierra.</summary>
    internal sealed record EmailGroup(
        ProcedureStateChangeEmailDispatch To,
        List<string> Bcc,
        List<ProcedureStateChangeEmailDispatch> Rows,
        bool Personalize);

    internal static ProcedureStateChangeEmailDispatch PickPrimary(
        IReadOnlyList<ProcedureStateChangeEmailDispatch> withEmail)
    {
        var comprador = withEmail
            .Where(r => string.Equals(r.RecipientRole, "comprador", StringComparison.OrdinalIgnoreCase))
            .OrderBy(r => KindRank(r.RecipientKind))
            .FirstOrDefault();
        if (comprador is not null)
            return comprador;

        return withEmail
            .OrderBy(r => RoleRank(r.RecipientRole))
            .ThenBy(r => KindRank(r.RecipientKind))
            .First();
    }

    private static int KindRank(string? kind) => kind switch
    {
        "empresa" => 0,
        "representante_legal" => 1,
        _ => 2,
    };

    private static int RoleRank(string? role) => role switch
    {
        "comprador" => 0,
        "locatario" => 1,
        "vendedor" => 2,
        "radicador" => 3,
        _ => 4,
    };

    private static async Task<bool> IsTemplateEmailsEnabledAsync(
        FlitDbContext db, Guid tenantId, string templateKey, CancellationToken ct)
    {
        var policy = await db.TenantOperationalPolicies.AsNoTracking()
            .Where(p => p.TenantId == tenantId)
            .Select(p => new { p.TramiteApprovedEmailsEnabled, p.TramiteRejectedEmailsEnabled })
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);
        if (policy is null)
            return true;
        if (string.Equals(templateKey, TramiteCambioEstadoEmailComposer.TemplateIdRechazado, StringComparison.Ordinal))
            return policy.TramiteRejectedEmailsEnabled;
        return policy.TramiteApprovedEmailsEnabled;
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
