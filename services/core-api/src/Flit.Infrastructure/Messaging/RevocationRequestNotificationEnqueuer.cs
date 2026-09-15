using System.Text.Json;
using Flit.Infrastructure.Persistence;
using Flit.Tramites.Application.Notifications;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Integration;
using Flit.Tramites.Domain.RevocationRequests;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Flit.Infrastructure.Messaging;

/// <summary>
/// HU #12572/#12576 (Feature #12565, AC3/AC4) — sink del sub-flujo de revocatoria. Resuelve el cupo
/// <c>radicador</c> (quien envió LA SOLICITUD, no comprador/vendedor del trámite: es un acuse de
/// recibo/decisión, no un aviso de cambio de estado) vía <see cref="ITramiteNotificationRecipientResolver"/>
/// SIN modificarlo, y hace DOS cosas por cada hito:
/// <list type="number">
/// <item>Deja constancia del encolado como evento PROPIO de bitácora (<see cref="ProcedureInstanceEvent"/>,
/// tipo <see cref="EventoTipo"/>/<see cref="DecisionEventoTipo"/>) — mismo mecanismo que ya usa
/// <c>AdminAnularHandler</c> para eventos que no son <c>cambio_estado</c>. Se conserva (no se
/// reemplaza) porque es trazabilidad de bajo costo, ya en producción, sin otro consumidor que
/// dependa de su ausencia.</item>
/// <item><b>HU #12579:</b> inserta filas reales en la cola propia
/// <c>tramites.revocation_request_email_dispatches</c>, que <see cref="RevocationRequestEmailDispatchProcessor"/>
/// consume y envía por <see cref="Flit.Modules.Security.Domain.Auth.IEmailSender"/>. Mismo patrón
/// que <c>PlateAssignmentEmailEnqueuer</c> (ADR-0046 Opción B): la resolución de destinatarios vive
/// aquí, el envío vive en el worker.</item>
/// </list>
///
/// <para>
/// NO reutiliza <c>procedure_state_change_outbox</c> ni fabrica una fila sintética: mismo motivo que
/// documentó ADR-0046 §Contexto punto 2 para el sub-estado de placa (activaría el fan-out hacia los
/// webhooks OT/ICT como si esto fuera un cambio de <c>status</c>, que no lo es).
/// </para>
/// </summary>
internal sealed class RevocationRequestNotificationEnqueuer(
    FlitDbContext db,
    ITramiteNotificationRecipientResolver recipientResolver,
    ILogger<RevocationRequestNotificationEnqueuer> logger) : IRevocationRequestNotifier
{
    public const string EventoTipo = "revocatoria_solicitud_notificacion_encolada";

    /// <summary>Plantilla de "solicitud recibida" (HU #12579, catálogo en <c>NotificationTemplateCatalog</c>).</summary>
    public const string TemplateKey = "tramites.revocatoria-solicitada";

    /// <summary>HU #12576 (AC4) — evento propio de bitácora para la DECISIÓN (aprobada/rechazada).</summary>
    public const string DecisionEventoTipo = "revocatoria_decision_notificacion_encolada";

    /// <summary>Plantilla de "decisión: aprobada" (HU #12579, catálogo en <c>NotificationTemplateCatalog</c>).</summary>
    public const string DecisionTemplateKeyAprobada = "tramites.revocatoria-aprobada";

    /// <summary>Plantilla de "decisión: rechazada" (HU #12579, catálogo en <c>NotificationTemplateCatalog</c>).</summary>
    public const string DecisionTemplateKeyRechazada = "tramites.revocatoria-rechazada";

    private const string StatusPendiente = "pendiente";
    private const string StatusOmitido = "omitido";

    public async Task NotifyAsync(RevocationRequestSolicitadaEvent evt, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(evt);

        var instance = await db.ProcedureInstances
            .AsNoTracking()
            .Include(i => i.Actors)
            .Include(i => i.Participants)
            .FirstOrDefaultAsync(
                i => i.Id == evt.ProcedureInstanceId && i.TenantId == evt.TenantId,
                cancellationToken)
            .ConfigureAwait(false);

        if (instance is null)
        {
            RevocationNotificationLog.InstanceMissing(logger, evt.ProcedureInstanceId, evt.TenantId);
            return;
        }

        var resolution = await ResolveRadicadorAsync(instance, evt.RequestedByUserId, cancellationToken)
            .ConfigureAwait(false);

        var payload = JsonSerializer.Serialize(new
        {
            revocation_request_id = evt.RevocationRequestId,
            attempt_number = evt.AttemptNumber,
            template_key = TemplateKey,
            recipients = resolution.Recipients.Select(r => new { r.Role, kind = r.Kind.ToString(), r.Email }),
            gaps = resolution.Gaps.Select(g => new { g.Role, kind = g.Kind.ToString() }),
        });

        db.ProcedureInstanceEvents.Add(new ProcedureInstanceEvent
        {
            Id = Guid.NewGuid(),
            TenantId = evt.TenantId,
            ProcedureInstanceId = evt.ProcedureInstanceId,
            Tipo = EventoTipo,
            Payload = payload,
            CreatedAt = DateTimeOffset.UtcNow,
            CreatedBy = evt.RequestedByUserId,
        });

        // HU #12579 — cola real de correo, mismo hecho que el evento de bitácora de arriba.
        var rows = BuildDispatchRows(
            evt.TenantId, evt.ProcedureInstanceId, evt.RevocationRequestId, evt.AttemptNumber,
            RevocationRequestEmailMilestone.Solicitada, TemplateKey, resolution,
            decisionReason: null, evt.RequestedByUserId, logger);

        // SaveChanges PROPIO: corre DESPUÉS del commit de la solicitud, en su propia unidad de trabajo
        // (el caller ya envuelve esta llamada en try/catch — ver IRevocationRequestNotifier).
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await InsertIdempotentAsync(rows, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// HU #12576 (Feature #12565, AC4) — sink de "decisión de revocatoria registrada". MISMO patrón que
    /// <see cref="NotifyAsync"/> (radicador de la solicitud, no comprador/vendedor: es un acuse de la
    /// decisión a quien la pidió).
    /// </summary>
    public async Task NotifyDecisionAsync(RevocationRequestDecidedEvent evt, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(evt);

        var instance = await db.ProcedureInstances
            .AsNoTracking()
            .Include(i => i.Actors)
            .Include(i => i.Participants)
            .FirstOrDefaultAsync(
                i => i.Id == evt.ProcedureInstanceId && i.TenantId == evt.TenantId,
                cancellationToken)
            .ConfigureAwait(false);

        if (instance is null)
        {
            RevocationNotificationLog.InstanceMissing(logger, evt.ProcedureInstanceId, evt.TenantId);
            return;
        }

        // Solo el radicador de la SOLICITUD (no de la decisión): el acuse de "tu solicitud fue
        // aprobada/rechazada" va a quien la pidió, no a quien decidió (el propio OT).
        var resolution = await ResolveRadicadorAsync(instance, evt.RequestedByUserId, cancellationToken)
            .ConfigureAwait(false);

        var templateKey = evt.Approved ? DecisionTemplateKeyAprobada : DecisionTemplateKeyRechazada;
        var payload = JsonSerializer.Serialize(new
        {
            revocation_request_id = evt.RevocationRequestId,
            attempt_number = evt.AttemptNumber,
            approved = evt.Approved,
            decided_by = evt.DecidedBy,
            decided_at = evt.DecidedAt,
            template_key = templateKey,
            recipients = resolution.Recipients.Select(r => new { r.Role, kind = r.Kind.ToString(), r.Email }),
            gaps = resolution.Gaps.Select(g => new { g.Role, kind = g.Kind.ToString() }),
        });

        db.ProcedureInstanceEvents.Add(new ProcedureInstanceEvent
        {
            Id = Guid.NewGuid(),
            TenantId = evt.TenantId,
            ProcedureInstanceId = evt.ProcedureInstanceId,
            Tipo = DecisionEventoTipo,
            Payload = payload,
            CreatedAt = DateTimeOffset.UtcNow,
            CreatedBy = evt.DecidedBy,
        });

        // HU #12579 — motivo de rechazo denormalizado en la fila de despacho (solo milestone
        // rechazada) para que el worker no tenga que releer al padre al enviar. La decisión ya está
        // persistida en tramites.procedure_revocation_requests: DecideRevocationRequestHandler hace
        // SaveChanges de la decisión ANTES de invocar este notifier (best-effort, ver su XML doc).
        string? decisionReason = null;
        if (!evt.Approved)
        {
            decisionReason = await db.ProcedureRevocationRequests
                .AsNoTracking()
                .Where(r => r.Id == evt.RevocationRequestId)
                .Select(r => r.DecisionReason)
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);
        }

        var milestone = evt.Approved
            ? RevocationRequestEmailMilestone.Aprobada
            : RevocationRequestEmailMilestone.Rechazada;
        var rows = BuildDispatchRows(
            evt.TenantId, evt.ProcedureInstanceId, evt.RevocationRequestId, evt.AttemptNumber,
            milestone, templateKey, resolution, decisionReason, evt.DecidedBy, logger);

        // SaveChanges PROPIO — mismo criterio que NotifyAsync (el caller envuelve en try/catch).
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await InsertIdempotentAsync(rows, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Resuelve el cupo <c>radicador</c> (persona identificada por <paramref name="requestedByUserId"/>)
    /// vía <see cref="ITramiteNotificationRecipientResolver"/>, con la policy que excluye
    /// comprador/vendedor — idéntica en los 3 hitos (solicitada/aprobada/rechazada): el destinatario
    /// de este sub-flujo es siempre quien radicó la solicitud original, nunca quien decide.
    /// </summary>
    private async Task<TramiteRecipientResolution> ResolveRadicadorAsync(
        ProcedureInstance instance, Guid requestedByUserId, CancellationToken cancellationToken)
    {
        var requester = await db.Users
            .AsNoTracking()
            .Where(u => u.Id == requestedByUserId)
            .Select(u => new { u.Email, u.DisplayName })
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        var policy = new TramiteStateEmailRecipientPolicy(
            Comprador: false, VendedorOPropietario: false, Radicador: true, ExtraEmail: null);

        TramiteEmailRecipient? radicador = null;
        if (requester is not null && !string.IsNullOrWhiteSpace(requester.Email))
        {
            var email = requester.Email.Trim();
            radicador = new TramiteEmailRecipient(
                TramiteNotificationRecipientResolver.RoleRadicador,
                TramiteRecipientKind.Persona,
                email,
                string.IsNullOrWhiteSpace(requester.DisplayName) ? email : requester.DisplayName.Trim());
        }

        var actors = instance.Actors?.ToList() ?? [];
        var participants = instance.Participants?.ToList() ?? [];
        return recipientResolver.Resolve(instance, actors, participants, policy, radicador);
    }

    /// <summary>
    /// Filas de <c>tramites.revocation_request_email_dispatches</c> para un hito: una por
    /// destinatario resuelto + una por cupo omitido. Idempotencia por
    /// <c>(revocation_request_id, milestone, destinatario)</c> — ver DDL 116.
    /// </summary>
    internal static IReadOnlyList<RevocationRequestEmailDispatch> BuildDispatchRows(
        Guid tenantId,
        Guid procedureInstanceId,
        Guid revocationRequestId,
        int attemptNumber,
        string milestone,
        string templateKey,
        TramiteRecipientResolution resolution,
        string? decisionReason,
        Guid? createdBy,
        ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(logger);

        var now = DateTimeOffset.UtcNow;
        var rows = new List<RevocationRequestEmailDispatch>();
        var seenEmails = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var recipient in resolution.Recipients)
        {
            var normalized = recipient.Email.Trim();
            var kindDb = PlateAssignmentEmailEnqueuer.KindToDb(recipient.Kind);
            if (!seenEmails.Add(normalized))
            {
                continue;
            }

            rows.Add(new RevocationRequestEmailDispatch
            {
                Id = Guid.CreateVersion7(),
                TenantId = tenantId,
                ProcedureInstanceId = procedureInstanceId,
                RevocationRequestId = revocationRequestId,
                AttemptNumber = attemptNumber,
                Milestone = milestone,
                Recipient = normalized,
                RecipientName = Truncate(recipient.DisplayName, 200),
                RecipientRole = recipient.Role,
                RecipientKind = kindDb,
                TemplateKey = templateKey,
                Status = StatusPendiente,
                FailureReason = null,
                DecisionReason = Truncate(decisionReason, 500),
                Attempts = 0,
                QueuedAt = now,
                CreatedAt = now,
                CreatedBy = createdBy,
            });
        }

        foreach (var gap in resolution.Gaps)
        {
            rows.Add(new RevocationRequestEmailDispatch
            {
                Id = Guid.CreateVersion7(),
                TenantId = tenantId,
                ProcedureInstanceId = procedureInstanceId,
                RevocationRequestId = revocationRequestId,
                AttemptNumber = attemptNumber,
                Milestone = milestone,
                Recipient = null,
                RecipientName = Truncate(gap.DisplayName, 200),
                RecipientRole = gap.Role,
                RecipientKind = PlateAssignmentEmailEnqueuer.KindToDb(gap.Kind),
                TemplateKey = templateKey,
                Status = StatusOmitido,
                FailureReason = PlateAssignmentEmailEnqueuer.GapReason(gap.Kind),
                DecisionReason = Truncate(decisionReason, 500),
                Attempts = 0,
                QueuedAt = now,
                ProcessedAt = now,
                CreatedAt = now,
                CreatedBy = createdBy,
            });
        }

        return rows;
    }

    private async Task InsertIdempotentAsync(
        IReadOnlyList<RevocationRequestEmailDispatch> rows,
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
                db.RevocationRequestEmailDispatches.Add(row);
            }

            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            return;
        }

        foreach (var row in rows)
        {
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"""
                 INSERT INTO tramites.revocation_request_email_dispatches
                     (id, tenant_id, procedure_instance_id, revocation_request_id, attempt_number,
                      milestone, recipient, recipient_name, recipient_role, recipient_kind,
                      template_key, status, failure_reason, decision_reason, attempts, queued_at,
                      processed_at, created_at, created_by)
                 VALUES
                     ({row.Id}, {row.TenantId}, {row.ProcedureInstanceId}, {row.RevocationRequestId},
                      {row.AttemptNumber}, {row.Milestone}, {row.Recipient}, {row.RecipientName},
                      {row.RecipientRole}, {row.RecipientKind}, {row.TemplateKey}, {row.Status},
                      {row.FailureReason}, {row.DecisionReason}, {row.Attempts}, {row.QueuedAt},
                      {row.ProcessedAt}, {row.CreatedAt}, {row.CreatedBy})
                 ON CONFLICT DO NOTHING
                 """,
                ct).ConfigureAwait(false);
        }
    }

    private async Task<bool> ExistsInMemoryAsync(RevocationRequestEmailDispatch row, CancellationToken ct)
    {
        if (row.Recipient is null)
        {
            return await db.RevocationRequestEmailDispatches
                .AnyAsync(
                    d => d.RevocationRequestId == row.RevocationRequestId
                         && d.Milestone == row.Milestone
                         && d.Recipient == null
                         && d.RecipientRole == row.RecipientRole
                         && d.RecipientKind == row.RecipientKind,
                    ct)
                .ConfigureAwait(false);
        }

        var needle = row.Recipient.ToLowerInvariant();
        return await db.RevocationRequestEmailDispatches
            .AnyAsync(
                d => d.RevocationRequestId == row.RevocationRequestId
                     && d.Milestone == row.Milestone
                     && d.Recipient != null
                     && d.Recipient.ToLower() == needle,
                ct)
            .ConfigureAwait(false);
    }

    private static string? Truncate(string? value, int max)
    {
        if (string.IsNullOrEmpty(value))
            return value;
        return value.Length <= max ? value : value[..max];
    }
}

internal static partial class RevocationNotificationLog
{
    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Sink revocatoria: instancia {ProcedureInstanceId} (tenant {TenantId}) no encontrada; no se encola notificación.")]
    public static partial void InstanceMissing(ILogger logger, Guid procedureInstanceId, Guid tenantId);
}
