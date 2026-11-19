namespace Flit.Tramites.Domain.Entities;

/// <summary>
/// Bug #14604 — un adjunto <see cref="ProcedureInstanceAttachment.IsHistorico"/> es una versión anterior: se muestra y se
/// descarga, pero NO cuenta como «el documento del tipo X» para el consolidado, el FUR, la impronta ni la vista del OT
/// cuando existe uno vigente del mismo tipo. Todo consumidor que ELIGE «el adjunto de tipo X» lee por aquí.
/// <para>Regla («efectivos»): los no históricos, más los históricos cuyo tipo no tiene ningún vigente. La segunda parte
/// conserva el comportamiento de la revocatoria (HU #12166), que marca TODOS los adjuntos como históricos sin
/// reemplazarlos: ahí el expediente sigue siendo el que se tuvo a la vista, no queda vacío. Las comprobaciones de
/// existencia («¿hay un adjunto de tipo X?») no necesitan filtrar: la carga administrativa siempre deja un vigente.</para>
/// </summary>
/// <remarks>Uso de ejemplo: <c>var soat = instance.AdjuntosEfectivos().FirstOrDefault(a =&gt; a.Tipo == "soat");</c></remarks>
public static class ProcedureInstanceAttachmentExtensions
{
    /// <summary>Adjuntos efectivos de la lista: vigentes, más los históricos que ningún vigente reemplaza.</summary>
    public static IEnumerable<ProcedureInstanceAttachment> Efectivos(this IEnumerable<ProcedureInstanceAttachment> adjuntos)
    {
        ArgumentNullException.ThrowIfNull(adjuntos);
        var lista = adjuntos as IReadOnlyCollection<ProcedureInstanceAttachment> ?? adjuntos.ToList();
        if (!lista.Any(a => a.IsHistorico))
            return lista;

        var tiposConVigente = lista
            .Where(a => !a.IsHistorico)
            .Select(a => a.Tipo)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return lista.Where(a => !a.IsHistorico || !tiposConVigente.Contains(a.Tipo)).ToList();
    }

    /// <summary>Adjuntos efectivos de la instancia (ver <see cref="Efectivos"/>).</summary>
    public static IEnumerable<ProcedureInstanceAttachment> AdjuntosEfectivos(this ProcedureInstance instance)
    {
        ArgumentNullException.ThrowIfNull(instance);
        return instance.Attachments.Efectivos();
    }
}
