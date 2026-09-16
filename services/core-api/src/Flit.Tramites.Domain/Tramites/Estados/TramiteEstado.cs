namespace Flit.Tramites.Domain.Tramites.Estados;

/// <summary>
/// Estados de NEGOCIO del ciclo de vida del trámite (N 03, RF01). Son los valores que se
/// persisten en <c>tramites.procedure_instances.status</c> (en español, snake_case-safe) y
/// los que expone la API. Reemplazan al vocabulario draft/submitted/... (ADR-0022).
/// </summary>
public static class TramiteEstado
{
    public const string Borrador = "borrador";
    public const string Anulado = "anulado";
    public const string Preparado = "preparado";

    /// <summary>
    /// ADR-0059 (Epic #12549) — Ruta Larga de matrícula inicial: el trámite se radicó SIN placa y espera
    /// que el Organismo de Tránsito se la asigne. UI: «Preasignación». Exclusivo de los tipos con
    /// <c>gate_profile.requiresPlateRequest = true</c> (ver <see cref="TramiteTransitionPolicy"/>).
    /// Sustituye al sub-estado <c>plate_flow_status = 'preasignado'</c> (Feature #10587), que se retira.
    /// </summary>
    public const string Preasignacion = "preasignacion";

    /// <summary>
    /// ADR-0059 (Epic #12549) — el OT ya asignó la placa; el gestor gestiona SOAT e impuestos SIN salir
    /// de este estado y, cuando termina, «Envía al OT» (→ <see cref="Entregado"/>). Sustituye al
    /// sub-estado <c>plate_flow_status = 'asignado'</c>. El antiguo <c>'terminado'</c> desaparece: ES
    /// <see cref="Entregado"/>.
    /// </summary>
    public const string Asignado = "asignado";

    public const string Entregado = "entregado";
    public const string Aprobado = "aprobado";
    public const string Rechazado = "rechazado";

    /// <summary>
    /// HU #12165/#12166 (Feature #12156) — el Organismo de Tránsito deshace una aprobación propia.
    /// Alcanzable SOLO desde <see cref="Aprobado"/>, y SOLO por el flujo OT (el admin de FLIT no tiene
    /// autoridad para revertir una decisión del organismo; ver "Cambiar estado"/"Anular" del admin,
    /// que excluyen <see cref="Aprobado"/> explícitamente). Final: libera la placa (
    /// <see cref="EstadosQueLiberanPlaca"/>) y habilita re-radicar con el mismo VIN/placa (excluido de
    /// los gates de duplicidad CF-01 y de estado registral CF-03).
    /// </summary>
    public const string Revocado = "revocado";

    /// <summary>
    /// LEGACY — ya no es un estado de negocio activo. La subsanación vive como flag
    /// <c>subsanacion_activa</c> sobre <see cref="Rechazado"/>. Se conserva la constante para
    /// leer historial / filas migradas pendientes. No forma parte de <see cref="Todos"/>.
    /// </summary>
    public const string Subsanacion = "subsanacion";

    /// <summary>Todos los estados válidos (para validación de entrada y checks DDL).</summary>
    public static readonly IReadOnlyList<string> Todos =
        [Borrador, Anulado, Preparado, Preasignacion, Asignado, Entregado, Aprobado, Rechazado, Revocado];

    /// <summary>
    /// Estados de la RUTA DE PLACA (ADR-0059): solo los alcanza un tipo que pide placa. Un trámite en
    /// cualquiera de ellos ya está en manos del organismo (<see cref="RecibidosPorOrganismo"/>), sigue
    /// «en proceso» para duplicidad (<see cref="EstadosEnProceso"/>) y retiene la placa
    /// (<see cref="OcupaPlaca"/>).
    /// </summary>
    public static readonly IReadOnlyList<string> EstadosDeRutaDePlaca = [Preasignacion, Asignado];

    /// <summary>¿<paramref name="estado"/> pertenece a la ruta de placa? Ver <see cref="EstadosDeRutaDePlaca"/>.</summary>
    public static bool EsEstadoDeRutaDePlaca(string? estado) =>
        estado is not null && EstadosDeRutaDePlaca.Contains(estado, StringComparer.Ordinal);

    /// <summary>Estados FINALES (RF04): sin transiciones posteriores ni edición de datos.</summary>
    public static readonly IReadOnlyList<string> Finales = [Aprobado, Anulado, Revocado];

