namespace Flit.Tramites.Domain.Integration;

/// <summary>Notificación de cambio de estado de trámite hacia integraciones OT (HU #10216).</summary>
public sealed record ProcedureStateChangeEvent(
    Guid TenantId,
    Guid ProcedureInstanceId,
    string? FromStatus,
    string ToStatus,
    DateTimeOffset ChangedAt);

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
