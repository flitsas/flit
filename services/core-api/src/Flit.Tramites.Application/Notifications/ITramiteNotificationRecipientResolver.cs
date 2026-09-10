using Flit.Tramites.Domain.Entities;

namespace Flit.Tramites.Application.Notifications;

/// <summary>
/// HU #11462 — resuelve a quién se le escribe el aviso de cambio de estado del trámite
/// (ADR-0045): cupos por tipo de persona, no por rol.
/// </summary>
public interface ITramiteNotificationRecipientResolver
{
    TramiteRecipientResolution Resolve(
        ProcedureInstance instance,
        IReadOnlyList<ProcedureInstanceActor> actors,
        IReadOnlyList<ProcedureInstanceParticipant> participants,
        TramiteStateEmailRecipientPolicy? policy = null,
        TramiteEmailRecipient? radicador = null);
}