    /// <summary>
    /// Estados en los que el trámite YA ESTÁ EN MANOS DEL ORGANISMO DE TRÁNSITO (HU #11945).
    ///
    /// <para>Un trámite en <see cref="Borrador"/> o <see cref="Preparado"/> todavía lo está redactando
    /// la empresa cliente: no se ha enviado, no le ha llegado al organismo, y por tanto el organismo no
    /// puede verlo. Exponerlo filtraría el trabajo en curso de la empresa a un tercero.</para>
    ///
    /// <para><see cref="Anulado"/> queda FUERA por decisión de producto, aun sabiendo que se puede
    /// llegar a él desde <see cref="Rechazado"/> (es decir, después de haber sido entregado): un
    /// trámite anulado ya no es accionable para el organismo. El precio es que el organismo pierde de
    /// vista un trámite sobre el que sí decidió; si eso llega a molestar, la corrección no es añadir
    /// <see cref="Anulado"/> a esta lista —eso dejaría entrar también los anulados desde
    /// <see cref="Borrador"/>, que nunca llegaron— sino exigir además que exista un evento
    /// <see cref="Entregado"/> en su historial.</para>
    ///
    /// <para><see cref="Subsanacion"/> SÍ entra pese a ser legado y no estar en <see cref="Todos"/>:
    /// solo lo llevan filas migradas que provienen de un rechazo, así que esos trámites estuvieron
    /// entregados. Dejarlo fuera escondería datos históricos que el organismo sí trabajó.</para>
    ///
    /// <para><see cref="Revocado"/> (HU #12166, Feature #12156) SÍ entra, a diferencia de
    /// <see cref="Anulado"/>: no tiene el mismo problema de origen, porque su ÚNICA transición
    /// posible es <see cref="Aprobado"/> → <see cref="Revocado"/> (ver
    /// <c>TramiteStateMachine.Transitions</c>), y a <see cref="Aprobado"/> solo se llega habiendo
    /// pasado por <see cref="Entregado"/>. No hace falta el resguardo adicional que sí necesitaría
    /// <see cref="Anulado"/> (evento <see cref="Entregado"/> en el historial): aquí es imposible que
    /// no lo haya. Además la revocación es una decisión que el propio organismo tomó (deshace su
    /// aprobación), así que ocultársela sería peor que el "precio" que sí se acepta para Anulado.</para>
    ///
    /// <para><see cref="Preasignacion"/> y <see cref="Asignado"/> (ADR-0059) SÍ entran: el trámite ya
    /// se radicó y es el organismo quien trabaja la cola de placa (asignar / liberar / rechazar). Lo que
    /// NO puede hacer en ellos es decidir (aprobar), que sigue siendo exclusivo de
    /// <see cref="Entregado"/>; esa restricción vive en <see cref="TramiteStateMachine"/>.</para>
    ///
    /// <para>Ojo al ampliar <see cref="Todos"/>: un estado nuevo NO es visible para el organismo hasta
    /// que se añada aquí explícitamente. Es el lado seguro por defecto.</para>
    /// </summary>
    public static readonly IReadOnlyList<string> RecibidosPorOrganismo =
        [Preasignacion, Asignado, Entregado, Aprobado, Rechazado, Subsanacion, Revocado];

    /// <summary>
    /// ¿El trámite ya está en manos del organismo de tránsito? Ver
    /// <see cref="RecibidosPorOrganismo"/>.
    /// </summary>
    public static bool EstaEnManosDelOrganismo(string? estado) =>
        estado is not null && RecibidosPorOrganismo.Contains(estado, StringComparer.Ordinal);

    /// <summary>
    /// Estados en los que el trámite LLEGA al organismo para que actúe (ADR-0059): <see cref="Entregado"/>
    /// (decidir) y <see cref="Preasignacion"/> (asignar placa). Una fila de historial hacia uno de ellos
    /// es una "entrega" para los relojes y recuentos del organismo; <see cref="Asignado"/> no lo es: ahí
    /// la pelota está en el gestor.
    /// </summary>
    public static readonly IReadOnlyList<string> EstadosDeLlegadaAlOrganismo = [Preasignacion, Entregado];

    /// <summary>¿Una transición hacia <paramref name="toStatus"/> pone el trámite en manos del organismo?</summary>
    public static bool EsLlegadaAlOrganismo(string? toStatus) =>
        toStatus is not null && EstadosDeLlegadaAlOrganismo.Contains(toStatus, StringComparer.Ordinal);

    /// <summary>
    /// ¿La transición es una RADICACIÓN (el gestor radica o re-radica)? Desde <see cref="Preparado"/> o
    /// <see cref="Rechazado"/> hacia un estado de llegada. Deja fuera <c>asignado → entregado</c>
    /// («Enviar al OT»): ese trámite ya se radicó.
    /// </summary>
    public static bool EsRadicacion(string? from, string? to) =>
        from is Preparado or Rechazado && EsLlegadaAlOrganismo(to);

    /// <summary>
    /// Estados en los que el trámite sigue ABIERTO en la bandeja del organismo (sin decisión final):
    /// la cola de placa completa y la de decisión. Universo de los "pendientes" de métricas e informes.
    /// </summary>
    public static readonly IReadOnlyList<string> PendientesDelOrganismo = [Preasignacion, Asignado, Entregado];

