using Flit.Infrastructure.Notifications.Tramites;
using Flit.Infrastructure.Persistence;
using Flit.Tramites.Application.Notifications;
using Flit.Tramites.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Flit.Infrastructure.Messaging;

/// <summary>
/// HU #11485 (Feature #11482, ADR-0046) — sink post-asignación de placa: resuelve cupos del rol
/// <c>comprador</c> vía <see cref="ITramiteNotificationRecipientResolver"/> e inserta filas en
/// <c>tramites.plate_assignment_email_dispatches</c>. Gemelo de
/// <see cref="ProcedureStateChangeEmailEnqueueNotifier"/> sin outbox ni I/O de red.
/// </summary>
internal sealed class PlateAssignmentEmailEnqueuer(
    FlitDbContext db,
    ITramiteNotificationRecipientResolver recipientResolver,
    ILogger<PlateAssignmentEmailEnqueuer> logger) : IPlateAssignmentEmailEnqueuer
{
    public const string StatusPendiente = "pendiente";
    public const string StatusOmitido = "omitido";

    public async Task EnqueueAsync(
        Guid clientTenantId,
        Guid procedureInstanceId,
        string plate,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(clientTenantId, Guid.Empty);
        ArgumentOutOfRangeException.ThrowIfEqual(procedureInstanceId, Guid.Empty);

        if (string.IsNullOrWhiteSpace(plate))
        {
            PlateAssignmentEnqueueLog.EmptyPlate(logger, procedureInstanceId);
            return;
        }

        var normalizedPlate = plate.Trim().ToUpperInvariant();

        var instance = await db.ProcedureInstances
            .AsNoTracking()
            .Include(i => i.Actors)
            .Include(i => i.Participants)
            .FirstOrDefaultAsync(
                i => i.Id == procedureInstanceId && i.TenantId == clientTenantId,
                cancellationToken)
            .ConfigureAwait(false);

        if (instance is null)
        {
            PlateAssignmentEnqueueLog.InstanceMissing(logger, procedureInstanceId, clientTenantId);
            return;
        }

        var actors = instance.Actors?.ToList() ?? [];
        var participants = instance.Participants?.ToList() ?? [];
        var resolution = FilterCompradorOnly(recipientResolver.Resolve(instance, actors, participants));
        var rows = BuildRows(clientTenantId, procedureInstanceId, normalizedPlate, resolution, logger);

        await InsertIdempotentAsync(rows, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Solo rol comprador — el resolver también produce vendedor en traspaso.</summary>
    internal static TramiteRecipientResolution FilterCompradorOnly(TramiteRecipientResolution resolution)
    {
        var role = TramiteNotificationRecipientResolver.RoleComprador;
        var recipients = resolution.Recipients
            .Where(r => string.Equals(r.Role, role, StringComparison.OrdinalIgnoreCase))
            .ToList();
        var gaps = resolution.Gaps
            .Where(g => string.Equals(g.Role, role, StringComparison.OrdinalIgnoreCase))
            .ToList();
        return new TramiteRecipientResolution(recipients, gaps);
    }

    /// <summary>
    /// Orden determinista del resolver (empresa→RL) + colapso de buzón compartido dentro del batch.
    /// </summary>
    internal static IReadOnlyList<PlateAssignmentEmailDispatch> BuildRows(
        Guid clientTenantId,
        Guid procedureInstanceId,
        string plate,
        TramiteRecipientResolution resolution,
        ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(logger);

        var now = DateTimeOffset.UtcNow;
        var templateKey = AsignacionPlacaEmailComposer.TemplateId;
        var rows = new List<PlateAssignmentEmailDispatch>();
        var seenEmails = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var recipient in resolution.Recipients)
        {
            var normalized = recipient.Email.Trim();
            var kindDb = KindToDb(recipient.Kind);
            if (!seenEmails.Add(normalized))
            {
                if (logger.IsEnabled(LogLevel.Information))
                {
                    PlateAssignmentEnqueueLog.MailboxCollapsed(
                        logger, procedureInstanceId, plate, recipient.Role, kindDb);
                }

                continue;
            }

            rows.Add(new PlateAssignmentEmailDispatch
            {
                Id = Guid.CreateVersion7(),
                TenantId = clientTenantId,
                ProcedureInstanceId = procedureInstanceId,
                Plate = plate,
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
            rows.Add(new PlateAssignmentEmailDispatch
            {
                Id = Guid.CreateVersion7(),
                TenantId = clientTenantId,
                ProcedureInstanceId = procedureInstanceId,
                Plate = plate,
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
        IReadOnlyList<PlateAssignmentEmailDispatch> rows,
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
                db.PlateAssignmentEmailDispatches.Add(row);
            }

            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            return;
        }

        foreach (var row in rows)
        {
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"""
                 INSERT INTO tramites.plate_assignment_email_dispatches
                     (id, tenant_id, procedure_instance_id, plate, recipient, recipient_name,
                      recipient_role, recipient_kind, template_key, status, failure_reason,
                      attempts, queued_at, processed_at, created_at)
                 VALUES
                     ({row.Id}, {row.TenantId}, {row.ProcedureInstanceId}, {row.Plate},
                      {row.Recipient}, {row.RecipientName}, {row.RecipientRole}, {row.RecipientKind},
                      {row.TemplateKey}, {row.Status}, {row.FailureReason}, {row.Attempts},
                      {row.QueuedAt}, {row.ProcessedAt}, {row.CreatedAt})
                 ON CONFLICT DO NOTHING
                 """,
                ct).ConfigureAwait(false);
        }
    }

    private async Task<bool> ExistsInMemoryAsync(PlateAssignmentEmailDispatch row, CancellationToken ct)
    {
        var plateUpper = row.Plate.ToUpperInvariant();

        if (row.Recipient is null)
        {
            return await db.PlateAssignmentEmailDispatches
                .AnyAsync(
                    d => d.ProcedureInstanceId == row.ProcedureInstanceId
                         && d.Plate.ToUpper() == plateUpper
                         && d.Recipient == null
                         && d.RecipientRole == row.RecipientRole
                         && d.RecipientKind == row.RecipientKind,
                    ct)
                .ConfigureAwait(false);
        }

        var needle = row.Recipient.ToLowerInvariant();
        return await db.PlateAssignmentEmailDispatches
            .AnyAsync(
                d => d.ProcedureInstanceId == row.ProcedureInstanceId
                     && d.Plate.ToUpper() == plateUpper
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

internal static partial class PlateAssignmentEnqueueLog
{
    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Sink asignación placa: placa vacía para instancia {ProcedureInstanceId}; no se encola.")]
    public static partial void EmptyPlate(ILogger logger, Guid procedureInstanceId);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Sink asignación placa: instancia {ProcedureInstanceId} (tenant {TenantId}) no encontrada; no se encola.")]
    public static partial void InstanceMissing(ILogger logger, Guid procedureInstanceId, Guid tenantId);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Sink asignación placa: cupo colapsado por buzón compartido (instancia {ProcedureInstanceId}, placa {Plate}, rol {Role}, kind {Kind}).")]
    public static partial void MailboxCollapsed(
        ILogger logger, Guid procedureInstanceId, string plate, string role, string kind);
}
