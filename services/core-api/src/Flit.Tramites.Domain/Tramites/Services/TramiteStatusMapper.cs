using Flit.Tramites.Domain.Enums;
using Flit.Tramites.Domain.Tramites.Enums;

namespace Flit.Tramites.Domain.Tramites.Services;

/// <summary>
/// Mapea el estado INTERNO de proceso (<see cref="TramiteEstadoInterno"/>, 14 estados) al
/// estado de PERSISTENCIA (<see cref="ProcedureInstanceStatus"/>, 5 estados runtime de #10150).
/// <para>
/// DECISIÓN: la máquina de proceso de Johan es más rica que el enum de persistencia, que
/// ya usa el snapshot EF / la API runtime. En lugar de duplicar o romper ese enum, la
/// máquina de proceso vive como capa de dominio separada y SE MAPEA al status de
/// persistencia con esta función. Así el runtime conserva sus 5 estados estables y el
/// dominio gana granularidad sin migración.
/// </para>
/// </summary>
public static class TramiteStatusMapper
{
    /// <summary>Estado de persistencia equivalente al estado interno de proceso.</summary>
    public static string ToPersistenceStatus(TramiteEstadoInterno estado) => estado switch
    {
        TramiteEstadoInterno.Borrador => ProcedureInstanceStatus.Draft,

        TramiteEstadoInterno.Radicado or
        TramiteEstadoInterno.EnviadoTransito or
        TramiteEstadoInterno.RecibidoTransito or
        TramiteEstadoInterno.PlacaPreasignada or
        TramiteEstadoInterno.SolicitudSoat or
        TramiteEstadoInterno.SoatComprado or
        TramiteEstadoInterno.SoatVerificado => ProcedureInstanceStatus.Submitted,

        TramiteEstadoInterno.EnValidacion or
        TramiteEstadoInterno.Documentos or
        TramiteEstadoInterno.Identidad or
        TramiteEstadoInterno.Aprobado => ProcedureInstanceStatus.InReview,

        TramiteEstadoInterno.Completado => ProcedureInstanceStatus.Completed,

        TramiteEstadoInterno.Rechazado => ProcedureInstanceStatus.Cancelled,

        _ => ProcedureInstanceStatus.Draft,
    };
}
