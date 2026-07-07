'use client';

import { useCallback, useEffect, useRef, useState } from 'react';
import { tramitesClient } from '@/lib/api/tramites-client';
import type {
  ChecklistView,
  DocumentOcrResult,
  ProcedureAttachment,
  WizardModalidad,
} from '@/lib/api/types/procedure-runtime';

// ── OCR de documentos ────────────────────────────────────────────────
// Tipos que pasan por OCR semántico antes de subir al expediente, por modalidad.
// Matrícula: factura + aduana + impronta + soat. Traspaso: sólo impronta + soat.
// HU #10591 — el traspaso unilateral (paz y salvo, doc. locatario, contrato de
// leasing, declaración de la arrendadora) es documentación jurídica sin OCR: [].
export const OCR_TIPOS: Record<WizardModalidad, readonly string[]> = {
  matricula_inicial: ['factura', 'aduana', 'impronta', 'soat'],
  traspaso: ['impronta', 'soat'],
  traspaso_unilateral: [],
};

/** Límite del OCR (10 MB, el del endpoint). Archivos mayores (≤20 MB) se suben sin analizar. */
export const OCR_MAX_BYTES = 10 * 1024 * 1024;

export function isOcrTipo(modalidad: WizardModalidad, tipo: string): boolean {
  return OCR_TIPOS[modalidad].includes(tipo);
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
 * Aplica las validaciones del frontend sobre el JSON del OCR: validez de tipo
 * (`es_factura_valida` para factura, `es_valido` para el resto) y cruce del VIN del documento
 * (`vehiculo_vin` o `vehiculo_chasis`) con el VIN del trámite. Devuelve si el documento queda rechazado.
 */
export function evaluateOcr(
  data: Record<string, unknown> | null,
  instanceVin: string | null,
): OcrEvaluation {
  if (!data) {
    return { rechazado: true, motivo: 'No se pudieron leer los datos del documento.' };
  }
  const validez = data.es_factura_valida ?? data.es_valido;
  if (validez === false) {
    return { rechazado: true, motivo: 'El documento no pasó la validación de tipo.' };
  }
  const docVin = pickString(data.vehiculo_vin) || pickString(data.vehiculo_chasis);
  const tramite = normalizeVin(instanceVin);
  const documento = normalizeVin(docVin);
  if (tramite && documento && tramite !== documento) {
    return {
      rechazado: true,
      motivo: `El VIN del documento (${docVin}) no coincide con el del trámite.`,
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
  /** tipo del ítem cuya subida está en curso (null = ninguna). */
  uploadingTipo: string | null;
  /** tipo del ítem que se está analizando por OCR (null = ninguno). */
  analyzingTipo: string | null;
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
  uploadingTipo: null,
  analyzingTipo: null,
  deletingId: null,
  ocrResults: {},
  error: null,
};

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

  const refresh = useCallback(async () => {
    if (!instanceId) return;
    setState((s) => ({ ...s, loading: true, error: null }));
    try {
      const [checklist, attachments] = await Promise.all([
        tramitesClient.getChecklist(instanceId, tenantId),
        tramitesClient.getAttachments(instanceId, tenantId),
      ]);
      setState((s) => ({ ...s, checklist, attachments, loading: false }));
    } catch (err) {
      setState((s) => ({
        ...s,
        loading: false,
        error:
          err instanceof Error
            ? err.message
            : 'Error al cargar los documentos',
      }));
    }
  }, [instanceId, tenantId]);

  useEffect(() => {
    void refresh();
  }, [refresh]);

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

      // Re-subir un tipo reinicia su OCR previo: se limpia hasta tener nuevo resultado.
      setState((s) => {
        const rest = { ...s.ocrResults };
        delete rest[tipo];
        return { ...s, error: null, analyzingTipo: null, ocrResults: rest };
      });

      let fileToUpload = file;
      const usaOcr = isOcrTipo(modalidad, tipo);

      if (usaOcr && file.size <= OCR_MAX_BYTES) {
        // 1) OCR antes de subir.
        setState((s) => ({ ...s, analyzingTipo: tipo }));
        let ocr: DocumentOcrResult;
        try {
          ocr = await tramitesClient.analyzeDocument(tipo, file, tenantId);
        } catch (err) {
          // Fallo HTTP del OCR → NO se sube. El operador puede reintentar / adjuntar manualmente.
          setState((s) => ({
            ...s,
            analyzingTipo: null,
            error:
              err instanceof Error ? err.message : 'Error al analizar el documento',
          }));
          return false;
        }

        const evaluation = evaluateOcr(ocr.data, vinRef.current);
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
          analyzingTipo: null,
          ocrResults: { ...s.ocrResults, [tipo]: ocrUi },
        }));
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

      // 2) Subida a S3 (flujo presign → S3 → register existente).
      setState((s) => ({ ...s, uploadingTipo: tipo }));
      try {
        await tramitesClient.uploadAttachment(instanceId, tipo, fileToUpload, tenantId);
        setState((s) => ({ ...s, uploadingTipo: null }));
        await refresh();
        return true;
      } catch (err) {
        setState((s) => ({
          ...s,
          uploadingTipo: null,
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
      setState((s) => ({ ...s, deletingId: attachmentId, error: null }));
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
