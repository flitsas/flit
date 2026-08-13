// Mapa código→copy amigable para las razones de incompletitud de un paso
// (WizardStep.reasons) y los bloqueos de envío (WizardState.blockers). El
// backend manda los códigos; la UI los traduce a lenguaje del operador. Si
// llega un código desconocido se devuelve un fallback legible (no se rompe).

/** En qué punto de la cadena de identidad automática se estaba cuando algo salió mal. */
export type IdentidadAutomaticaEtapa = 'asegurar' | 'proveedor' | 'iniciar' | 'simular';

/**
 * Qué decirle al gestor cuando la identidad automática del Continuar no prospera.
 *
 * <p>La cadena son cuatro llamadas (asegurar → resolver proveedor → iniciar/simular) y hasta ahora
 * TODAS caían en el mismo mensaje: "no se pudo iniciar automáticamente la validación de identidad".
 * Con eso delante, un gestor que no ve llegar el correo de Kyverum no puede saber si el actor no
 * tiene correo, si esa persona ya tenía un envío en vuelo o si el proveedor rechazó — y nosotros
 * tampoco, porque el código de estado no quedaba en ninguna parte.</p>
 *
 * <p><b>El 409 no es un fallo.</b> Desde la decisión única de envío por persona (HU #11265), que ya
 * haya una validación en curso para ese documento significa que el sistema decidió NO mandar otra:
 * la identidad está encaminada. Pintarlo en rojo hacía leer como avería lo que era la regla
 * funcionando, así que sale como aviso positivo.</p>
 */
export function identidadAutomaticaCopy(
  etapa: IdentidadAutomaticaEtapa,
  status?: number,
): { message: string; tone: 'success' | 'error' } {
  if (status === 409) {
    return {
      message:
        'Esta persona ya tiene una validación de identidad en curso, así que no se envió otra.',
      tone: 'success',
    };
  }

  if (etapa === 'iniciar' && status === 400) {
    return {
      message:
        'Faltan datos del actor para pedir la validación de identidad (nombre, tipo y número de documento y correo).',
      tone: 'error',
    };
  }

  if (etapa === 'iniciar' && (status === 502 || status === 503)) {
    return {
      message: 'El proveedor de validación de identidad no respondió, así que el enlace no salió.',
      tone: 'error',
    };
  }

  if (etapa === 'iniciar') {
    return { message: 'No se pudo enviar la validación de identidad al proveedor.', tone: 'error' };
  }

  if (etapa === 'proveedor') {
    return {
      message: 'No se pudo resolver qué proveedor de identidad aplica a este trámite.',
      tone: 'error',
    };
  }

  // `asegurar` y `simular` comparten el mensaje de siempre: en el primero aún no se sabe nada del
  // envío, y el segundo no manda correos (proveedor mock), así que el detalle no aporta.
  return {
    message: 'No se pudo iniciar automáticamente la validación de identidad.',
    tone: 'error',
  };
}

