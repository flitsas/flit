using Flit.Tramites.Application.Storage;
using Flit.Tramites.Domain.Entities;

namespace Flit.Tramites.Application.UseCases.ProcedureInstances;

/// <summary>
/// Bug #14631 — descarga en paralelo los adjuntos que se fusionan en un consolidado (wizard y maestro).
///
/// <para><b>El problema.</b> Ambos consolidados bajaban los adjuntos UNO POR UNO dentro de la misma
/// petición HTTP. En PDN cada descarga son dos idas y vueltas (URL firmada en el file-manager + GET al
/// almacenamiento en Europa) y en la franja lenta del almacenamiento cada una tardaba 2-10 s: un
/// expediente de 10-12 documentos pasaba de 60-150 s, el gateway cortaba a 150 s, el usuario recargaba
/// antes y el trámite quedaba en Preparado.</para>
///
/// <para><b>Qué cambia y qué no.</b> Solo la red va en paralelo, con un tope de
/// <see cref="MaxParalelo"/> descargas simultáneas para no saturar el file-manager ni el almacenamiento.
/// El resultado conserva el orden de entrada y la normalización/estampado posterior sigue siendo
/// secuencial (usa el <c>DbContext</c>, que no admite concurrencia). Un adjunto que no existe vuelve como
/// <c>null</c> en su posición: el llamador responde <c>adjunto_no_disponible</c> igual que antes. Una
/// excepción del almacenamiento se propaga igual que en el bucle secuencial (la primera en orden).</para>
/// </summary>
internal static class ConsolidadoDescargaAdjuntos
{
    /// <summary>Descargas simultáneas por consolidado.</summary>
    internal const int MaxParalelo = 6;

    /// <summary>
    /// Bytes de cada adjunto en el MISMO orden de <paramref name="adjuntos"/>; <c>null</c> donde el
    /// almacenamiento no encontró el archivo.
    /// </summary>
    public static async Task<byte[]?[]> DescargarAsync(
        IAttachmentStorage storage,
        IReadOnlyList<ProcedureInstanceAttachment> adjuntos,
        CancellationToken ct,
        int maxParalelo = MaxParalelo)
    {
        ArgumentNullException.ThrowIfNull(storage);
        ArgumentNullException.ThrowIfNull(adjuntos);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxParalelo, 1);

        var resultado = new byte[]?[adjuntos.Count];
        if (adjuntos.Count == 0)
            return resultado;

        using var cupo = new SemaphoreSlim(maxParalelo, maxParalelo);
        var tareas = new Task[adjuntos.Count];
        for (var i = 0; i < adjuntos.Count; i++)
            tareas[i] = DescargarUnoAsync(storage, adjuntos[i].StoragePath, resultado, i, cupo, ct);

        // Se espera a TODAS antes de soltar el semáforo; si alguna falló, se relanza la primera en orden
        // (lo mismo que habría lanzado el bucle secuencial).
        try
        {
            await Task.WhenAll(tareas).ConfigureAwait(false);
        }
        catch
        {
            foreach (var t in tareas)
            {
                if (t.IsFaulted)
                    System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(t.Exception!.InnerException!).Throw();
            }

            throw;
        }

        return resultado;
    }

    private static async Task DescargarUnoAsync(
        IAttachmentStorage storage,
        string storagePath,
        byte[]?[] resultado,
        int indice,
        SemaphoreSlim cupo,
        CancellationToken ct)
    {
        await cupo.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await using var stream = await storage.OpenReadAsync(storagePath, ct).ConfigureAwait(false);
            if (stream is null)
                return;

            using var ms = new MemoryStream();
            await stream.CopyToAsync(ms, ct).ConfigureAwait(false);
            resultado[indice] = ms.ToArray();
        }
        finally
        {
            cupo.Release();
        }
    }
}
