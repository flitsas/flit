using Flit.Tramites.Domain.Tramites.Estados;

namespace Flit.DataMigration.V1.Mapping;

/// <summary>
/// Traduce el estado de V1 de una MATRÍCULA (<c>vehicle_registration_parameter_process_status</c>)
/// al estado de negocio de V2.
///
/// <para>
/// <b>Este catálogo NO es el de traspaso.</b> Matrícula tiene 10 estados y traspaso 8, y divergen
/// desde el id 5 (ver <see cref="IV1StateMap"/>). Confundirlos migra los aprobados como rechazados.
/// </para>
///
/// <para>
/// Como en traspaso, la traducción colapsa (10 estados → 6) y por eso el estado original siempre
/// se conserva en el campo <c>legacy_process_status</c>: la decisión es reversible sin volver a V1.
/// </para>
/// </summary>
public sealed class RegistrationStateMap : IV1StateMap
{
    public static readonly RegistrationStateMap Instance = new();

    private RegistrationStateMap() { }

    /// <summary>Catálogo real de V1, verificado contra pdn (13.055 matrículas).</summary>
    private static readonly Dictionary<int, string> V1Names = new()
    {
        [1] = "Draft",
        [2] = "Aborted",
        [3] = "Prepared",
        [4] = "Sent",
        [5] = "Assigned",
        [6] = "Delivered",
        [7] = "Approved",
        [8] = "Rejected",
        [9] = "Revoked",
        [10] = "Archived",
    };

    /// <summary>
    /// Estados sin equivalente exacto en V2. Se migran igual —el migrador es indiferente a lo que
    /// le manden— pero se reportan como decisión pendiente de negocio. En producción son pocos:
    /// Sent 1, Assigned 5, Revoked 7, Archived 0.
    /// </summary>
    private static readonly HashSet<int> Ambiguous = [4, 5, 9, 10];

    public string V1Name(int processStatus) =>
        V1Names.TryGetValue(processStatus, out var name) ? name : $"desconocido({processStatus})";

    public bool IsAmbiguous(int processStatus) =>
        Ambiguous.Contains(processStatus) || !V1Names.ContainsKey(processStatus);

    public string ToV2(int processStatus) => processStatus switch
    {
        1 => TramiteEstado.Borrador,
        2 => TramiteEstado.Anulado,
        3 => TramiteEstado.Preparado,

        // "Sent" (radicado al organismo) no existe en V2; 'preparado' es el estado inmediatamente
        // anterior a la entrega y el más cercano semánticamente. Mismo criterio que en traspaso.
        4 => TramiteEstado.Preparado,

        // "Assigned" (asignado dentro del organismo) es posterior a Sent: el trámite YA está en
        // tránsito, así que 'entregado' describe mejor el hecho que 'preparado'.
        5 => TramiteEstado.Entregado,

        6 => TramiteEstado.Entregado,
        7 => TramiteEstado.Aprobado,
        8 => TramiteEstado.Rechazado,

        // "Revoked": matrícula que se revocó DESPUÉS de aprobarse. No es lo mismo que anular un
        // trámite en curso, pero 'anulado' es lo más cercano y preserva que quedó sin efecto.
        // Son 7 trámites en producción; el original queda en legacy_process_status.
        9 => TramiteEstado.Anulado,

        // "Archived" es un trámite cerrado y archivado, no uno cancelado: 'entregado' preserva
        // mejor el hecho de que terminó. Mismo criterio que en traspaso.
        10 => TramiteEstado.Entregado,

        _ => TramiteEstado.Borrador,
    };
}