/** Razones por las que un paso queda incompleto. */
const REASON_COPY: Record<string, string> = {
  // consultas RUNT por actor
  runt_comprador: 'Consulta RUNT del comprador',
  runt_vendedor: 'Consulta RUNT del vendedor',
  // consulta inicial del vehículo
  consulta_pendiente: 'Consulta el vehículo por placa',
  vin_pendiente: 'Consulta el VIN del vehículo',
  impuesto_pendiente: 'Confirma el paz y salvo de impuesto vehicular',
  // preflight / semáforo legal
  preflight_pendiente: 'Falta correr el pre-vuelo',
  preflight_red: 'Hay bloqueos críticos',
  preflight_provider_error: 'No se pudo verificar la consulta; vuelve a intentarla',
  // Sin `preflight_yellow`: el amarillo NO bloquea y este canal es el de "Antes de enviar,
  // resuelve:". Las advertencias se informan en PreflightPanel, no como razón de incompletitud.
  validacion_pendiente: 'Falta validar el resultado legal',
  // documentos
  documentos_incompletos: 'Faltan documentos obligatorios',
  // actores
  comprador_incompleto: 'Faltan datos del comprador',
  vendedor_incompleto: 'Faltan datos del vendedor',
  actores_incompletos: 'Faltan datos de los participantes',
  comprador_doc: 'Falta el documento del comprador',
  // Comparendos del comprador (traspaso). Sin estas entradas el código crudo se "humanizaba"
  // a "Simit Pendiente", que no le dice al operador ni qué falta ni dónde resolverlo.
  simit_pendiente:
    'Falta la consulta de comparendos del comprador: vuelve al paso 1 y ejecuta "Consultar RUNT" para generar el pre-vuelo',
  simit_doc:
    'La consulta de comparendos no corresponde al documento del comprador: vuelve al paso 1 y repite la consulta',
  simit_multas: 'El comprador tiene comparendos pendientes en el SIMIT',
  // datos comerciales
  comercial_incompleto: 'Faltan datos comerciales (valor, causal, impuestos)',
  comercial_valor: 'Ingresa un valor de venta mayor a cero',
  // identidad / firma / FUR (Slice 6-7)
  identidad_pendiente: 'Validación biométrica pendiente',
  pendiente_biometria: 'Validación biométrica pendiente',
  // B12 (HU #10661, ADR-0028): la firma es informativa y no bloquea el traspaso.
  pendiente_firma: 'Firma de la compraventa (informativa, no bloquea)',
  fur_pendiente: 'FUR pendiente (opcional)',
  // R10 (HU #10597/#10598) — prenda como gate del traspaso (gravámenes en warn).
  prenda_decision_requerida:
    'El vehículo tiene gravámenes: registra una decisión de prenda para continuar',
  prenda_documento_requerido:
    'La decisión de prenda seleccionada requiere adjuntar su documento de soporte',
  // CF-06 (HU #10881) — código distinto del anterior a propósito: este bloqueo NO nace de la
  // decisión del gestor sino de una regla del organismo, y decir "la decisión seleccionada
  // requiere…" a quien eligió "sin prenda" describe una causa que no es la suya.
  prenda_documento_requerido_ot:
    'El organismo de tránsito exige adjuntar el documento de prenda para este trámite',
  // R19 (HU #10604/#10605/#10697) — RNMC ya NO bloquea: la medida correctiva es informativa.
  rnmc_medida_pendiente:
    'Medida correctiva RNMC registrada (informativa, no bloquea el envío)',
};

/** Bloqueos que impiden enviar/finalizar el trámite. */
const BLOCKER_COPY: Record<string, string> = {
  preflight_red: 'Hay bloqueos críticos en el pre-vuelo',
  preflight_provider_error: 'No se pudo verificar la consulta (RUNT/SIMIT/RNMC); vuelve a intentarla antes de continuar',
  documentos_incompletos: 'Faltan documentos obligatorios',
  // N 03 (RF03) — gate Borrador→Preparado: identidad del comprador aprobada y vigente.
  identidad_no_aprobada: 'La validación de identidad del comprador no está aprobada',
  actores_incompletos: 'Faltan datos de los participantes',
  comercial_incompleto: 'Faltan datos comerciales',
  identidad_pendiente: 'Validación biométrica pendiente',
  pendiente_biometria: 'Validación biométrica pendiente',
  // B12 (HU #10661, ADR-0028): informativa; ya no es un bloqueo de envío.
  pendiente_firma: 'Firma de la compraventa (informativa, no bloquea)',
  fur_pendiente: 'FUR pendiente (opcional)',
  pasos_incompletos: 'Hay pasos sin completar',
  // R10 (HU #10597/#10598) — gate de preparación/radicación por prenda. Como BLOQUEO este código
  // tiene dos orígenes: el semáforo de gravámenes del traspaso y el override del organismo (que
  // aplica también a matrícula inicial, sin gravamen que detectar). Por eso no los nombra: decirle
  // "el vehículo tiene gravámenes" a quien matricula un vehículo nuevo describe una causa que no es
  // la suya — el mismo error que motivó separar prenda_documento_requerido_ot. Como RAZÓN de paso
  // sigue naciendo solo del semáforo, y allí el texto sí los nombra.
  prenda_decision_requerida:
    'Registra la decisión de prenda del vehículo antes de preparar o radicar el trámite',
  prenda_documento_requerido:
    'La decisión de prenda seleccionada requiere adjuntar su documento de soporte',
  // CF-06 (HU #10881) — ver la nota del mapa de razones: el origen es el organismo, no la decisión.
  prenda_documento_requerido_ot:
    'El organismo de tránsito exige adjuntar el documento de prenda para este trámite',
  // R19 (HU #10697) — RNMC ya NO bloquea el envío al OT; no hay blocker de medida correctiva.
};

