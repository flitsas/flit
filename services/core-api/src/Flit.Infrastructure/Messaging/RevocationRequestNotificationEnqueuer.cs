using System.Text.Json;
using Flit.Infrastructure.Persistence;
using Flit.Tramites.Application.Notifications;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Integration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Flit.Infrastructure.Messaging;

/// <summary>
/// HU #12572 (Feature #12565, AC3) — sink de "solicitud de revocatoria recibida". Resuelve el cupo
/// <c>radicador</c> (quien envió LA SOLICITUD, no comprador/vendedor del trámite: es un acuse de
/// recibo, no un aviso de cambio de estado) vía <see cref="ITramiteNotificationRecipientResolver"/>
/// SIN modificarlo, y deja constancia del encolado como evento PROPIO de bitácora
/// (<see cref="ProcedureInstanceEvent"/>, Tipo <see cref="EventoTipo"/>) — mismo mecanismo que ya usa
/// <c>AdminAnularHandler</c> para eventos que no son <c>cambio_estado</c>.
///
/// <para>
/// <b>Por qué NO hay una cola de despacho de correo propia todavía (a diferencia de
/// <c>PlateAssignmentEmailEnqueuer</c>, ADR-0046):</b> AC3 de HU #12572 solo exige "se encola la
/// notificación", verificable con un mock de <see cref="IRevocationRequestNotifier"/>. La plantilla
/// (<c>tramites.revocatoria-solicitada</c>), el composer, la tabla de despacho con reintentos y el
/// <c>BackgroundService</c> que de verdad envía el correo son un punto de enganche NUEVO,
/// deliberadamente fuera del alcance de esta HU (ver Contexto de la HU: "solo agrega el punto de
/// enganche nuevo"). Cuando esa plantilla exista, este sink se reemplaza por uno que escriba en una
/// cola de despacho propia —mismo patrón de <c>PlateAssignmentEmailEnqueuer</c>— sin tocar
/// <see cref="IRevocationRequestNotifier"/> ni al llamador (<c>RequestRevocationHandler</c>).
/// </para>
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

    /// <summary>Placeholder del template key hasta que exista la plantilla real (ver XML doc de la clase).</summary>
    public const string TemplateKey = "tramites.revocatoria-solicitada";

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

        var requester = await db.Users
            .AsNoTracking()
            .Where(u => u.Id == evt.RequestedByUserId)
            .Select(u => new { u.Email, u.DisplayName })
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        // Solo el radicador — "solicitud recibida" es un acuse de recibo a quien la envió, no un
        // aviso de cambio de estado del trámite (comprador/vendedor quedan fuera a propósito).
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
        var resolution = recipientResolver.Resolve(instance, actors, participants, policy, radicador);

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

        // SaveChanges PROPIO: corre DESPUÉS del commit de la solicitud, en su propia unidad de trabajo
        // (el caller ya envuelve esta llamada en try/catch — ver IRevocationRequestNotifier).
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}

internal static partial class RevocationNotificationLog
{
    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Sink solicitud revocatoria: instancia {ProcedureInstanceId} (tenant {TenantId}) no encontrada; no se encola notificación.")]
    public static partial void InstanceMissing(ILogger logger, Guid procedureInstanceId, Guid tenantId);
}
