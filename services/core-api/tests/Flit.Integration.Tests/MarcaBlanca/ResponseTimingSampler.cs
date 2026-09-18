using System.Diagnostics;

namespace Flit.Integration.Tests.MarcaBlanca;

/// <summary>
/// HU #12429 AC2/AC3 — mediana de N muestras tras un calentamiento, para comparar el costo de dos
/// caminos anti-enumeración sin falsos positivos por el ruido normal de una VM de CI (GC, JIT,
/// primera conexión del pool). Tolerancia elegida (ver <see cref="ToleranceMs"/>): FIJA en 200 ms —
/// generosa frente al ruido observado en muestras locales (variación ≤ 40 ms entre casos que SÍ
/// comparten camino), pero suficientemente estricta para detectar un atajo real (p. ej. saltarse el
/// hash Argon2 del señuelo, que por sí solo cuesta un orden de magnitud más que la diferencia
/// tolerada).
/// </summary>
internal static class ResponseTimingSampler
{
    public const int Samples = 12;
    public const int WarmupSamples = 3;
    public const double ToleranceMs = 200;

    /// <summary>Mediana en milisegundos de <see cref="Samples"/> ejecuciones de <paramref name="action"/>, tras <see cref="WarmupSamples"/> de calentamiento descartadas.</summary>
    public static async Task<double> MedianMsAsync(Func<Task> action)
    {
        for (var i = 0; i < WarmupSamples; i++)
        {
            await action().ConfigureAwait(false);
        }

        var elapsed = new List<double>(Samples);
        for (var i = 0; i < Samples; i++)
        {
            var sw = Stopwatch.StartNew();
            await action().ConfigureAwait(false);
            sw.Stop();
            elapsed.Add(sw.Elapsed.TotalMilliseconds);
        }

        elapsed.Sort();
        var mid = elapsed.Count / 2;
        return elapsed.Count % 2 == 0 ? (elapsed[mid - 1] + elapsed[mid]) / 2 : elapsed[mid];
    }
}
