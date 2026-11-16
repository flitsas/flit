// Capturas del cliente: viven SOLO en memoria del flujo hasta el envío final (HU #13294/#13295).
import type { ManualCaptureError, ManualSubmitFiles } from "./types";
import type { FotoKind, ResultadoFoto } from "./validacionFotos";

export type CaptureKey = keyof ManualSubmitFiles;
export type Captures = Partial<ManualSubmitFiles>;

/** Índice del paso (STEPS) de cada captura. */
export const CAPTURE_STEP_INDEX: Record<CaptureKey, number> = { rostro: 1, anverso: 2, reverso: 3, firma: 4 };

export const STEP_INDEX_DATOS = 0;

export function isComplete(c: Captures): c is ManualSubmitFiles {
  return !!(c.rostro && c.anverso && c.reverso && c.firma);
}

/**
 * Paso a repetir ante un 413/415. SUPUESTO: el contrato no indica qué archivo falló; si el código o el
 * mensaje del backend nombra la parte (rostro|anverso|reverso|firma) se vuelve a ese paso; si no, null.
 */
export function offendingCapture(error: ManualCaptureError): CaptureKey | null {
  const text = `${error.code} ${error.message}`.toLowerCase();
  const keys: CaptureKey[] = ["rostro", "anverso", "reverso", "firma"];
  return keys.find((k) => text.includes(k)) ?? null;
}

/**
 * Estado de validación de cada foto. Vive en el Flow (no en el visor) para que, al volver con «Atrás», el
 * contador de intentos fallidos y el resultado de la foto ya confirmada se conserven.
 */
export interface EstadoFoto {
  /** Resultado de la foto confirmada; sin él, en el envío queda «no verificada / sin_resultado». */
  resultado?: ResultadoFoto;
  /** Intentos fallidos acumulados en ese lado. */
  fallos: number;
}
export type Fotos = Partial<Record<FotoKind, EstadoFoto>>;
