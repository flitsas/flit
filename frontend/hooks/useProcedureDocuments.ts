'use client';

import { useCallback, useEffect, useRef, useState } from 'react';
import { tramitesClient } from '@/lib/api/tramites-client';
import { useRevalidateOnFocus } from './useRevalidateOnFocus';
import type {
  ChecklistView,
  DocumentOcrResult,
  ProcedureAttachment,
  WizardModalidad,
} from '@/lib/api/types/procedure-runtime';

// ── OCR de documentos ────────────────────────────────────────────────
// Tipos que pasan por OCR semántico antes de subir al expediente, por modalidad.
// Matrícula: factura + aduana + impronta + soat. Traspaso: sólo impronta + soat.
// HU #10977 (Feature #10972) — se añade `rtm` en AMBAS modalidades: el certificado de vigencia
// SOAT y RTM pide número, entidad, expedición y vigencia de la revisión, y esos tres últimos no
// los entrega ningún proveedor de consulta. Salen del propio certificado del CDA.
// HU #11996 — se añade `tarjeta_propiedad` (licencia de tránsito) en AMBAS modalidades: es el
// documento más cargado de la plataforma (72.813 cargas medidas en V1) y hasta ahora no tenía
// ninguna validación. Va también en `traspaso` porque `modalidadPorEntrada` mapea a esa modalidad
// TODO lo que entra por placa, familia OTROS incluida — y es justo en otros servicios donde la
// licencia es requisito de entrada (17 asignaciones en `procedure_document_requirements`).
export const OCR_TIPOS: Record<WizardModalidad, readonly string[]> = {
  matricula_inicial: ['factura', 'aduana', 'impronta', 'soat', 'rtm', 'tarjeta_propiedad', 'paz_salvo'],
  traspaso: ['impronta', 'soat', 'rtm', 'tarjeta_propiedad', 'paz_salvo'],
};

/**
 * HU #10975 — tipos cuyo JSON de OCR se PERSISTE en `field_values` además de analizarse.
 * Es un subconjunto de OCR_TIPOS: factura/aduana/impronta se analizan para validar el documento,
 * pero no alimentan ningún certificado. La whitelist real (y la regla de precedencia frente al
 * RUNT) vive en el backend; esto solo evita mandar peticiones que se descartarían.
 */
export const OCR_TIPOS_PERSISTIBLES: readonly string[] = ['soat', 'rtm'];

/** Límite del OCR (10 MB, el del endpoint). Archivos mayores (≤20 MB) se suben sin analizar. */
export const OCR_MAX_BYTES = 10 * 1024 * 1024;

export function isOcrTipo(modalidad: WizardModalidad, tipo: string): boolean {
  const t = tipo.toLowerCase();
  return OCR_TIPOS[modalidad].some((x) => x.toLowerCase() === t);
}

/** Estado OCR por tipo que consume la UI del checklist. */
export type OcrStatus = 'verified' | 'rejected' | 'skipped';

export interface OcrUiResult {
  status: OcrStatus;
  /** Motivo del rechazo, o nota del salto de análisis (skipped). */
  motivo?: string;
  /** JSON extraído (para el resumen por tipo). null cuando no hubo análisis. */
  data: Record<string, unknown> | null;
}

export interface OcrEvaluation {
  rechazado: boolean;
  motivo?: string;
}

function pickString(v: unknown): string {
  return typeof v === 'string' ? v : '';
}

/** Normaliza un VIN/serial para comparar: sólo alfanuméricos, mayúsculas. */
export function normalizeVin(value: string | null | undefined): string {
  return (value ?? '').replace(/[^a-z0-9]/gi, '').toUpperCase();
}

/**
 * Un documento puede amparar VARIOS vehículos: una declaración de importación cubre el lote entero
 * que entró en el contenedor, y el OCR devuelve los VIN separados por comas en un solo campo. Por eso
 * el cruce es por pertenencia y no por igualdad — comparar la cadena completa rechazaría una
 * declaración legítima sólo por traer a los otros 49 vehículos del lote.
 *
 * No se parte por espacios a propósito: `normalizeVin` ya los ignora dentro de un VIN ("VIN 123").
 */
