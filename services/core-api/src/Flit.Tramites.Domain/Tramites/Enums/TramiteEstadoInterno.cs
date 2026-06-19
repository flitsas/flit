namespace Flit.Tramites.Domain.Tramites.Enums;

/// <summary>
/// Estados internos del proceso de trámite (paridad <c>VALID_ESTADOS</c> de Johan, TRAM-12a).
/// Máquina de proceso de dominio, SEPARADA del estado de persistencia
/// <see cref="ProcedureInstanceStatus"/> (ver <c>TramiteStatusMapper</c>).
/// </summary>
public enum TramiteEstadoInterno
{
    Borrador,
    Radicado,
    EnValidacion,
    Documentos,
    Identidad,
    Aprobado,
    Rechazado,
    EnviadoTransito,
    RecibidoTransito,
    PlacaPreasignada,
    SolicitudSoat,
    SoatComprado,
    SoatVerificado,
    Completado,
}
