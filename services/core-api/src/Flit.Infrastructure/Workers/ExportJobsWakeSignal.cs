using System.Threading.Channels;

namespace Flit.Infrastructure.Workers;

/// <summary>
/// Canal interno NOTIFY → Worker (Feature #11076 / HU #11107 / ADR-0037).
/// El listener escribe al recibir NOTIFY; el worker espera con timeout 30 s (fallback polling).
/// </summary>
internal static class ExportJobsWakeSignal
{
    private static readonly Channel<bool> Channel =
        System.Threading.Channels.Channel.CreateUnbounded<bool>(new UnboundedChannelOptions
        {
            SingleReader = false,
            SingleWriter = false,
            AllowSynchronousContinuations = false,
        });

    public static void Signal() => Channel.Writer.TryWrite(true);

    public static async Task WaitAsync(TimeSpan timeout, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);
        try
        {
            _ = await Channel.Reader.ReadAsync(cts.Token).ConfigureAwait(false);
            // Drena señales acumuladas para no procesar en ráfaga innecesaria.
            while (Channel.Reader.TryRead(out _)) { }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            // Timeout de polling — AC2.
        }
    }
}
