namespace Flit.Tramites.Domain.Documents;

/// <summary>
/// Precedencia declarada por grado de especificidad del origen de un adjunto (DT-4, Feature #11309,
/// <c>plan-tecnico-documentos-personalizados-compania.md</c> §5). Reemplaza el desempate "gana el más
/// reciente" —no determinista cuando dos filas del mismo tipo se escriben con el mismo <c>now</c>— por
/// una regla legible e independiente del orden en que se lean los adjuntos.
///
/// <para>Tres grados, de mayor a menor especificidad:</para>
/// <list type="number">
///   <item><b>Hecho del trámite</b> (<c>ot</c>, <c>user</c>, <c>ict</c>, <c>portal</c>, <c>ocr</c>,
///         <c>consultation</c>): alguien cargó o importó ese documento para ESTE expediente.</item>
///   <item><b>Configuración de la compañía</b> (<c>company</c>, y cualquier origen NO declarado):
///         vale para todos los trámites de la compañía. Un origen desconocido cae aquí a propósito
///         — nunca debe desplazar por accidente un hecho del trámite (<c>user</c>/<c>ot</c>/<c>ict</c>),
///         ni quedar por debajo de una plantilla genérica.</item>
///   <item><b>Plantilla del sistema</b> (<c>system</c>): el default genérico.</item>
/// </list>
///
/// Dentro del MISMO grado, gana el <c>UploadedAt</c> más reciente (comportamiento previo a este
/// Feature, ahora acotado a los empates reales dentro de un grado).
///
/// <para><b>Alcance de esta clase:</b> decide "quién gana" entre dos orígenes cualesquiera — es de
/// aplicación general. La decisión de A QUÉ TIPOS documentales se le aplica (solo <c>mandato</c> y
/// <c>tramite_virtual</c>, por decisión del PO del 2026-08-10) vive en el llamador
/// (<c>ConsolidadoCommand.SanitizeConsolidadoParts</c>), no aquí: la <c>compraventa</c> queda
/// deliberadamente excluida y conserva su criterio actual de "el más reciente gana", por
/// <c>ADR-0035-compraventa-autogenerada-siempre-y-sin-membrete</c>.</para>
///
/// <para>No toca base de datos ni el resto del dominio: es una función pura y se prueba como tal.</para>
/// </summary>
public static class AttachmentSourcePrecedence
{
    private static readonly HashSet<string> HechoDelTramite = new(StringComparer.OrdinalIgnoreCase)
    {
        "ot", "user", "ict", "portal", "ocr", "consultation",
    };

    private const string OrigenPlantilla = "system";

    /// <summary>Peso del grado: menor valor = mayor especificidad = gana.</summary>
    public static int Weight(string? source)
    {
        if (string.Equals(source, OrigenPlantilla, StringComparison.OrdinalIgnoreCase))
            return 3;

        return !string.IsNullOrEmpty(source) && HechoDelTramite.Contains(source)
            ? 1
            : 2; // "company" y cualquier origen no declarado — nunca ganan por accidente, nunca pierden ante la plantilla.
    }

    /// <summary>
    /// De varios candidatos del MISMO tipo documental, elige el que debe sobrevivir en el
    /// consolidado: primero por grado (más específico gana), y dentro del mismo grado, el más
    /// reciente. <see cref="Enumerable.OrderBy{TSource,TKey}(IEnumerable{TSource},Func{TSource,TKey})"/>
    /// es una ordenación estable, así que a igual grado e igual fecha el resultado es siempre el
    /// mismo elemento de <paramref name="candidatos"/> con independencia del orden en que haya
    /// llegado la secuencia (p. ej. el orden de lectura de la base de datos).
    /// </summary>
    /// <exception cref="InvalidOperationException"><paramref name="candidatos"/> está vacío.</exception>
    public static T SelectWinner<T>(
        IEnumerable<T> candidatos,
        Func<T, string?> source,
        Func<T, DateTimeOffset> uploadedAt)
    {
        return candidatos
            .OrderBy(c => Weight(source(c)))
            .ThenByDescending(c => uploadedAt(c))
            .First();
    }
}
