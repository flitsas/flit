using Flit.Infrastructure.Persistence;
using Flit.Tramites.Application.Notifications;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Integration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Flit.Infrastructure.Messaging;

/// <summary>
/// HU #11465 (Feature #11459, ADR-0045) — tercer sink del fan-out de cambio de estado:
/// resuelve plantilla + destinatarios e <b>inserta</b> filas en
/// <c>tramites.procedure_state_change_email_dispatches</c>. No hace I/O de red; el envío
/// lo hace el worker (HU #11467). Idempotencia = índices UNIQUE de la base
/// (<c>ON CONFLICT DO NOTHING</c>); en InMemory se degrada a comprobación LINQ.
/// </summary>
internal sealed class ProcedureStateChangeEmailEnqueueNotifier(
    FlitDbContext db,
    ITramiteNotificationRecipientResolver recipientResolver,
    ILogger<ProcedureStateChangeEmailEnqueueNotifier> logger) : IProcedureStateChangeNotifier
{
    public const string StatusPendiente = "pendiente";
    public const string StatusOmitido = "omitido";

    public async Task NotifyAsync(ProcedureStateChangeEvent change, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(change);

        if (change.OutboxId == Guid.Empty)
        {
            EmailEnqueueLog.MissingOutboxId(logger, change.ProcedureInstanceId);
            return;
        }

        var templateKey = TramiteStateChangeTemplateMap.ResolveTemplateKey(change.ToStatus);
        if (templateKey is null)
            return;

        var instance = await db.ProcedureInstances
            .AsNoTracking()
            // ADR-0050 — el proyector del correo decide por la familia del tipo.
            .Include(i => i.ProcedureType)
            .Include(i => i.Actors)
            .Include(i => i.Participants)
            .FirstOrDefaultAsync(
                i => i.Id == change.ProcedureInstanceId && i.TenantId == change.TenantId,
                cancellationToken)
            .ConfigureAwait(false);

        if (instance is null)
        {
            EmailEnqueueLog.InstanceMissing(logger, change.ProcedureInstanceId, change.TenantId);
            return;
        }

        var actors = instance.Actors?.ToList() ?? [];
        var participants = instance.Participants?.ToList() ?? [];
        var resolution = recipientResolver.Resolve(instance, actors, participants);
        var rows = BuildRows(change, templateKey, resolution, logger);

        await InsertIdempotentAsync(rows, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Orden determinista del resolver (comprador→vendedor; empresa→RL) + colapso de
    /// buzón compartido dentro del mismo batch (sobrevive la primera fila).
    /// </summary>
    internal static IReadOnlyList<ProcedureStateChangeEmailDispatch> BuildRows(
        ProcedureStateChangeEvent change,
        string templateKey,
        TramiteRecipientResolution resolution,
        ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(logger);

        var now = DateTimeOffset.UtcNow;
        var rows = new List<ProcedureStateChangeEmailDispatch>();
        var seenEmails = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var recipient in resolution.Recipients)
        {
            var normalized = recipient.Email.Trim();
            var kindDb = KindToDb(recipient.Kind);
            if (!seenEmails.Add(normalized))
            {
                if (logger.IsEnabled(LogLevel.Information))
                {
                    EmailEnqueueLog.MailboxCollapsed(
                        logger, change.OutboxId, recipient.Role, kindDb);
                }

                continue;
            }

            rows.Add(new ProcedureStateChangeEmailDispatch
            {
                Id = Guid.CreateVersion7(),
                TenantId = change.TenantId,
                OutboxId = change.OutboxId,
                ProcedureInstanceId = change.ProcedureInstanceId,
                Recipient = normalized,
                RecipientName = Truncate(recipient.DisplayName, 200),
                RecipientRole = recipient.Role,
                RecipientKind = kindDb,
                TemplateKey = templateKey,
                Status = StatusPendiente,
                FailureReason = null,
                Attempts = 0,
                QueuedAt = now,
                CreatedAt = now,
            });
        }

        foreach (var gap in resolution.Gaps)
        {
            rows.Add(new ProcedureStateChangeEmailDispatch
            {
                Id = Guid.CreateVersion7(),
                TenantId = change.TenantId,
                OutboxId = change.OutboxId,
                ProcedureInstanceId = change.ProcedureInstanceId,
                Recipient = null,
                RecipientName = Truncate(gap.DisplayName, 200),
                RecipientRole = gap.Role,
                RecipientKind = KindToDb(gap.Kind),
                TemplateKey = templateKey,
                Status = StatusOmitido,
                FailureReason = GapReason(gap.Kind),
                Attempts = 0,
                QueuedAt = now,
                ProcessedAt = now,
                CreatedAt = now,
            });
        }

        return rows;
    }

    private async Task InsertIdempotentAsync(
        IReadOnlyList<ProcedureStateChangeEmailDispatch> rows,
        CancellationToken ct)
    {
        if (rows.Count == 0)
            return;

        if (!db.Database.IsRelational())
        {
            foreach (var row in rows)
            {
                if (await ExistsInMemoryAsync(row, ct).ConfigureAwait(false))
                    continue;
                db.ProcedureStateChangeEmailDispatches.Add(row);
            }

            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            return;
        }

        foreach (var row in rows)
        {
            // ON CONFLICT DO NOTHING sin target: absorbe cualquiera de los dos UNIQUE parciales.
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"""
                 INSERT INTO tramites.procedure_state_change_email_dispatches
                     (id, tenant_id, outbox_id, procedure_instance_id, recipient, recipient_name,
                      recipient_role, recipient_kind, template_key, status, failure_reason,
                      attempts, queued_at, processed_at, created_at)
                 VALUES
                     ({row.Id}, {row.TenantId}, {row.OutboxId}, {row.ProcedureInstanceId},
                      {row.Recipient}, {row.RecipientName}, {row.RecipientRole}, {row.RecipientKind},
                      {row.TemplateKey}, {row.Status}, {row.FailureReason}, {row.Attempts},
                      {row.QueuedAt}, {row.ProcessedAt}, {row.CreatedAt})
                 ON CONFLICT DO NOTHING
                 """,
                ct).ConfigureAwait(false);
        }
    }

    private async Task<bool> ExistsInMemoryAsync(ProcedureStateChangeEmailDispatch row, CancellationToken ct)
    {
        if (row.Recipient is null)
        {
            return await db.ProcedureStateChangeEmailDispatches
                .AnyAsync(
                    d => d.OutboxId == row.OutboxId
                         && d.Recipient == null
                         && d.RecipientRole == row.RecipientRole
                         && d.RecipientKind == row.RecipientKind,
                    ct)
                .ConfigureAwait(false);
        }

        var needle = row.Recipient.ToLowerInvariant();
        return await db.ProcedureStateChangeEmailDispatches
            .AnyAsync(
                d => d.OutboxId == row.OutboxId
                     && d.Recipient != null
                     && d.Recipient.ToLower() == needle,
                ct)
            .ConfigureAwait(false);
    }

    internal static string KindToDb(TramiteRecipientKind kind) => kind switch
    {
        TramiteRecipientKind.Persona => "persona",
        TramiteRecipientKind.Empresa => "empresa",
        TramiteRecipientKind.RepresentanteLegal => "representante_legal",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };

    internal static string GapReason(TramiteRecipientKind kind) => kind switch
    {
        TramiteRecipientKind.Persona => "Sin correo para la persona",
        TramiteRecipientKind.Empresa => "Sin correo para la empresa",
        TramiteRecipientKind.RepresentanteLegal => "Sin correo para el representante legal",
        _ => "Sin correo",
    };

    private static string? Truncate(string? value, int max)
    {
        if (string.IsNullOrEmpty(value))
            return value;
        return value.Length <= max ? value : value[..max];
    }
}

internal static partial class EmailEnqueueLog
{
    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Sink correo: evento sin OutboxId para instancia {ProcedureInstanceId}; no se encola.")]
    public static partial void MissingOutboxId(ILogger logger, Guid procedureInstanceId);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Sink correo: instancia {ProcedureInstanceId} (tenant {TenantId}) no encontrada; no se encola.")]
    public static partial void InstanceMissing(ILogger logger, Guid procedureInstanceId, Guid tenantId);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Sink correo: cupo colapsado por buzón compartido (outbox {OutboxId}, rol {Role}, kind {Kind}).")]
    public static partial void MailboxCollapsed(
        ILogger logger, Guid outboxId, string role, string kind);
}
