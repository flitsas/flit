namespace Flit.Tramites.Application.UseCases.ConsolidadoLotes;

/// <summary>
/// HU #13377 (Épica #13216, ADR-0070, diseño §3 «Asignación de partes», CF-09/CF-10) — reparto puro de los ítems
/// cerrados de un lote en partes de como mucho <c>N</c> PDF y <c>M</c> MB de PDF en claro. Sin E/S: el llamador
/// (<c>ConsolidadoLotePartesAsignacion</c>, en la transacción con el lock del lote) lee los ítems sin parte, llama
/// aquí y persiste el plan.
/// <list type="bullet">
///   <item><b>Orden</b>: incluidos por <see cref="ItemSinParte.ProcesadoEn"/> y luego por
///   <see cref="ItemSinParte.Posicion"/>; omitidos por posición.</item>
///   <item><b>Avaro N/M</b>: si sumar el siguiente PDF superaría M y la parte en curso no está vacía, la parte se
///   cierra sin él. Una parte también se cierra al llegar a N PDF o a M bytes. Un PDF mayor que M va solo
///   (AC2). Así N+1 PDF dan exactamente 2 partes (AC1).</item>
///   <item><b>Omitidos</b>: todos los omitidos sin parte van en la primera parte que sale del reparto, cada parte con
///   sus propios omitidos (v2.1; su <c>omitidos.csv</c>).</item>
///   <item><see cref="ModoAsignacion.Parcial"/> (cierre de un ítem): solo salen las partes llenas; el resto espera.
///   <see cref="ModoAsignacion.Final"/> (fin del carril de ítems): sale también la parte en curso. Si en el modo
///   final no sale ningún PDF pero quedan omitidos sin parte, o el lote no tiene ninguna parte, sale una parte solo
///   con <c>omitidos.csv</c> (AC3; también el lote de selección vacía, que queda con una parte sin filas).</item>
/// </list>
/// Repartir en tandas (parcial tras cada PDF + final) da el mismo resultado que repartir todo de una vez, porque la
/// parte en curso nunca se cierra antes de tiempo.
/// </summary>
/// <remarks>
/// Uso de ejemplo:
/// <code>
/// var plan = AsignadorDePartes.Planear(sinParte, maxPdfs: 500, maxBytes: AsignadorDePartes.MbABytes(250),
///     ModoAsignacion.Final, loteSinPartes: true);
/// // plan[0].Pdfs, plan[0].Omitidos → parte (partes existentes + 1)
/// </code>
/// </remarks>
public static class AsignadorDePartes
{
    /// <summary>Bytes por MB de <c>max_mb_per_part</c> (MiB).</summary>
    public const long BytesPorMb = 1024L * 1024L;

    /// <summary>M en bytes a partir de <c>max_mb_per_part</c>.</summary>
    public static long MbABytes(int maxMbPorParte)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxMbPorParte, 1);
        return maxMbPorParte * BytesPorMb;
    }

    /// <summary>
    /// Planea las partes nuevas. No numera: la parte <c>k</c> del plan es la <c>partes existentes + k + 1</c> del lote.
    /// </summary>
    /// <param name="sinParte">Ítems <c>incluido</c> u <c>omitido</c> del lote que aún no tienen parte.</param>
    /// <param name="maxPdfs">N (<c>max_pdfs_per_part</c>), al menos 1.</param>
    /// <param name="maxBytes">M en bytes, al menos 1.</param>
    /// <param name="modo">Parcial (cierre de ítem) o final (fin del carril).</param>
    /// <param name="loteSinPartes">El lote aún no tiene ninguna parte (decide la parte solo con CSV en el final).</param>
    public static IReadOnlyList<PartePlaneada> Planear(
        IReadOnlyList<ItemSinParte> sinParte, int maxPdfs, long maxBytes, ModoAsignacion modo, bool loteSinPartes)
    {
        ArgumentNullException.ThrowIfNull(sinParte);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxPdfs, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxBytes, 1L);

        var incluidos = sinParte.Where(i => i.Incluido)
            .OrderBy(i => i.ProcesadoEn).ThenBy(i => i.Posicion).ThenBy(i => i.Id)
            .ToList();
        var omitidos = sinParte.Where(i => !i.Incluido)
            .OrderBy(i => i.Posicion).ThenBy(i => i.Id)
            .Select(i => i.Id)
            .ToList();

        var llenas = new List<List<ItemSinParte>>();
        var enCurso = new List<ItemSinParte>();
        long bytesEnCurso = 0;
        foreach (var pdf in incluidos)
        {
            var tamano = Math.Max(0, pdf.SizeBytes);
            if (enCurso.Count > 0 && bytesEnCurso + tamano > maxBytes)
            {
                llenas.Add(enCurso);
                (enCurso, bytesEnCurso) = ([], 0);
            }

            enCurso.Add(pdf);
            bytesEnCurso += tamano;
            if (enCurso.Count >= maxPdfs || bytesEnCurso >= maxBytes)
            {
                llenas.Add(enCurso);
                (enCurso, bytesEnCurso) = ([], 0);
            }
        }

        if (modo == ModoAsignacion.Final && enCurso.Count > 0)
            llenas.Add(enCurso);

        var plan = new List<PartePlaneada>(llenas.Count + 1);
        for (var k = 0; k < llenas.Count; k++)
        {
            var pdfs = llenas[k];
            plan.Add(new PartePlaneada(
                [.. pdfs.Select(p => p.Id)],
                k == 0 ? omitidos : [],
                pdfs.Sum(p => Math.Max(0, p.SizeBytes))));
        }

        // AC3: en el final, los omitidos que no encontraron parte (o el lote sin ninguna parte) van en una parte solo
        // con omitidos.csv.
        if (plan.Count == 0 && modo == ModoAsignacion.Final && (omitidos.Count > 0 || loteSinPartes))
            plan.Add(new PartePlaneada([], omitidos, 0));

        return plan;
    }
}

/// <summary>Cuándo se reparte: tras cerrar un ítem (solo partes llenas) o al terminar el carril (todo).</summary>
public enum ModoAsignacion
{
    Parcial,
    Final,
}

/// <summary>Ítem cerrado de un lote sin parte asignada.</summary>
/// <param name="Id">Id del ítem.</param>
/// <param name="Incluido"><c>true</c> si su PDF entra en el ZIP; <c>false</c> si es omitido (fila del CSV).</param>
/// <param name="SizeBytes">Tamaño del PDF en claro (snapshot); 0 en omitidos.</param>
/// <param name="ProcesadoEn">Cierre del ítem (<c>processed_at</c>).</param>
/// <param name="Posicion">Orden en la selección (<c>position</c>), desempate estable.</param>
public sealed record ItemSinParte(Guid Id, bool Incluido, long SizeBytes, DateTimeOffset ProcesadoEn, int Posicion);

/// <summary>Una parte nueva del plan: sus PDF (en orden) y sus omitidos (en orden de posición).</summary>
/// <param name="Pdfs">Ids de los ítems incluidos.</param>
/// <param name="Omitidos">Ids de los ítems omitidos (filas de su <c>omitidos.csv</c>).</param>
/// <param name="Bytes">Suma del tamaño en claro de sus PDF.</param>
public sealed record PartePlaneada(IReadOnlyList<Guid> Pdfs, IReadOnlyList<Guid> Omitidos, long Bytes);
