using Flit.Tramites.Domain.Tramites.Estados;

namespace Flit.Tramites.Application.Tests.UseCases.ProcedureInstances.Estados;

/// <summary>
/// Fake del puerto de historial (HU-2): captura los registros que el lifecycle service encola,
/// para asertar exactamente qué transiciones se registraron y con qué autoría/motivo.
/// </summary>
internal sealed class RecordingTransitionRecorder : ITramiteTransitionRecorder
{
    public List<TramiteTransitionRecord> Records { get; } = [];

    public Task RecordAsync(TramiteTransitionRecord record, CancellationToken ct = default)
    {
        Records.Add(record);
        return Task.CompletedTask;
    }
}

/// <summary>Fake del puerto de publicación (HU-3): captura las notificaciones encoladas.</summary>
internal sealed class RecordingTransitionPublisher : ITramiteTransitionPublisher
{
    public List<TramiteTransitionRecord> Published { get; } = [];

    public Task EnqueueAsync(TramiteTransitionRecord record, CancellationToken ct = default)
    {
        Published.Add(record);
        return Task.CompletedTask;
    }
}