    /// <summary>
    /// Estados "en proceso" (CF-01, HU #10876): activan el bloqueo de duplicidad de trámite por
    /// familia. Los estados finales (<see cref="Aprobado"/>, <see cref="Rechazado"/>,
    /// <see cref="Anulado"/>) NO cuentan por sí solos. Un <see cref="Rechazado"/> con
    /// <c>subsanacion_activa</c> SÍ cuenta (ver <see cref="EstaEnProceso"/>).
    /// </summary>
    public static readonly IReadOnlyList<string> EstadosEnProceso =
        [Borrador, Preparado, Preasignacion, Asignado, Entregado];

    /// <summary>
    /// Estados que LIBERAN la placa: un trámite en estos estados ya no la retiene y la placa puede
    /// asignarse a otro trámite. Cualquier otro estado (borrador, preparado, preasignacion, asignado,
    /// entregado, aprobado) la mantiene ocupada — una placa no puede estar viva en dos trámites a la vez.
    /// </summary>
    public static readonly IReadOnlyList<string> EstadosQueLiberanPlaca = [Rechazado, Anulado, Revocado];

    /// <summary>¿Un trámite en <paramref name="estado"/> retiene la placa e impide reasignarla?</summary>
    public static bool OcupaPlaca(string? estado) =>
        estado is not null
        && !EstadosQueLiberanPlaca.Contains(estado, StringComparer.OrdinalIgnoreCase);

    /// <summary>¿<paramref name="estado"/> es un estado de negocio conocido?</summary>
    public static bool EsValido(string? estado) =>
        estado is not null && Todos.Contains(estado, StringComparer.Ordinal);

    /// <summary>¿<paramref name="estado"/> es final (RF04)? Aprobado, Anulado y Revocado son inmutables.</summary>
    public static bool EsFinal(string? estado) =>
        estado is Aprobado or Anulado or Revocado;

    /// <summary>
    /// ¿El trámite está "en proceso" para duplicidad (CF-01)? Incluye el legado
    /// <see cref="Subsanacion"/> y <see cref="Rechazado"/> con flag de subsanación activa.
    /// </summary>
    public static bool EstaEnProceso(string? estado, bool subsanacionActiva = false) =>
        estado is not null
        && (EstadosEnProceso.Contains(estado, StringComparer.OrdinalIgnoreCase)
            || string.Equals(estado, Subsanacion, StringComparison.OrdinalIgnoreCase)
            || (string.Equals(estado, Rechazado, StringComparison.OrdinalIgnoreCase) && subsanacionActiva));

    /// <summary>
    /// ¿Se pueden editar datos del expediente (campos, actores, adjuntos, etc.)?
    /// Editable en <see cref="Borrador"/>, en <see cref="Rechazado"/> con subsanación activa,
    /// o (legacy) en <see cref="Subsanacion"/>.
    /// </summary>
    public static bool PermiteEdicionDatos(string? status, bool subsanacionActiva = false) =>
        string.Equals(status, Borrador, StringComparison.OrdinalIgnoreCase)
        || (string.Equals(status, Rechazado, StringComparison.OrdinalIgnoreCase) && subsanacionActiva)
        || string.Equals(status, Subsanacion, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// ¿El <b>GESTOR</b> puede generar o regenerar documentación del expediente en este estado?
    /// (HU #11051)
    /// <para>En los estados <see cref="Finales"/> la documentación del expediente es DEFINITIVA: es la
    /// que el organismo de tránsito tuvo a la vista al aprobar (o la del trámite anulado). Permitir que
    /// el gestor la regenerara después reemplazaba esos PDF por otros nuevos, dejando el expediente
    /// aprobado sin la documentación con la que se aprobó.</para>
    /// <para><b>Solo aplica al gestor.</b> El sistema SÍ regenera en estado final por diseño: la
    /// aprobación del organismo de tránsito regenera FUR, mandato, trámite virtual y certificados para
    /// que reflejen las firmas definitivas (HU #10996), y lo propio hacen la asignación de placa, el
    /// consumidor de identidad validada y las transiciones de estado. Por eso este gate se aplica en los
    /// <b>endpoints del gestor</b> y NO dentro de los handlers de generación, que son compartidos.</para>
    /// </summary>
    public static bool PermiteGeneracionDocumentalDelGestor(string? status) => !EsFinal(status);

    /// <summary>
    /// ¿La re-radicación selectiva (gates por diff de snapshot) aplica a esta transición a entregado?
    /// </summary>
    public static bool EsReRadicacionSubsanacion(string? from, bool subsanacionActiva) =>
        string.Equals(from, Subsanacion, StringComparison.OrdinalIgnoreCase)
        || (string.Equals(from, Rechazado, StringComparison.OrdinalIgnoreCase) && subsanacionActiva);
}
