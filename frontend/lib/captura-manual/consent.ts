// Textos literales del paso Datos (réplica de Kyverum, decisión del PO, Épica #13202).

/** Versión del texto de consentimiento; se envía al backend y queda en la constancia. */
export const CONSENT_TEXT_VERSION = "kyverum-2026-10-05";

// PENDIENTE VALIDACIÓN LEGAL (Ley 1581 de 2012): la frase «Kyverum ... no conserva las imágenes» es
// falsa en el flujo manual (FLIT sí conserva las imágenes). Se deja tal cual por petición del PO.
// Cualquier cambio de redacción exige subir CONSENT_TEXT_VERSION.
export const CONSENT_TEXT =
  "Autorizo de forma libre, previa y expresa a Flit, que es quien guarda mis datos, el tratamiento de mis datos biométricos (dato sensible) para verificar mi identidad (Ley 1581 de 2012). No estoy obligado(a). Kyverum, empresa de Estados Unidos, solo hace la verificación y no conserva las imágenes.";

export const CONSENT_PRIVACY_LABEL = "Aviso de privacidad";

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
