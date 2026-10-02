namespace Flit.Tramites.Domain.Tramites.Estados;

/// <summary>
/// Bug #13194 (punto 4) — qué trámites siguen <b>pendientes de firma</b> de una persona: los que la
/// empresa todavía no ha entregado al organismo para decidir y que aún pueden cambiar de documentos.
///
/// <para>Regla de negocio: cuando una persona aprueba su validación de identidad, se firman ese
/// trámite y TODOS los pendientes donde es parte (persona natural) o representante legal (persona
/// jurídica), dentro del mismo tenant. El conjunto se define aquí, en un solo sitio, para que el
/// consumidor del evento y el disparo del correo usen la misma lista.</para>
///
/// <list type="bullet">
///   <item><see cref="TramiteEstado.Borrador"/> <b>finalizado</b>: es lo que ya firmaba el consumidor
///   (HU #10349). El borrador sin finalizar NO entra: todavía no tiene FUR, y cuando se finalice el
///   generador resuelve la identidad vigente de la persona y pinta el sello por sí mismo.</item>
///   <item><see cref="TramiteEstado.Rechazado"/> con subsanación activa: el gestor lo está corrigiendo
///   para re-radicarlo; sus documentos se rehacen.</item>
///   <item><see cref="TramiteEstado.Preparado"/>, <see cref="TramiteEstado.Preasignacion"/> y
///   <see cref="TramiteEstado.Asignado"/>: radicados o listos para radicar, pero sin la entrega al
///   organismo para decidir. El sistema ya regenera el FUR en ellos (asignación de placa), así que
///   regenerarlo aquí no viola ninguna inmutabilidad: el FUR no escribe <c>field_values</c>.</item>
/// </list>
///
/// <para>Quedan FUERA: <see cref="TramiteEstado.Entregado"/> (el organismo tiene a la vista los
/// documentos y decide sobre ellos; cambiarlos por debajo rompería lo que está revisando),
/// <see cref="TramiteEstado.Rechazado"/> sin subsanación (devuelto, nadie lo está trabajando) y los
/// finales (<see cref="TramiteEstado.Aprobado"/>, <see cref="TramiteEstado.Anulado"/>,
/// <see cref="TramiteEstado.Revocado"/>).</para>
/// </summary>
public static class TramiteFirmaPendiente
{
    /// <summary>Estados posteriores al borrador que siguen pendientes de firma (sin subsanación).</summary>
    public static readonly IReadOnlyList<string> EstadosRadicadosPendientes =
        [TramiteEstado.Preparado, TramiteEstado.Preasignacion, TramiteEstado.Asignado];

    /// <summary>
    /// ¿El trámite entra en el lote de firma al aprobarse la identidad de una de sus partes?
    /// </summary>
    public static bool EntraEnLoteDeFirma(string? status, bool subsanacionActiva, bool borradorFinalizado) =>
        (string.Equals(status, TramiteEstado.Borrador, StringComparison.Ordinal) && borradorFinalizado)
        || (string.Equals(status, TramiteEstado.Rechazado, StringComparison.Ordinal) && subsanacionActiva)
        || EsRadicadoPendiente(status);

    /// <summary>
    /// ¿Se le puede enviar (o reenviar) a una parte el correo de validación de identidad en este
    /// estado? Los editables de siempre (<see cref="TramiteEstado.PermiteEdicionDatos"/>) más los
    /// radicados pendientes: una identidad vencida antes de la entrega al organismo debe poder
    /// renovarse sin devolver el trámite a borrador.
    /// </summary>
    public static bool PermiteIniciarValidacion(string? status, bool subsanacionActiva) =>
        TramiteEstado.PermiteEdicionDatos(status, subsanacionActiva) || EsRadicadoPendiente(status);

    private static bool EsRadicadoPendiente(string? status) =>
        status is not null && EstadosRadicadosPendientes.Contains(status, StringComparer.Ordinal);
}
