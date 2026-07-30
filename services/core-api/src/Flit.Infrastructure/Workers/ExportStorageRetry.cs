namespace Flit.Infrastructure.Workers;

/// <summary>Reintentos con backoff ante fallos del file-manager (HU #11107 / AC4).</summary>
internal static class ExportStorageRetry
{
    public const int MaxAttempts = 3;

    public static async Task<T> ExecuteAsync<T>(
        Func<CancellationToken, Task<T>> action,
        Func<int, TimeSpan>? delayFactory = null,
        CancellationToken ct = default)
    {
        delayFactory ??= attempt => TimeSpan.FromSeconds(Math.Pow(2, attempt)); // 2s, 4s, 8s
        Exception? last = null;
        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                return await action(ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (attempt < MaxAttempts)
            {
                last = ex;
                await Task.Delay(delayFactory(attempt), ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                last = ex;
            }
        }

        throw last ?? new InvalidOperationException("Export storage retry exhausted.");
    }
}
