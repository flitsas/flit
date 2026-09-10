namespace Flit.Tramites.Domain.Integration;

/// <summary>
/// Notificación de cambio de estado de trámite hacia integraciones OT (HU #10216).
/// N 03 (RF05/RNF01): incluye el motivo de la transición y el usuario que la ejecutó
/// (opcionales con default para no romper a los emisores existentes).
/// <para>
/// <see cref="OutboxId"/> (HU #11465 / ADR-0045): id de la fila en
/// <c>tramites.procedure_state_change_outbox</c>. Obligatorio para el sink de correo
/// (idempotencia); los sinks OT/ICT lo ignoran.
/// </para>
/// </summary>
public sealed record ProcedureStateChangeEvent(
    Guid TenantId,
    Guid ProcedureInstanceId,
    string? FromStatus,
    string ToStatus,
    DateTimeOffset ChangedAt,
    string? Reason = null,
    Guid? ChangedByUserId = null,
    Guid OutboxId = default);

/// <summary>Puerto para notificar cambios de estado sin acoplar trámites al módulo Admin.</summary>
public interface IProcedureStateChangeNotifier
{
    Task NotifyAsync(ProcedureStateChangeEvent change, CancellationToken cancellationToken = default);
}

/// <summary>Implementación nula cuando no hay integración webhook registrada.</summary>
public sealed class NullProcedureStateChangeNotifier : IProcedureStateChangeNotifier
{
    public static NullProcedureStateChangeNotifier Instance { get; } = new();

    public Task NotifyAsync(ProcedureStateChangeEvent change, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;
}
