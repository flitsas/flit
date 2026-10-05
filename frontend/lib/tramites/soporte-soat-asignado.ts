/**
 * Bug #13194 (P3) — salida del gestor en `asignado` cuando el RUNT no reporta el SOAT.
 *
 * Con la opción «continuar sin SOAT vigente» apagada, «Enviar al OT» responde 409 `soat_no_vigente`
 * y el backend pide cargar el PDF del SOAT. La lectura de ese PDF solo se registra en `asignado` si
 * el adjunto ya está en el trámite (si no, 409 `soporte_soat_requerido`), así que el ORDEN importa y
 * es distinto al del asistente (que persiste el OCR antes de subir):
 *
 *   1. OCR del PDF (`/ocr/soat`) — si falla, el PDF se sube igual (el OCR es ayuda, no compuerta);
 *   2. subir el adjunto `soat` (permitido en `asignado`);
 *   3. registrar la lectura (`ocr-fields`, tipo `soat`: el backend solo toma estado y vencimiento);
 *   4. re-verificar con `soat/validate-runt`, que conserva un soporte manual vigente si el RUNT no lo
 *      reporta. Un 502 aquí es «el RUNT no respondió», no «SOAT no vigente».
 *
 * Devuelve un resultado con el texto para la UI; nunca lanza.
 */
import type {
  DocumentOcrResult,
  ValidateSoatResult,
} from '@/lib/api/types/procedure-runtime';

/** Subconjunto de `tramitesClient` que usa el flujo (inyectable en pruebas). */
export interface SoporteSoatClient {
  analyzeDocument: (tipo: string, file: File, tenantId?: string) => Promise<DocumentOcrResult>;
  uploadAttachment: (instanceId: string, tipo: string, file: File, tenantId?: string) => Promise<unknown>;
  persistOcrFields: (
    instanceId: string,
    tipo: string,
    fields: Record<string, unknown>,
    tenantId?: string,
  ) => Promise<unknown>;
  validateSoatViaRunt: (instanceId: string, tenantId?: string) => Promise<ValidateSoatResult>;
}

export type EstadoSoporteSoat = 'vigente' | 'no_vigente' | 'sin_lectura' | 'runt_no_respondio' | 'error';

export interface ResultadoSoporteSoat {
  estado: EstadoSoporteSoat;
  mensaje: string;
}

const TIPO_SOAT = 'soat';

function errorStatus(err: unknown): number | null {
  const s = err && typeof err === 'object' ? (err as { status?: unknown }).status : undefined;
  return typeof s === 'number' ? s : null;
}

function errorCode(err: unknown): string | null {
  const p = err && typeof err === 'object' ? (err as { problem?: unknown }).problem : undefined;
  const t = p && typeof p === 'object' ? (p as { title?: unknown }).title : undefined;
  return typeof t === 'string' ? t : null;
}

function errorMessage(err: unknown, fallback: string): string {
  return err instanceof Error && err.message.trim() ? err.message : fallback;
}

/** PDF recortado (base64) que devuelve el OCR de un PDF multi-documento → File para subir. */
function base64ToPdf(base64: string, originalName: string): File {
  const binary = atob(base64);
  const bytes = new Uint8Array(binary.length);
  for (let i = 0; i < binary.length; i++) bytes[i] = binary.charCodeAt(i);
  const name = originalName.toLowerCase().endsWith('.pdf')
    ? originalName
    : `${originalName.replace(/\.[^.]+$/, '')}.pdf`;
  return new File([bytes], name, { type: 'application/pdf' });
}

export async function cargarSoporteSoatAsignado(
  client: SoporteSoatClient,
  instanceId: string,
  file: File,
  tenantId?: string,
): Promise<ResultadoSoporteSoat> {
  // 1) OCR — best-effort: su caída no impide subir el documento.
  let ocr: DocumentOcrResult | null = null;
  try {
    ocr = await client.analyzeDocument(TIPO_SOAT, file, tenantId);
  } catch {
    ocr = null;
  }

  // 2) Adjunto primero: sin él, el backend rechaza la lectura con `soporte_soat_requerido`.
  const aSubir = ocr?.extractedPdfBase64 ? base64ToPdf(ocr.extractedPdfBase64, file.name) : file;
  try {
    await client.uploadAttachment(instanceId, TIPO_SOAT, aSubir, tenantId);
  } catch (err) {
    return { estado: 'error', mensaje: errorMessage(err, 'No se pudo cargar el PDF del SOAT. Inténtalo de nuevo.') };
  }

  if (!ocr?.ok || !ocr.data) {
    return {
      estado: 'sin_lectura',
      mensaje:
        'Se cargó el PDF del SOAT, pero no se pudo leer su fecha de vencimiento. Sin esa fecha el soporte no cuenta para el envío: carga un PDF legible del SOAT.',
    };
  }

  // 3) Registrar la lectura (solo estado y vencimiento en `asignado`).
  try {
    await client.persistOcrFields(instanceId, TIPO_SOAT, ocr.data, tenantId);
  } catch (err) {
    if (errorCode(err) === 'soporte_soat_requerido') {
      return {
        estado: 'error',
        mensaje: 'El PDF del SOAT no quedó registrado en el trámite, así que su lectura no se pudo guardar. Vuelve a cargarlo.',
      };
    }
    return { estado: 'error', mensaje: errorMessage(err, 'No se pudo registrar la lectura del SOAT.') };
  }

  // 4) Re-verificar: confirma si el soporte acredita el SOAT para el envío.
  try {
    const r = await client.validateSoatViaRunt(instanceId, tenantId);
    if (r.vigente) {
      return { estado: 'vigente', mensaje: `${r.message} Ya puedes enviar el trámite al OT.` };
    }
    return {
      estado: 'no_vigente',
      mensaje:
        r.soatEstado === 'vencido'
          ? r.message
          : 'El PDF cargado no acredita un SOAT vigente (fecha de vencimiento ilegible o ya vencida). Carga el SOAT vigente del vehículo.',
    };
  } catch (err) {
    if (errorStatus(err) === 502) {
      return {
        estado: 'runt_no_respondio',
        mensaje:
          'El RUNT no respondió al verificar el SOAT. El soporte del SOAT quedó registrado: intenta «Enviar al OT» de nuevo en unos minutos.',
      };
    }
    return { estado: 'error', mensaje: errorMessage(err, 'No se pudo verificar el SOAT.') };
  }
}
