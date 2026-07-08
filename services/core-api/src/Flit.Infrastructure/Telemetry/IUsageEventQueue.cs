namespace Flit.Infrastructure.Telemetry;

/// <summary>
/// Cola en memoria de eventos de telemetría (HU-A Reportes 2.0). Productores: middleware
/// <c>UsageTelemetryMiddleware</c> y endpoint batch <c>POST /analytics/events</c>.
/// Consumidor: <see cref="UsageEventWriterProcessor"/>. La telemetría NUNCA bloquea ni
/// lanza al caller: <see cref="TryEnqueue"/> devuelve <c>false</c> si la cola está llena
/// (bounded 10 000, DropWrite) y el evento simplemente se pierde.
/// </summary>
public interface IUsageEventQueue
{
    bool TryEnqueue(UsageEventRecord evt);
}
