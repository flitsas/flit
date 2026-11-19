/**
 * Bug #14604 — vigente vs. «versión anterior».
 *
 * Cuando un administrador carga un documento de un tipo que ya tenía uno, el anterior queda como
 * histórico (`isHistorico: true`). Un histórico reemplazado NO cuenta como el documento de la casilla
 * (ni para el checklist ni para el veredicto OCR), pero sigue visible y descargable. `isHistorico` es
 * opcional para tolerar backends que aún no lo envían: ausente = vigente.
 *
 * Regla «efectivos» (espejo de `ProcedureInstanceAttachmentExtensions.Efectivos` del backend): los
 * vigentes, MÁS los históricos cuyo tipo no tiene ningún vigente. La revocatoria (HU #12166) marca
 * TODOS los adjuntos como históricos sin reemplazarlos: con un filtro estricto el expediente de un
 * trámite revocado quedaría vacío y todo saldría como «versión anterior».
 */
type ConVigencia = { tipo?: string | null; isHistorico?: boolean };

/** ¿El adjunto NO está marcado como histórico? (ojo: no considera la regla «efectivos»). */
export function esVigente(a: object): boolean {
  return (a as ConVigencia).isHistorico !== true;
}

const claveTipo = (a: object): string => ((a as ConVigencia).tipo ?? '').trim().toLowerCase();

/** Adjuntos efectivos: vigentes + históricos que ningún vigente del mismo tipo reemplaza. */
export function soloVigentes<T extends object>(attachments: readonly T[]): T[] {
  if (!attachments.some((a) => !esVigente(a))) return [...attachments];
  const tiposConVigente = new Set(attachments.filter(esVigente).map(claveTipo));
  return attachments.filter((a) => esVigente(a) || !tiposConVigente.has(claveTipo(a)));
}

/** Versiones anteriores: históricos reemplazados por un vigente del mismo tipo. */
export function soloHistoricos<T extends object>(attachments: readonly T[]): T[] {
  const efectivos = new Set(soloVigentes(attachments));
  return attachments.filter((a) => !efectivos.has(a));
}