/** Convierte un código a copy legible (fallback: el código humanizado). */
function humanize(code: string): string {
  return code
    .replace(/[_-]+/g, ' ')
    .replace(/\b\w/g, (c) => c.toUpperCase());
}

export function reasonCopy(code: string): string {
  return REASON_COPY[code] ?? humanize(code);
}

export function blockerCopy(code: string): string {
  return BLOCKER_COPY[code] ?? humanize(code);
}

/**
 * Nombre del paso en la nomenclatura de la propuesta de diseño.
 *
 * El backend manda el label junto a cada paso; esto NO lo sustituye como fuente de verdad de la
 * estructura —los pasos, su orden y su estado siguen viniendo del servidor—, solo unifica cómo se
 * escriben en pantalla. La misma etapa se llamaba distinto según la modalidad ("Consulta VIN" vs
 * "Consulta del vehículo") y el paso final aparecía como "Resumen del trámite" pese a ser donde se
 * genera el FUR y el expediente.
 *
 * `actores` es la clave del paso visual que fusiona vendedor+comprador en traspaso.
 */
/**
 * UN diccionario, no uno por modalidad.
 *
 * Había dos, y tres claves se llamaban distinto en cada uno: `documentos` era "Requisitos" en
 * matrícula y "Documentos" en traspaso; `fur` era "Resumen" y "FUR y Expediente". La diferencia no
 * venía de que los pasos hicieran cosas distintas —hacen exactamente lo mismo, con el mismo
 * componente— sino de que cada modalidad se portó de un asistente distinto del repo de propuesta.
 * Un gestor que trabaja las dos modalidades tenía que aprender dos vocabularios para el mismo
 * trabajo.
 *
 * Nomenclatura fijada por el equipo: Consulta Vehículo · Actores · Requisitos · Validación de
 * Identidad · Resumen. Los mismos nombres para cualquier tipo de trámite.
 *
 * `Datos Comerciales` no está en esa lista porque el paso solo existe en traspaso: no hay nada que
 * unificar, ninguna otra modalidad lo tiene. Los pasos y su orden los sigue definiendo el backend;
 * esto solo decide cómo se escriben.
 */
const STEP_LABELS: Record<string, string> = {
  consulta: 'Consulta Vehículo',
  consulta_vin: 'Consulta Vehículo',
  actores: 'Actores',
  vendedor: 'Actores',
  comprador: 'Actores',
  documentos: 'Requisitos',
  comercial: 'Datos Comerciales',
  identidad: 'Validación de Identidad',
  fur: 'Resumen',
};

/**
 * Etiqueta a mostrar para un paso. Cae al label del servidor cuando la clave no está mapeada:
 * un paso nuevo en backend aparece con su nombre, nunca en blanco.
 *
 * Ya no recibe la modalidad: una misma clave se llama igual en todos los trámites.
 */
export function stepLabelCopy(key: string, serverLabel: string): string {
  return STEP_LABELS[key] ?? serverLabel;
}
