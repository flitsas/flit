namespace Flit.Tramites.Domain.Tramites.Enums;

/// <summary>
/// Estados del workflow STT (Secretaría/Organismo de Tránsito) del traspaso
/// (paridad <c>TramiteEstadoStt</c> de Johan). Terminales: <see cref="Entregado"/>, <see cref="Anulado"/>.
/// </summary>
public enum TramiteEstadoStt
{
    Radicado,
    EnValidacion,
    Subsanacion,
    EnTramite,
    Aprobado,
    Rechazado,
    Entregado,
    Anulado,
}