const SEPARADOR_VINS = /[,;/\n]+/;

export function vinsDelDocumento(value: string): string[] {
  return value
    .split(SEPARADOR_VINS)
    .map((v) => normalizeVin(v))
    .filter(Boolean);
}

/** Cuántos VIN se muestran antes de resumir el resto. Un lote de 50 hace ilegible el mensaje. */
const MAX_VINS_VISIBLES = 2;

/** Deja el listado de VIN en algo legible: los primeros y un contador del resto. */
export function resumirVins(value: string): string {
  const vins = value.split(SEPARADOR_VINS).map((v) => v.trim()).filter(Boolean);
  if (vins.length <= MAX_VINS_VISIBLES) return value;
  const resto = vins.length - MAX_VINS_VISIBLES;
  return `${vins.slice(0, MAX_VINS_VISIBLES).join(', ')} y ${resto} más`;
}

/** Motivo del rechazo por tipo, usando lo que el propio OCR haya explicado. */
function motivoDeTipo(data: Record<string, unknown>): string {
  const base = 'El documento no pasó la validación de tipo';
  const nota = pickString(data.observaciones).trim();
  if (nota) return `${base}: ${nota.length > 200 ? `${nota.slice(0, 200)}…` : nota}`;
  const identificado = pickString(data.tipo_documento).trim();
  if (identificado && identificado !== 'otro') {
    return `${base}: el análisis lo identificó como «${identificado.replace(/_/g, ' ')}».`;
  }
  return `${base}: no se reconoció como el documento esperado.`;
}

/**
 * Aplica las validaciones del frontend sobre el JSON del OCR: `ok` de la API (si viene en falso),
 * validez de tipo (`es_factura_valida` / `es_valido`) y cruce del VIN del documento
 * (`vehiculo_vin` o `vehiculo_chasis`) con el VIN del trámite.
 */
export function evaluateOcr(
  data: Record<string, unknown> | null,
  instanceVin: string | null,
  apiOk?: boolean,
): OcrEvaluation {
  if (apiOk === false) {
    if (data) {
      const validez = data.es_factura_valida ?? data.es_valido;
      if (validez === false) return { rechazado: true, motivo: motivoDeTipo(data) };
    }
    return { rechazado: true, motivo: 'El análisis no confirmó el documento.' };
  }
  if (!data) {
    return { rechazado: true, motivo: 'No se pudieron leer los datos del documento.' };
  }
  const validez = data.es_factura_valida ?? data.es_valido;
  if (validez === false) {
    return { rechazado: true, motivo: motivoDeTipo(data) };
  }
  const docVin = pickString(data.vehiculo_vin) || pickString(data.vehiculo_chasis);
  const tramite = normalizeVin(instanceVin);
  const documento = vinsDelDocumento(docVin);
  if (tramite && documento.length > 0 && !documento.includes(tramite)) {
    return {
      rechazado: true,
      motivo: `El VIN del documento (${resumirVins(docVin)}) no coincide con el del trámite.`,
    };
  }
  return { rechazado: false };
}

/** Convierte el PDF recortado (base64) del OCR en un File para subir en vez del original. */
export function base64ToPdfFile(base64: string, originalName: string): File {
  const binary = atob(base64);
  const bytes = new Uint8Array(binary.length);
  for (let i = 0; i < binary.length; i++) bytes[i] = binary.charCodeAt(i);
  const name = originalName.toLowerCase().endsWith('.pdf')
    ? originalName
    : `${originalName.replace(/\.[^.]+$/, '')}.pdf`;
  return new File([bytes], name, { type: 'application/pdf' });
}

// ── Hook ─────────────────────────────────────────────────────────────

