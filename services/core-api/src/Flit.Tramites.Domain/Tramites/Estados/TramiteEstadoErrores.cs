namespace Flit.Tramites.Domain.Tramites.Estados;

/// <summary>
/// Códigos de error del módulo de estados (N 03). Contrato compartido entre el servicio de
/// ciclo de vida, los endpoints y el frontend. Los endpoints los exponen como
/// <c>ProblemDetails.title</c> con el mensaje en español en <c>detail</c> (ADR-0022).
/// </summary>
public static class TramiteEstadoErrores
{
    /// <summary>RF02 — la transición solicitada no existe en la máquina de estados (422).</summary>
    public const string TransicionNoPermitida = "transicion_no_permitida";

    /// <summary>RF04 — el trámite está en estado final (aprobado/anulado): ni transiciones ni edición (422).</summary>
    public const string EstadoFinal = "estado_final";

    /// <summary>RF03 — gate Borrador→Preparado: la validación de identidad no está aprobada/vigente (422).</summary>
    public const string IdentidadNoAprobada = "identidad_no_aprobada";

    /// <summary>RF03 — gate Borrador→Preparado: faltan documentos obligatorios del checklist (422).</summary>
    public const string DocumentosIncompletos = "documentos_incompletos";

    /// <summary>El estado destino no es un estado de negocio conocido (422).</summary>
    public const string EstadoDesconocido = "estado_desconocido";

    /// <summary>Anular o rechazar exige motivo (RF05) (422).</summary>
    public const string MotivoRequerido = "motivo_requerido";

    /// <summary>RNF01 — conflicto de concurrencia optimista (row_version): reintentar (409, sin efectos parciales).</summary>
    public const string ConflictoConcurrencia = "conflicto_concurrencia";

    /// <summary>La instancia no existe o no pertenece al tenant (404).</summary>
    public const string NoEncontrado = "not_found";

    /// <summary>Gate de entrega — el tipo de trámite no está publicado (422).</summary>
    public const string TipoNoPublicado = "not_published";

    /// <summary>
    /// Gate de entrega (R09) — el organismo de tránsito elegido no está habilitado para la
    /// empresa: sin ese grant el trámite entregado no llega a la bandeja del OT (422).
    /// </summary>
    public const string OrganismoNoHabilitado = "organismo_no_habilitado";

    /// <summary>Gate de entrega — una regla OT activa bloquea la entrega (422).</summary>
    public const string ReglaOtBloquea = "ot_rule_blocked";

    /// <summary>
    /// R10 (HU #10597) — gate Borrador→Preparado del traspaso: el vehículo tiene gravámenes (prenda)
    /// y no se ha registrado una decisión de prenda vigente (409).
    /// </summary>
    public const string PrendaDecisionRequerida = "prenda_decision_requerida";

    /// <summary>
    /// R10 (HU #10597) — la decisión de prenda registrada exige un documento de soporte que no se ha
    /// adjuntado (409).
    /// </summary>
    public const string PrendaDocumentoRequerido = "prenda_documento_requerido";

    /// <summary>
    /// CF-06 (HU #10881) — el override compañía+OT exige el documento de prenda y no está adjunto
    /// (409). Código PROPIO, distinto de <see cref="PrendaDocumentoRequerido"/>, desde 2026-08-12:
    /// ambos caminos compartían código y el wizard pintaba el copy del gate del traspaso ("la decisión
    /// de prenda seleccionada requiere…") para un bloqueo cuyo origen es una regla del organismo, no
    /// la decisión del gestor. El backend ya distinguía los dos casos en el <c>detail</c>; el listado
    /// de blockers del wizard no, porque solo transporta el código. Separarlos permite que el mensaje
    /// diga de dónde viene, sin romper la coincidencia wizard/submit: ambos emiten ESTE código para
    /// este camino.
    /// </summary>
    public const string PrendaDocumentoRequeridoOt = "prenda_documento_requerido_ot";

    /// <summary>
    /// HU #11591 — la decisión de prenda vigente CONSTITUYE gravamen (<c>solicitar</c>/<c>registrar</c>,
    /// ver <see cref="Flit.Tramites.Domain.Tramites.ValueObjects.PrendaDecision.ImplicaGravamen"/>) y no
    /// tiene diligenciado el acreedor (<c>AcreedorNombre</c> y/o <c>AcreedorDocumento</c>): el FUR no
    /// puede salir con el gravamen sin beneficiario identificado (409).
    /// </summary>
    public const string PrendaAcreedorRequerido = "prenda_acreedor_requerido";

    /// <summary>
    /// El trámite de LEVANTAMIENTO de prenda no tiene diligenciada la entidad ante la que se levantó
    /// el gravamen (409). Es lo que el párrafo 23 del FUR declara en este trámite: sin ella el
    /// recuadro sale mudo mientras la casilla 12 afirma que hubo levantamiento. No aplica a traspaso
    /// ni a matrícula, donde <c>levantar</c> es una decisión entre varias y conserva su literal.
    /// </summary>
    public const string PrendaEntidadLevantamientoRequerida = "prenda_entidad_levantamiento_requerida";

    /// <summary>
    /// El trámite declara un organismo de DESTINO —el traslado de cuenta— y no está diligenciado, o
    /// el elegido no está habilitado para la compañía (409). Sin él el FUR no puede decir a dónde va
    /// la cuenta, que es el objeto entero del trámite.
    /// </summary>
    public const string OrganismoDestinoRequerido = "organismo_destino_requerido";

    /// <summary>
    /// HU #11051 — el gestor pidió generar o regenerar documentación de un trámite en estado final
    /// (aprobado/anulado), cuya documentación ya es definitiva (409). No aplica a la regeneración
    /// interna del sistema (aprobación del OT, asignación de placa, identidad validada).
    /// </summary>
    public const string GeneracionBloqueadaEstadoFinal = "generacion_bloqueada_estado_final";

    /// <summary>
    /// ADR-0036 §D9 (HU #10916) — al aprobar un trámite que exige mandato hay VARIOS mandatarios y
    /// ninguno cotejó con el usuario que aprueba: se debe elegir uno explícitamente (409, subsanable
    /// reintentando la transición con <c>mandateSignerId</c>).
    /// </summary>
    public const string MandatarioRequerido = "mandatario_requerido";

    /// <summary>
    /// ICT (servicio v1 <c>pauseDraftProcess</c> / bandera <c>starts_procedure_in_paused</c>) — el
    /// trámite está PAUSADO (<c>procedure_instances.is_paused=true</c>): no avanza (radicación /
    /// preparación bloqueadas), replicando el <c>ForbiddenError</c> de v1. Es reversible (reanudar con
    /// <c>pauseProcess=false</c>). La anulación NO se bloquea. Solo aplica a trámites pausados (default
    /// false) ⇒ cero impacto en trámites de plataforma (409).
    /// </summary>
    public const string TramitePausado = "tramite_pausado";
}
