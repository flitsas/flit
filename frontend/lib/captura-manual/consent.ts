// Textos literales del paso Datos (réplica de Kyverum, decisión del PO, Épica #13202).

/**
 * Versión del TEXTO de consentimiento que este front renderiza. Debe coincidir con la vigente del backend
 * (`ManualCaptureConsent.TextVersion`, que devuelve el GET en `consentTextVersion`); al aceptar se envía la que
 * devolvió el GET y, si no coincide con esta, la página bloquea el avance (el texto cambió: recargar).
 */
export const RENDERED_CONSENT_TEXT_VERSION = "manual-ley1581-v1";

// PENDIENTE VALIDACIÓN LEGAL (Ley 1581 de 2012): la frase «Kyverum ... no conserva las imágenes» es
// falsa en el flujo manual (FLIT sí conserva las imágenes). Se deja tal cual por petición del PO.
// Cualquier cambio de redacción exige subir RENDERED_CONSENT_TEXT_VERSION (y la del backend).
export const CONSENT_TEXT_BODY =
  "Autorizo de forma libre, previa y expresa a Flit, que es quien guarda mis datos, el tratamiento de mis datos biométricos (dato sensible) para verificar mi identidad (Ley 1581 de 2012). No estoy obligado(a). Kyverum, empresa de Estados Unidos, solo hace la verificación y no conserva las imágenes.";

export const CONSENT_PRIVACY_LABEL = "Aviso de privacidad";

/** Texto literal completo de Kyverum (cuerpo + «Aviso de privacidad.»). */
export const CONSENT_TEXT = `${CONSENT_TEXT_BODY} ${CONSENT_PRIVACY_LABEL}.`;

export const CAPTURE_TIPS = [
  "Busca buena luz, sin reflejos.",
  "Que tu rostro y el documento se vean completos y nítidos.",
  "Usa el documento original.",
] as const;

export const CONTACT_EMAIL = "samuel.cardenas@flitsas.com";

// PENDIENTE VALIDACIÓN LEGAL: misma frase sobre Kyverum que en CONSENT_TEXT.
export const LEGAL_FOOTER_PREFIX =
  "FLIT 2.0 guarda tus datos. Para conocerlos, corregirlos o pedir que se borren:";
export const LEGAL_FOOTER_SUFFIX =
  "Kyverum, empresa de Estados Unidos, solo hace la verificación y no conserva las imágenes.";
