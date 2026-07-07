// Mapa código→copy amigable para las razones de incompletitud de un paso
// (WizardStep.reasons) y los bloqueos de envío (WizardState.blockers). El
// backend manda los códigos; la UI los traduce a lenguaje del operador. Si
// llega un código desconocido se devuelve un fallback legible (no se rompe).

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
  preflight_yellow: 'Hay advertencias por revisar',
  validacion_pendiente: 'Falta validar el resultado legal',
  // documentos
  documentos_incompletos: 'Faltan documentos obligatorios',
  // actores
  comprador_incompleto: 'Faltan datos del comprador',
  vendedor_incompleto: 'Faltan datos del vendedor',
  actores_incompletos: 'Faltan datos de los participantes',
  // datos comerciales
  comercial_incompleto: 'Faltan datos comerciales (valor, causal, impuestos)',
  // identidad / firma / FUR (Slice 6-7)
  identidad_pendiente: 'Validación biométrica pendiente',
  pendiente_biometria: 'Validación biométrica pendiente',
  pendiente_firma: 'Firma de la compraventa pendiente',
  fur_pendiente: 'FUR pendiente (opcional)',
  // R10 (HU #10597/#10598) — prenda como gate del traspaso (gravámenes en warn).
  prenda_decision_requerida:
    'El vehículo tiene gravámenes: registra una decisión de prenda para continuar',
  prenda_documento_requerido:
    'La decisión de prenda seleccionada requiere adjuntar su documento de soporte',
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
  pendiente_firma: 'Firma de la compraventa pendiente',
  fur_pendiente: 'FUR pendiente (opcional)',
  pasos_incompletos: 'Hay pasos sin completar',
  // R10 (HU #10597/#10598) — gate de preparación/radicación del traspaso por prenda.
  prenda_decision_requerida:
    'El vehículo tiene gravámenes: registra una decisión de prenda antes de preparar o radicar el trámite',
  prenda_documento_requerido:
    'La decisión de prenda seleccionada requiere adjuntar su documento de soporte',
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
