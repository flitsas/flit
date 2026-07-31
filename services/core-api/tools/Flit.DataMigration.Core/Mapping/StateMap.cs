using Flit.Tramites.Domain.Tramites.Estados;

namespace Flit.DataMigration.V1.Mapping;

/// <summary>
/// Traduce el estado de V1 (<c>process_status</c>, catálogo
/// <c>vehicle_transfer_parameter_process_status</c>) al estado de negocio de V2
/// (<see cref="TramiteEstado"/>).
/// <para>
/// V1 tiene 8 estados y V2 tiene 6: la traducción COLAPSA y por tanto pierde granularidad.
/// Para que no se pierda información, todo trámite migrado guarda además el estado original
/// en el campo <c>legacy_process_status</c>. Así la decisión es reversible: si negocio define
/// después otro destino para "Sent" o "Archived", se puede recalcular sin volver a V1.
/// </para>
/// </summary>
public static class StateMap
{
    /// <summary>Catálogo de V1, para reportes legibles por humanos.</summary>
    private static readonly Dictionary<int, string> V1Names = new()
    {
        [1] = "Draft",
        [2] = "Aborted",
        [3] = "Prepared",
        [4] = "Sent",
        [5] = "Delivered",
        [6] = "Approved",
        [7] = "Rejected",
        [8] = "Archived",
    };

    /// <summary>
    /// Estados de V1 SIN equivalente exacto en V2. Se migran igual (el migrador debe ser
    /// indiferente a lo que le manden), pero se reportan como decisión pendiente de negocio.
    /// </summary>
    private static readonly HashSet<int> Ambiguous = [4, 8];

    public static string V1Name(int processStatus) =>
        V1Names.TryGetValue(processStatus, out var name) ? name : $"desconocido({processStatus})";

    /// <summary>¿El mapeo de este estado es una decisión pendiente de negocio?</summary>
    public static bool IsAmbiguous(int processStatus) =>
        Ambiguous.Contains(processStatus) || !V1Names.ContainsKey(processStatus);

    /// <summary>
    /// Estado de V2 correspondiente. Nunca lanza: un estado desconocido (p. ej. el "10" no
    /// documentado que aparece en V1) cae en <c>borrador</c>, el estado más conservador —
    /// no afirma que el trámite terminó ni que se anuló — y queda marcado como ambiguo.
    /// </summary>
    public static string ToV2(int processStatus) => processStatus switch
    {
        1 => TramiteEstado.Borrador,
        2 => TramiteEstado.Anulado,
        3 => TramiteEstado.Preparado,
        // "Sent" (enviado al organismo de tránsito) no existe en V2; 'preparado' es el estado
        // inmediatamente anterior a la entrega y el más cercano semánticamente.
        4 => TramiteEstado.Preparado,
        5 => TramiteEstado.Entregado,
        6 => TramiteEstado.Aprobado,
        7 => TramiteEstado.Rechazado,
        // "Archived" es un trámite cerrado y archivado, no uno cancelado: mapearlo a
        // 'anulado' afirmaría algo falso. 'entregado' preserva mejor el hecho de que terminó.
        8 => TramiteEstado.Entregado,
        _ => TramiteEstado.Borrador,
    };
}
