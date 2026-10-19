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

    /// <summary>
    /// Mediana en milisegundos de <see cref="Samples"/> ejecuciones de cada una de <paramref name="actions"/>,
    /// tras <see cref="WarmupSamples"/> rondas de calentamiento descartadas. Las muestras se toman
    /// INTERCALADAS (A, B, C, A, B, C…): una racha de carga del runner cae sobre todos los caminos por
    /// igual en vez de inflar solo el que se medía en ese momento (#606: 527 ms contra 735 ms con
    /// mediciones secuenciales). Devuelve las medianas en el mismo orden de <paramref name="actions"/>.
    /// </summary>
    public static async Task<double[]> MediansMsAsync(params Func<Task>[] actions)
    {
        for (var i = 0; i < WarmupSamples; i++)
        {
            foreach (var action in actions)
            {
                await action().ConfigureAwait(false);
            }
        }

        var elapsed = actions.Select(_ => new List<double>(Samples)).ToArray();
        for (var i = 0; i < Samples; i++)
        {
            for (var a = 0; a < actions.Length; a++)
            {
                var sw = Stopwatch.StartNew();
                await actions[a]().ConfigureAwait(false);
                sw.Stop();
                elapsed[a].Add(sw.Elapsed.TotalMilliseconds);
            }
        }

        return elapsed.Select(Median).ToArray();
    }

    private static double Median(List<double> samples)
    {
        samples.Sort();
        var mid = samples.Count / 2;
        return samples.Count % 2 == 0 ? (samples[mid - 1] + samples[mid]) / 2 : samples[mid];
    }
}
