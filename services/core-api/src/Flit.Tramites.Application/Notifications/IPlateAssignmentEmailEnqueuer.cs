namespace Flit.Tramites.Application.Notifications;

/// <summary>
/// HU #11485 (Feature #11482, ADR-0046) — encola filas en
/// <c>tramites.plate_assignment_email_dispatches</c> tras asignar placa (Flujo B).
/// Sin I/O de red; idempotencia en índices UNIQUE de la base.
/// </summary>
public interface IPlateAssignmentEmailEnqueuer
{
    /// <summary>
    /// Resuelve destinatarios del rol comprador e inserta despachos pendientes u omitidos.
    /// </summary>
    /// <param name="clientTenantId">
    /// Tenant cliente dueño del trámite y de la política de canal — nunca el tenant del OT.
    /// </param>
    Task EnqueueAsync(
        Guid clientTenantId,
        Guid procedureInstanceId,
        string plate,
        CancellationToken cancellationToken = default);
}
