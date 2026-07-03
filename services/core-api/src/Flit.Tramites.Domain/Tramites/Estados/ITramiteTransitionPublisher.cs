namespace Flit.Tramites.Domain.Tramites.Estados;

/// <summary>
/// Puerto de PUBLICACIÓN (HU-3, RNF01): encola el cambio de estado para notificarlo a las
/// integraciones OT (<c>IProcedureStateChangeNotifier</c> → webhooks) con entrega consistente.
/// ENCOLA en la MISMA unidad de trabajo (patrón outbox, como <c>identity_validation_outbox</c>)
/// SIN hacer SaveChanges; la entrega efectiva ocurre tras el commit (procesador en background).
/// Garantía: una transición confirmada notifica exactamente una vez; una transición que falla
/// (rollback) no notifica.
/// </summary>
public interface ITramiteTransitionPublisher
{
    Task EnqueueAsync(TramiteTransitionRecord record, CancellationToken ct = default);
}

/// <summary>No-op para compilar/testear HU-1 de forma aislada; la integración registra la real.</summary>
public sealed class NullTramiteTransitionPublisher : ITramiteTransitionPublisher
{
    public Task EnqueueAsync(TramiteTransitionRecord record, CancellationToken ct = default) =>
        Task.CompletedTask;
}
