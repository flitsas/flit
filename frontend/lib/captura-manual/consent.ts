// Textos literales del paso Datos (réplica de Kyverum, decisión del PO, Épica #13202).

import { DR_FLIT_SUPPORT_EMAIL } from "@/components/dr-flit/dr-flit-intents";

/**
 * Versión del TEXTO de consentimiento que este front renderiza. Debe coincidir con la vigente del backend
 * (`ManualCaptureConsent.TextVersion`, que devuelve el GET en `consentTextVersion`); al aceptar se envía la que
 * devolvió el GET y, si no coincide con esta, la página bloquea el avance (el texto cambió: recargar).
 */
export const RENDERED_CONSENT_TEXT_VERSION = "manual-ley1581-v2";

// Cualquier cambio de redacción exige subir RENDERED_CONSENT_TEXT_VERSION (y la del backend).
export const CONSENT_TEXT_BODY =
  "Autorizo de forma libre, previa y expresa a Flit, que es quien guarda mis datos, el tratamiento de mis datos biométricos (dato sensible) para verificar mi identidad (Ley 1581 de 2012). No estoy obligado(a).";

export const CONSENT_PRIVACY_LABEL = "Aviso de privacidad";

/** Texto literal completo de Kyverum (cuerpo + «Aviso de privacidad.»). */
export const CONSENT_TEXT = `${CONSENT_TEXT_BODY} ${CONSENT_PRIVACY_LABEL}.`;

export const CAPTURE_TIPS = [
  "Busca buena luz, sin reflejos.",
  "Que tu rostro y el documento se vean completos y nítidos.",
  "Usa el documento original.",
] as const;

// Bug #13449: canal de soporte de FLIT (el mismo de Ayuda y Dr. FLIT), no un correo personal.
export const CONTACT_EMAIL = DR_FLIT_SUPPORT_EMAIL;

export const LEGAL_FOOTER_PREFIX =
  "FLIT 2.0 guarda tus datos. Para conocerlos, corregirlos o pedir que se borren:";
// PENDIENTE VALIDACIÓN LEGAL: faltan las frases finales del texto original; la conservación de imágenes
// por FLIT difiere de lo que dice el texto de Kyverum. Se omite a propósito toda mención a Kyverum (decisión
// del PO); el resto sigue la captura del paso Firma del flujo real (2026-10-06).
export const AUTORIZACION_FIRMA_TITLE = "Autorización de trámite digital";
export const AUTORIZACION_FIRMA_TEXT =
  "Declaro que adelanto esta verificación de forma libre y voluntaria. Autorizo a Flit el tratamiento de mis datos personales, incluidos mis datos biométricos y mi firma, que son datos sensibles, únicamente para verificar mi identidad. Flit es quien guarda esos datos.";
export const AUTORIZACION_FIRMA_INSTRUCTION =
  "Firma dentro del recuadro con el dedo o el mouse para autorizar y continuar con tu trámite.";
