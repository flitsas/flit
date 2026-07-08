using System.Threading.Channels;

namespace Flit.Infrastructure.Telemetry;

/// <summary>
/// Implementación de <see cref="IUsageEventQueue"/> sobre <see cref="Channel"/> bounded
/// (capacidad 10 000, <see cref="BoundedChannelFullMode.DropWrite"/>): si la cola se llena,
/// los eventos nuevos se descartan sin bloquear el request (la telemetría es best-effort).
/// Singleton; el lado lector lo consume <see cref="UsageEventWriterProcessor"/>.
/// </summary>
public sealed class ChannelUsageEventQueue : IUsageEventQueue
{
    internal const int Capacity = 10_000;

    private readonly Channel<UsageEventRecord> _channel = Channel.CreateBounded<UsageEventRecord>(
        new BoundedChannelOptions(Capacity)
        {
            FullMode = BoundedChannelFullMode.DropWrite,
            SingleReader = true,
            SingleWriter = false,
        });

    public bool TryEnqueue(UsageEventRecord evt) => evt is not null && _channel.Writer.TryWrite(evt);

    /// <summary>Espera (cancelable) a que haya al menos un evento por leer.</summary>
    internal ValueTask<bool> WaitToReadAsync(CancellationToken ct) => _channel.Reader.WaitToReadAsync(ct);

    /// <summary>Extrae el siguiente evento pendiente, si lo hay (lado writer).</summary>
    internal bool TryDequeue(out UsageEventRecord evt)
    {
        if (_channel.Reader.TryRead(out var read))
        {
            evt = read;
            return true;
        }

        evt = null!;
        return false;
    }
}
