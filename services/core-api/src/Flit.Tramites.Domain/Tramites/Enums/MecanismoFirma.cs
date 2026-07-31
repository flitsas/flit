namespace Flit.Tramites.Domain.Tramites.Enums;

/// <summary>
/// HU #11061 — mecanismo con el que se plasma la firma del representante legal de una parte jurídica
/// en los documentos del trámite.
/// <para>
/// Hasta ahora la elección era IMPLÍCITA y global: la HU #11031 fijó la prioridad del baúl (si hay
/// firma de baúl vigente, esa es la firma y no se añade el sello de identidad). Esa regla sigue siendo
/// el <b>valor por defecto</b>; lo que se persiste aquí es la elección EXPLÍCITA del gestor cuando el
/// representante tiene los dos mecanismos vigentes y el negocio necesita uno concreto.
/// </para>
/// <para>
/// Se guarda en <c>actor.metadata</c> (jsonb), junto al representante legal elegido, en vez de una
/// columna nueva: es un dato del representante seleccionado, viaja por el mismo camino que ya lo lleva
/// hasta los generadores y no exige migración.
/// </para>
/// </summary>
public static class MecanismoFirma
{
    /// <summary>Firma precargada del baúl (ADR-0025).</summary>
    public const string Baul = "baul";

    /// <summary>Sello de la validación biométrica de identidad.</summary>
    public const string Identidad = "identidad";

    /// <summary>
    /// Normaliza el valor recibido: <c>null</c> si viene vacío o no es uno de los dos mecanismos
    /// conocidos. <c>null</c> significa "sin elección explícita" ⇒ se aplica la precedencia del baúl.
    /// </summary>
    public static string? Normalizar(string? value)
    {
        var v = value?.Trim().ToLowerInvariant();
        return v is Baul or Identidad ? v : null;
    }

    /// <summary>
    /// ¿Debe consumirse la firma del baúl para esta parte? Sí salvo que el gestor haya elegido
    /// explícitamente el sello de identidad. Sin elección se mantiene el comportamiento de la
    /// HU #11031 (el baúl manda si existe y está vigente).
    /// </summary>
    public static bool ConsumeBaul(string? mecanismo) =>
        Normalizar(mecanismo) != Identidad;
}
