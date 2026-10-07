/**
 * Motivos homologados de rechazo de una validación de identidad MANUAL (Épica #13202, contrato §3).
 *
 * Lista cerrada: el backend valida el código (`400 motivo_invalido` si no está) y el correo al cliente
 * usa las mismas etiquetas. Este es el ÚNICO lugar del front con las etiquetas en español: todo lo que
 * muestre o ofrezca un motivo (select del rechazo, detalle de un rechazo ya hecho) debe leerlo de aquí.
 */
export const MOTIVOS_RECHAZO_MANUAL = [
  { code: 'imagen_borrosa', label: 'Imagen borrosa' },
  { code: 'rostro_no_coincide', label: 'El rostro no coincide con el documento' },
  { code: 'documento_ilegible_o_incompleto', label: 'Documento ilegible o incompleto' },
  { code: 'documento_no_corresponde', label: 'El documento no corresponde' },
  { code: 'firma_ilegible_o_no_corresponde', label: 'Firma ilegible o no corresponde' },
  { code: 'captura_fuera_de_encuadre', label: 'Captura fuera de encuadre' },
] as const;

export type MotivoRechazoManualCode = (typeof MOTIVOS_RECHAZO_MANUAL)[number]['code'];

const ETIQUETAS: Record<string, string> = Object.fromEntries(
  MOTIVOS_RECHAZO_MANUAL.map((m) => [m.code, m.label]),
);

/** Etiqueta en español de un código; `null` si el código no está en la lista cerrada. */
export function etiquetaMotivoRechazoManual(code: string | null | undefined): string | null {
  return code ? (ETIQUETAS[code] ?? null) : null;
}