export interface ProcedureDocumentsState {
  checklist: ChecklistView | null;
  attachments: ProcedureAttachment[];
  loading: boolean;
  /**
   * Tipos con subida en curso (varios en paralelo). Feature #11066 — evidencia por documento
   * aunque el operador lance otra carga de inmediato.
   */
  uploadingTipos: ReadonlySet<string>;
  /** tipos con OCR en curso (varios en paralelo). */
  analyzingTipos: ReadonlySet<string>;
  /** id del adjunto en proceso de borrado (null = ninguno). */
  deletingId: string | null;
  /** Resultado OCR por tipo (verificado / rechazado / no analizado). Vive sólo en sesión. */
  ocrResults: Record<string, OcrUiResult>;
  error: string | null;
}

const INITIAL_STATE: ProcedureDocumentsState = {
  checklist: null,
  attachments: [],
  loading: false,
  uploadingTipos: new Set(),
  analyzingTipos: new Set(),
  deletingId: null,
  ocrResults: {},
  error: null,
};

function withTipoInSet(set: ReadonlySet<string>, tipo: string): Set<string> {
  const next = new Set(set);
  next.add(tipo);
  return next;
}

function withoutTipoInSet(set: ReadonlySet<string>, tipo: string): Set<string> {
  const next = new Set(set);
  next.delete(tipo);
  return next;
}

function withoutOcrForTipo(
  ocrResults: Record<string, OcrUiResult>,
  tipo: string,
): Record<string, OcrUiResult> {
  const needle = tipo.toLowerCase();
  const next = { ...ocrResults };
  for (const key of Object.keys(next)) {
    if (key.toLowerCase() === needle) delete next[key];
  }
  return next;
}

export function ocrResultForTipo(
  ocrResults: Record<string, OcrUiResult>,
  tipo: string,
): OcrUiResult | undefined {
  if (ocrResults[tipo]) return ocrResults[tipo];
  const needle = tipo.toLowerCase();
  return Object.entries(ocrResults).find(([key]) => key.toLowerCase() === needle)?.[1];
}

export interface UseProcedureDocumentsOptions {
  /** Modalidad del trámite: decide qué tipos pasan por OCR. */
  modalidad?: WizardModalidad;
  tenantId?: string;
}

/**
 * Carga el checklist guiado por la tipología + los adjuntos de la instancia,
 * y expone subir/borrar/refrescar. En los tipos con OCR, analiza el documento
 * ANTES de subirlo a S3: valida tipo y VIN, recorta PDFs multi-documento. Un rechazo del
 * OCR no bloquea el cargue en ninguna modalidad: el documento se sube igual y el rechazo
 * queda marcado en la UI (ocrResults[tipo].status = 'rejected').
 * `instanceId` null deja el hook inerte (la instancia draft aún no existe).
 */
