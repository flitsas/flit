namespace Flit.Tramites.Domain.Certifications;

/// <summary>
/// Decide, dato a dato, si lo que acaba de llegar debe reemplazar a lo que ya estaba guardado.
/// </summary>
/// <remarks>
/// Es la regla que hoy no existe: el escritor de consultas sobrescribe sin mirar quién había puesto el
/// valor anterior, así que <b>una corrección manual se pierde en silencio</b> en la siguiente consulta.
/// La única precedencia documentada del sistema vive en el camino del OCR y no aplica a nadie más.
///
/// <para>Tres reglas, en orden:</para>
/// <list type="number">
///   <item><b>Un valor ausente nunca desplaza a uno presente.</b> Un proveedor que no manda el número
///         de póliza no puede borrar el que ya se tenía. Esta regla va primero porque es la que
///         convierte la reconsulta en una operación segura.</item>
///   <item><b>Una corrección manual sobrevive a la reconsulta</b> (D2). Es una excepción deliberada
///         al peso: si un operador arregló un dato a mano, sabía algo que el proveedor no.
///         <para><b>Nota sobre la formulación del plan.</b> El plan enuncia esta regla como «<c>user</c>
///         con <c>observed_at</c> posterior prevalece sobre <c>consultation</c>». Comparar las fechas
///         deja la excepción <b>inerte</b>: una consulta nueva siempre llega con un <c>observed_at</c>
///         más reciente que la corrección que pretende proteger, así que la corrección se perdería
///         igual — exactamente el defecto que D2 venía a cerrar. Se implementa la intención que el PO
///         enunció («¿sobrevive una corrección manual a una reconsulta posterior? sí, gana la
///         corrección»), sin comparar fechas entre esas dos fuentes.</para></item>
///   <item>Si no, gana el peso; a igual peso, gana lo más reciente.</item>
/// </list>
///
/// <para>No toca base de datos: es una función pura y se prueba como tal.</para>
/// </remarks>
public static class CertificationPrecedence
{
    public static int Weight(CertificationSourceKind source) => source switch
    {
        CertificationSourceKind.Consultation => 300,
        CertificationSourceKind.User => 200,
        CertificationSourceKind.Ocr => 100,
        _ => 50,
    };

    /// <summary>
    /// ¿Gana <paramref name="incoming"/> sobre <paramref name="existing"/>?
    /// </summary>
    /// <param name="incomingHasValue">¿El dato entrante trae algo? Un <c>null</c> no compite.</param>
    /// <param name="existingHasValue">¿El dato guardado tiene algo que proteger?</param>
    public static bool Wins(
        CertificationProvenance incoming,
        CertificationProvenance existing,
        bool incomingHasValue = true,
        bool existingHasValue = true)
    {
        // (1) Ausente nunca desplaza a presente.
        if (!incomingHasValue)
            return false;
        if (!existingHasValue)
            return true;

        // (2) D2 — la corrección manual sobrevive a la reconsulta. Sin comparación de fechas: ver la
        // nota de la documentación de esta clase.
        if (existing.Source == CertificationSourceKind.User
            && incoming.Source == CertificationSourceKind.Consultation)
        {
            return false;
        }

        // (3) Peso, y a igual peso lo más reciente.
        var incomingWeight = Weight(incoming.Source);
        var existingWeight = Weight(existing.Source);

        return incomingWeight != existingWeight
            ? incomingWeight > existingWeight
            : incoming.ObservedAt > existing.ObservedAt;
    }

    /// <summary>
    /// Aplica la regla celda a celda entre dos versiones del mismo valor certificado, devolviendo el
    /// que debe quedar. Es el uso normal desde la ingesta: fusiona sin perder lo que ya había.
    /// </summary>
    public static TValue Merge<TValue>(
        TValue incoming, CertificationProvenance incomingProvenance,
        TValue existing, CertificationProvenance existingProvenance)
        where TValue : ICertifiedValue =>
        Wins(incomingProvenance, existingProvenance, incoming.HasValue, existing.HasValue)
            ? incoming
            : existing;
}
