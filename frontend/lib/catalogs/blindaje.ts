import type { ProcedureAttachment } from '@/lib/api/types/procedure-runtime';

/**
 * Opciones del trámite de blindaje. Espejo de `BlindajeOpciones` (dominio backend): los códigos son
 * el contrato que viaja en `field_values` y el que el FUR traduce a observaciones, así que los dos
 * lados tienen que decir exactamente lo mismo.
 *
 * <p>Vive aparte del componente para que el resto del frontend —la vista previa de observaciones del
 * FUR, sobre todo— pueda leer la regla sin arrastrar React ni el `use client` de la tarjeta.</p>
 */

/** Clave de `field_values` donde vive la opción. Espejo de `BlindajeOpciones.FieldKey`. */
export const BLINDAJE_NIVEL_FIELD_KEY = 'blindaje_nivel';

/** Bandera derivada que el resto del expediente ya consumía. Espejo de `BlindajeOpciones.BanderaFieldKey`. */
export const BLINDAJE_FLAG_FIELD_KEY = 'blindaje';

/** DocTipo del certificado. Espejo del requisito parametrizado de `BLINDAJE`. */
export const BLINDAJE_DOC_TIPO = 'certificado_blindaje';

export type BlindajeOpcion = 'NIVEL_1' | 'NIVEL_2' | 'NIVEL_3' | 'DESMONTE';

/** Opciones en el mismo orden en que las declara el dominio (`BlindajeOpciones.Codigos`). */
export const BLINDAJE_OPCIONES: { codigo: BlindajeOpcion; label: string }[] = [
  { codigo: 'NIVEL_1', label: 'Blindaje nivel 1' },
  { codigo: 'NIVEL_2', label: 'Blindaje nivel 2' },
  { codigo: 'NIVEL_3', label: 'Blindaje nivel 3' },
  { codigo: 'DESMONTE', label: 'Desmontar blindaje' },
];

const CODIGOS = new Set<string>(BLINDAJE_OPCIONES.map((o) => o.codigo));

/** Lee la opción persistida; cualquier valor no reconocido se trata como «sin declarar». */
export function parseBlindajeOpcion(valor: string | null | undefined): BlindajeOpcion | null {
  const v = (valor ?? '').trim().toUpperCase();
  return CODIGOS.has(v) ? (v as BlindajeOpcion) : null;
}

/**
 * ¿El vehículo queda blindado al terminar? Solo los tres niveles. De aquí sale la bandera derivada
 * `blindaje`, que es la que marca la casilla «vehículo blindado SI/NO» del FUR — por eso el desmonte
 * la deja en `false` aunque el trámite SEA un blindaje.
 */
export function dejaElVehiculoBlindado(opcion: BlindajeOpcion | null): boolean {
  return opcion === 'NIVEL_1' || opcion === 'NIVEL_2' || opcion === 'NIVEL_3';
}

/** ¿Es el certificado de blindaje del expediente? */
export function esCertificadoDeBlindaje(a: ProcedureAttachment): boolean {
  return a.tipo.toLowerCase() === BLINDAJE_DOC_TIPO;
}

/**
 * Completo = opción declarada + certificado adjunto. El certificado es obligatorio en las CUATRO
 * opciones, desmonte incluido: también retirar un blindaje hay que acreditarlo.
 */
export function blindajeCompleto(
  opcion: BlindajeOpcion | null,
  attachments: ProcedureAttachment[],
): boolean {
  return opcion !== null && attachments.some(esCertificadoDeBlindaje);
}

/**
 * Texto que el FUR anexará a las observaciones. Espejo de `FurBlindajeObservation.Compose`: si
 * cambia la redacción allí, cambia aquí, y los tests de ambos lados usan los mismos ejemplos.
 */
export function blindajeObservacionFur(opcion: BlindajeOpcion | null): string | null {
  if (!opcion) return null;
  return opcion === 'DESMONTE'
    ? 'DESMONTE DE BLINDAJE.'
    : `BLINDAJE NIVEL ${opcion.slice(-1)}.`;
}