export function useProcedureDocuments(
  instanceId: string | null,
  // Sin default hardcodeado de tenant: lo resuelve `tenantHeader` (tenant activo del `?t=` → JWT).
  { modalidad = 'matricula_inicial', tenantId }: UseProcedureDocumentsOptions = {},
) {
  const [state, setState] = useState<ProcedureDocumentsState>(INITIAL_STATE);

  // VIN del trámite (de field_values) para el cruce con el documento leído por OCR. Se lee una vez por
  // instancia; en la modalidad VIN-first el paso de documentos viene después de consultar/persistir el VIN.
  const vinRef = useRef<string | null>(null);

  // Marca de refresco en vuelo: al volver a la pestaña pueden llegar `focus` y `visibilitychange`
  // casi a la vez, y sin esto se pedirían dos checklists para la misma vuelta.
  const refrescandoRef = useRef(false);

  /**
   * Relee checklist y adjuntos.
   *
   * En segundo plano (`background`) NO toca `loading` ni `error`: se dispara sola al recuperar el
   * foco, y poner la vista en «cargando» o pintar un error mientras el gestor está capturando sería
   * peor que el dato viejo que viene a corregir. Si falla, se conserva lo que ya está en pantalla.
   */
  const refresh = useCallback(
    async (opts?: { background?: boolean }) => {
      if (!instanceId) return;
      if (refrescandoRef.current) return;
      refrescandoRef.current = true;
      if (!opts?.background) setState((s) => ({ ...s, loading: true, error: null }));
      try {
        const [checklist, attachments] = await Promise.all([
          tramitesClient.getChecklist(instanceId, tenantId),
          tramitesClient.getAttachments(instanceId, tenantId),
        ]);
        setState((s) => ({ ...s, checklist, attachments, loading: false }));
      } catch (err) {
        if (opts?.background) return;
        setState((s) => ({
          ...s,
          loading: false,
          error:
            err instanceof Error
              ? err.message
              : 'Error al cargar los documentos',
        }));
      } finally {
        refrescandoRef.current = false;
      }
    },
    [instanceId, tenantId],
  );

  useEffect(() => {
    void refresh();
  }, [refresh]);

  // Un documento dado de alta en Documental mientras esta pantalla estaba abierta no llegaba hasta
  // reabrir el trámite: sin casilla donde cargarlo y sin frenar el paso. Al volver a la pestaña se
  // relee el checklist, que es donde el servidor ya declara el requisito nuevo.
  const revalidarEnFoco = useCallback(() => {
    void refresh({ background: true });
  }, [refresh]);
  useRevalidateOnFocus(revalidarEnFoco, Boolean(instanceId));

  // Lee el VIN de la instancia (best-effort; su fallo no bloquea el checklist).
  useEffect(() => {
    if (!instanceId) {
      vinRef.current = null;
      return;
    }
    let active = true;
    tramitesClient
      .getInstance(instanceId, tenantId)
      .then((detail) => {
        if (active)
          vinRef.current =
            detail?.fieldValues?.find((f) => f.fieldKey === 'vin')?.valueText?.trim() || null;
      })
      .catch(() => {});
    return () => {
      active = false;
    };
  }, [instanceId, tenantId]);

  const upload = useCallback(
    async (tipo: string, file: File) => {
      if (!instanceId) return false;

      let fileToUpload = file;
      const usaOcr = isOcrTipo(modalidad, tipo);
      const analizaAhora = usaOcr && file.size <= OCR_MAX_BYTES;

      // Reemplazo: la marca es del archivo anterior. Se apaga al instante (no al terminar el OCR)
      // para no dejar el rechazo viejo mientras se analiza el nuevo.
      setState((s) => ({
        ...s,
        error: null,
        ocrResults: withoutOcrForTipo(s.ocrResults, tipo),
        analyzingTipos: analizaAhora ? withTipoInSet(s.analyzingTipos, tipo) : s.analyzingTipos,
      }));

      if (analizaAhora) {
        // HU #11996 — el OCR NUNCA impide cargar. Antes, un fallo HTTP aquí hacía `return false` y el
        // documento no se subía: como el proveedor devuelve 503 ante cualquier problema suyo (sin key,
        // caído, respuesta inválida), una caída de la IA dejaba al operador sin poder adjuntar NADA de
        // los tipos con OCR. Ahora se degrada igual que un archivo de 10–20 MB: sube y queda "no
        // analizado". El análisis es una ayuda, no una compuerta.
        let ocr: DocumentOcrResult | null = null;
        try {
          ocr = await tramitesClient.analyzeDocument(tipo.toLowerCase(), file, tenantId);
        } catch {
          setState((s) => ({
            ...s,
            analyzingTipos: withoutTipoInSet(s.analyzingTipos, tipo),
            ocrResults: {
              ...s.ocrResults,
              [tipo]: {
                status: 'skipped',
                motivo: 'No se pudo analizar el documento automáticamente: se subió sin verificar.',
                data: null,
              },
            },
          }));
        }

        if (ocr) {
        const evaluation = evaluateOcr(ocr.data, vinRef.current, ocr.ok);
        const ocrUi: OcrUiResult = {
          status: evaluation.rechazado ? 'rejected' : 'verified',
          motivo: evaluation.motivo,
          data: ocr.data,
        };

        // PDF multi-documento → se sube el recorte, no el original.
        if (ocr.extractedPdfBase64) {
          fileToUpload = base64ToPdfFile(ocr.extractedPdfBase64, file.name);
        }

        // Ambas modalidades suben igual aunque el OCR quede rechazado: el rechazo no bloquea
        // el cargue, solo queda marcado en la UI (ocrResults[tipo].status = 'rejected').
        setState((s) => ({
          ...s,
          analyzingTipos: withoutTipoInSet(s.analyzingTipos, tipo),
          ocrResults: { ...s.ocrResults, [tipo]: ocrUi },
        }));

        // HU #10975 — persistir en field_values lo que el OCR extrajo, para que el certificado de
        // vigencia SOAT y RTM deje de salir con celdas en blanco. Solo con el documento VERIFICADO:
        // de un documento rechazado (tipo equivocado o VIN que no cruza) no se toma ningún dato.
        // Best-effort deliberado: si esta llamada falla, el adjunto ya se sube igual — el documento
        // es el entregable y los field_values son un enriquecimiento del certificado, así que un
        // fallo aquí no puede costarle al operador el cargue que ya hizo.
        if (!evaluation.rechazado && ocr.data && OCR_TIPOS_PERSISTIBLES.some((x) => x.toLowerCase() === tipo.toLowerCase())) {
          try {
            await tramitesClient.persistOcrFields(instanceId, tipo.toLowerCase(), ocr.data, tenantId);
          } catch {
            // Silencio intencionado: ver comentario de arriba.
          }
        }
        }
      } else if (usaOcr && file.size > OCR_MAX_BYTES) {
        // 10–20 MB: se salta el OCR (excede el límite del análisis), se sube igual y se marca "no analizado".
        setState((s) => ({
          ...s,
          ocrResults: {
            ...s.ocrResults,
            [tipo]: {
              status: 'skipped',
              motivo: 'Archivo mayor a 10 MB: subido sin análisis automático.',
              data: null,
            },
          },
        }));
      }

      // 2) Subida a S3 (flujo presign → S3 → register existente). Varios tipos en paralelo.
      setState((s) => ({
        ...s,
        uploadingTipos: withTipoInSet(s.uploadingTipos, tipo),
      }));
      try {
        await tramitesClient.uploadAttachment(instanceId, tipo, fileToUpload, tenantId);
        setState((s) => ({
          ...s,
          uploadingTipos: withoutTipoInSet(s.uploadingTipos, tipo),
        }));
        await refresh();
        return true;
      } catch (err) {
        setState((s) => ({
          ...s,
          uploadingTipos: withoutTipoInSet(s.uploadingTipos, tipo),
          error:
            err instanceof Error ? err.message : 'Error al subir el documento',
        }));
        return false;
      }
    },
    [instanceId, tenantId, modalidad, refresh],
  );

  const remove = useCallback(
    async (attachmentId: string) => {
      if (!instanceId) return false;
      setState((s) => {
        const att = s.attachments.find((a) => a.id === attachmentId);
        return {
          ...s,
          deletingId: attachmentId,
          error: null,
          ocrResults: att ? withoutOcrForTipo(s.ocrResults, att.tipo) : s.ocrResults,
        };
      });
      try {
        await tramitesClient.deleteAttachment(
          instanceId,
          attachmentId,
          tenantId,
        );
        setState((s) => ({ ...s, deletingId: null }));
        await refresh();
        return true;
      } catch (err) {
        setState((s) => ({
          ...s,
          deletingId: null,
          error:
            err instanceof Error
              ? err.message
              : 'Error al borrar el documento',
        }));
        return false;
      }
    },
    [instanceId, tenantId, refresh],
  );

  const clearError = useCallback(() => {
    setState((s) => ({ ...s, error: null }));
  }, []);

  return { state, refresh, upload, remove, clearError };
}
