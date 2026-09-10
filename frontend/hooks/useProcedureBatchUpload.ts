'use client';

import { useCallback, useMemo, useState } from 'react';
import { tramitesClient } from '@/lib/api/tramites-client';
import {
  cargarTiposOcr,
  OCR_TIPOS_PERSISTIBLES,
  evaluateOcr,
  type OcrEvaluation,
} from './useProcedureDocuments';
import type {
  BatchOcrFileError,
  BatchOcrPiece,
  BatchOcrUnrecognized,
  ChecklistItemView,
  ProcedureAttachment,
  WizardModalidad,
} from '@/lib/api/types/procedure-runtime';

// ── Topes del lote ───────────────────────────────────────────────────────────
// Espejo de AnalyzeBatchHandler: se validan también aquí para que el operador vea el error al soltar
// los archivos y no después de esperar la subida. El backend sigue siendo la autoridad.

export const BATCH_MAX_FILES = 20;
export const BATCH_MAX_TOTAL_BYTES = 100 * 1024 * 1024;
export const BATCH_MAX_FILE_BYTES = 32 * 1024 * 1024;

/** Extensiones que el lote sabe leer. WEBP se admite como adjunto pero el modelo de visión no lo lee. */
export const BATCH_ACCEPT = '.pdf,.jpg,.jpeg,.png,.zip';

/**
 * Fases del cargue masivo. `reviewing` es la que importa: nada llega a S3 mientras el operador no
 * confirme, de modo que un error de clasificación no ensucia el expediente.
 */
export type BatchPhase = 'idle' | 'analyzing' | 'reviewing' | 'uploading';

/** Qué hacer con una pieza propuesta. */
export type BatchDecision = 'accept' | 'skip';

/** Una pieza propuesta, ya cruzada con el estado real del expediente. */
export interface BatchReviewItem {
  /** Identidad estable dentro del lote (no viene del backend). */
  id: string;
  piece: BatchOcrPiece;
  /** Validez de tipo + cruce de VIN, con las MISMAS reglas del cargue campo a campo. */
  evaluation: OcrEvaluation;
  /** Adjunto que se reemplazaría al confirmar; null si el campo está vacío. */
  conflicto: ProcedureAttachment | null;
  /** Otra pieza del mismo lote, con mejor confianza, ya reclama este tipo. */
  duplicado: boolean;
  decision: BatchDecision;
}

export interface BatchReviewState {
  phase: BatchPhase;
  items: BatchReviewItem[];
  noReconocidos: BatchOcrUnrecognized[];
  errores: BatchOcrFileError[];
  /** Archivos originales del lote: los «no reconocidos» se resuelven cargándolos a mano. */
  archivos: File[];
  /**
   * Avance del análisis, para que la espera diga por dónde va en vez de ser una barra ciega. Sólo
   * tiene sentido en `analyzing`: el lote se manda un archivo por petición, así que el operador puede
   * saber cuántos van. null fuera de esa fase.
   */
  progreso: { hechos: number; total: number } | null;
  /** Error del lote completo (no de un archivo suelto). */
  error: string | null;
}

const INITIAL_STATE: BatchReviewState = {
  phase: 'idle',
  items: [],
  noReconocidos: [],
  errores: [],
  archivos: [],
  progreso: null,
  error: null,
};

/** Identidad de una pieza dentro del lote: archivo + tipo + páginas la distinguen sin ambigüedad. */
function pieceId(piece: BatchOcrPiece): string {
  return `${piece.sourceFilename}#${piece.tipo}#${piece.paginas.join('-')}`;
}

/** Reconstruye el binario de una pieza para subirlo por el flujo de adjuntos de siempre. */
export function base64ToFile(base64: string, filename: string, mimetype: string): File {
  const binary = atob(base64);
  const bytes = new Uint8Array(binary.length);
  for (let i = 0; i < binary.length; i++) bytes[i] = binary.charCodeAt(i);
  return new File([bytes], filename, { type: mimetype });
}

/**
 * Tipos que el lote puede repartir: los que tienen OCR Y que el checklist realmente muestra. El
 * checklist es configurable por tenant, así que proponer un tipo oculto sería ofrecerle al operador un
 * campo que no existe.
 *
 * HU #12034 — `tiposOcr` lo resuelve el backend y se inyecta aquí para que la función siga siendo pura
 * y testeable. Con `null` —no se pudo consultar— se proponen TODOS los visibles: el backend descarta
 * por su cuenta los que no tengan prompt, y es preferible eso a repartir de menos en silencio.
 */
export function tiposDelLote(
  tiposOcr: ReadonlySet<string> | null,
  items: readonly ChecklistItemView[],
): string[] {
  const visibles = items.map((i) => i.docTipo ?? i.key);
  return tiposOcr === null ? [...visibles] : visibles.filter((t) => tiposOcr.has(t.toLowerCase()));
}

/** Valida los archivos antes de gastar una llamada. Devuelve el error, o null si el lote es aceptable. */
export function validateBatch(files: readonly File[]): string | null {
  if (files.length === 0) return 'Selecciona al menos un archivo.';
  if (files.length > BATCH_MAX_FILES)
    return `Máximo ${BATCH_MAX_FILES} archivos por carga. Seleccionaste ${files.length}.`;

  const grande = files.find((f) => f.size > BATCH_MAX_FILE_BYTES);
  if (grande)
    return `«${grande.name}» supera el máximo de ${BATCH_MAX_FILE_BYTES / (1024 * 1024)} MB por archivo.`;

  const total = files.reduce((acc, f) => acc + f.size, 0);
  if (total > BATCH_MAX_TOTAL_BYTES)
    return `La carga supera el máximo de ${BATCH_MAX_TOTAL_BYTES / (1024 * 1024)} MB en total.`;

  return null;
}

/**
 * Cruza las piezas que propone el backend con el estado real del expediente y decide qué queda marcado
 * de entrada. La regla es no pisar nada sin permiso: se marca sólo lo que llena un campo vacío y pasa
 * la validación. Todo lo demás llega desmarcado y visible, para que el operador lo active a conciencia.
 */
export function buildReviewItems(
  piezas: readonly BatchOcrPiece[],
  attachments: readonly ProcedureAttachment[],
  instanceVin: string | null,
): BatchReviewItem[] {
  const yaAdjunto = new Map<string, ProcedureAttachment>();
  for (const a of attachments) {
    if (!yaAdjunto.has(a.tipo)) yaAdjunto.set(a.tipo, a);
  }

  // Cuando el lote trae dos documentos del mismo tipo (dos facturas), gana el de mejor confianza y el
  // resto queda desmarcado: son alternativas entre las que el operador elige, no cosas que sumar.
  const mejorPorTipo = new Map<string, number>();
  for (const p of piezas) {
    const actual = mejorPorTipo.get(p.tipo);
    if (actual === undefined || p.confianza > actual) mejorPorTipo.set(p.tipo, p.confianza);
  }
  const primerGanador = new Set<string>();

  return piezas.map((piece) => {
    const evaluation = piece.data
      ? evaluateOcr(piece.data, instanceVin)
      : {
          rechazado: true,
          motivo: piece.analisisError ?? 'No se pudieron leer los datos del documento.',
        };

    const conflicto = yaAdjunto.get(piece.tipo) ?? null;

    const esGanador =
      piece.confianza === mejorPorTipo.get(piece.tipo) && !primerGanador.has(piece.tipo);
    if (esGanador) primerGanador.add(piece.tipo);
    const duplicado = !esGanador;

    return {
      id: pieceId(piece),
      piece,
      evaluation,
      conflicto,
      duplicado,
      decision:
        !conflicto && !duplicado && !evaluation.rechazado ? 'accept' : 'skip',
    };
  });
}

export interface UseProcedureBatchUploadOptions {
  modalidad?: WizardModalidad;
  tenantId?: string;
}

/**
 * Cargue masivo de documentos. Manda el lote al análisis, cruza el resultado con el expediente y deja
 * al operador una lista revisable. No sube nada por su cuenta: subir es una acción explícita que llega
 * en el paso siguiente del flujo.
 *
 * Convive con `useProcedureDocuments` sin tocarlo — el cargue campo a campo sigue exactamente igual y
 * este es un camino adicional, no un reemplazo.
 */
export function useProcedureBatchUpload(
  instanceId: string | null,
  { modalidad = 'matricula_inicial', tenantId }: UseProcedureBatchUploadOptions = {},
) {
  const [state, setState] = useState<BatchReviewState>(INITIAL_STATE);

  const analyze = useCallback(
    async (
      files: File[],
      checklistItems: readonly ChecklistItemView[],
      attachments: readonly ProcedureAttachment[],
    ) => {
      if (!instanceId) return false;

      const invalido = validateBatch(files);
      if (invalido) {
        setState((s) => ({ ...s, error: invalido }));
        return false;
      }

      const tipos = tiposDelLote(await cargarTiposOcr(), checklistItems);
      if (tipos.length === 0) {
        setState((s) => ({
          ...s,
          error: 'Este trámite no tiene documentos que se puedan repartir automáticamente.',
        }));
        return false;
      }

      setState({
        ...INITIAL_STATE,
        phase: 'analyzing',
        archivos: files,
        progreso: { hechos: 0, total: files.length },
      });

      // El VIN del trámite alimenta el mismo cruce que hace el cargue campo a campo. Best-effort: si no
      // se puede leer, las piezas se evalúan sin ese contraste en vez de bloquear la revisión entera.
      let vin: string | null = null;
      try {
        const detail = await tramitesClient.getInstance(instanceId, tenantId);
        vin = detail?.fieldValues?.find((f) => f.fieldKey === 'vin')?.valueText?.trim() || null;
      } catch {
        // Silencio intencionado: ver comentario de arriba.
      }

      // Un archivo por petición, en serie. Mandar el lote entero en una sola petición hacía que un
      // lote de 4 expedientes acumulara ~90 s de silencio y el proxy lo cortara con un 504, perdiendo
      // TODO el lote. Trocear no cambia el trabajo ni el orden —el backend ya procesaba en serie por
      // el límite de tasa del proveedor—, pero ninguna petición individual se acerca ya a ese corte, y
      // un archivo que falle deja de llevarse por delante a los demás.
      const piezas: BatchOcrPiece[] = [];
      const noReconocidos: BatchOcrUnrecognized[] = [];
      const errores: BatchOcrFileError[] = [];
      let algunoRespondio = false;
      let hechos = 0;

      for (const file of files) {
        try {
          const result = await tramitesClient.analyzeBatch(tipos, [file], tenantId);
          piezas.push(...result.piezas);
          noReconocidos.push(...result.noReconocidos);
          errores.push(...result.errores);
          algunoRespondio = true;
        } catch (err) {
          // El fallo de un archivo es información para el operador, no el fin del lote: baja a la
          // misma lista de errores por archivo que ya pinta la pantalla de revisión.
          errores.push({
            filename: file.name,
            motivo: err instanceof Error ? err.message : 'No se pudo analizar el archivo.',
          });
        }

        hechos++;
        setState((s) => ({ ...s, progreso: { hechos, total: files.length } }));
      }

      if (!algunoRespondio) {
        setState((s) => ({
          ...s,
          phase: 'idle',
          items: [],
          progreso: null,
          error: errores[0]?.motivo ?? 'No se pudo analizar la carga.',
        }));
        return false;
      }

      // buildReviewItems se calcula sobre TODAS las piezas del lote, nunca por archivo: la regla
      // «gana la de mejor confianza por tipo» es del lote entero, y aplicarla por trozo marcaría la
      // primera factura que llegue aunque la del último archivo sea mejor.
      setState({
        phase: 'reviewing',
        items: buildReviewItems(piezas, attachments, vin),
        noReconocidos,
        errores,
        archivos: files,
        progreso: null,
        error: null,
      });
      return true;
    },
    [instanceId, tenantId, modalidad],
  );

  /** Marca o desmarca una pieza. Al marcar una, se desmarcan las demás del mismo tipo. */
  const setDecision = useCallback((id: string, decision: BatchDecision) => {
    setState((s) => {
      const objetivo = s.items.find((i) => i.id === id);
      if (!objetivo) return s;
      return {
        ...s,
        items: s.items.map((item) => {
          if (item.id === id) return { ...item, decision };
          // Un tipo sólo puede recibir un documento: marcar uno libera el campo del otro.
          if (decision === 'accept' && item.piece.tipo === objetivo.piece.tipo) {
            return { ...item, decision: 'skip' as BatchDecision };
          }
          return item;
        }),
      };
    });
  }, []);

  /**
   * Adjunta las piezas marcadas por el flujo presign→S3→register de siempre. Secuencial a propósito:
   * son pocas y así un fallo se atribuye a su pieza sin ambigüedad.
   *
   * Si el operador marcó una pieza que pisaba un adjunto existente, se borra el anterior primero —
   * es lo que dice la advertencia que aceptó al marcarla.
   *
   * Devuelve true si todo entró. Si algo falla, las que sí entraron desaparecen de la lista y las
   * fallidas quedan con su motivo, de modo que reintentar no duplica adjuntos.
   */
  const confirm = useCallback(async (): Promise<boolean> => {
    if (!instanceId) return false;

    const seleccionadas = state.items.filter((i) => i.decision === 'accept');
    if (seleccionadas.length === 0) return false;

    setState((s) => ({ ...s, phase: 'uploading', error: null }));

    const subidas: string[] = [];
    const fallos: BatchOcrFileError[] = [];

    for (const item of seleccionadas) {
      const { piece } = item;
      try {
        if (item.conflicto) {
          await tramitesClient.deleteAttachment(instanceId, item.conflicto.id, tenantId);
        }

        await tramitesClient.uploadAttachment(
          instanceId,
          piece.tipo,
          base64ToFile(piece.contentBase64, piece.filename, piece.mimetype),
          tenantId,
        );
        subidas.push(item.id);

        // Mismo enriquecimiento que el cargue campo a campo: sólo de documentos verificados, y
        // best-effort — el adjunto ya está y un fallo aquí no puede costarle al operador el cargue.
        if (
          !item.evaluation.rechazado &&
          piece.data &&
          OCR_TIPOS_PERSISTIBLES.includes(piece.tipo)
        ) {
          try {
            await tramitesClient.persistOcrFields(instanceId, piece.tipo, piece.data, tenantId);
          } catch {
            // Silencio intencionado: ver comentario de arriba.
          }
        }
      } catch (err) {
        fallos.push({
          filename: piece.filename,
          motivo: err instanceof Error ? err.message : 'No se pudo adjuntar.',
        });
      }
    }

    if (fallos.length === 0) {
      setState(INITIAL_STATE);
      return true;
    }

    setState((s) => ({
      ...s,
      phase: 'reviewing',
      items: s.items.filter((i) => !subidas.includes(i.id)),
      errores: [...s.errores, ...fallos],
    }));
    return false;
  }, [instanceId, tenantId, state.items]);

  /** Descarta la revisión sin subir nada. */
  const reset = useCallback(() => setState(INITIAL_STATE), []);

  const clearError = useCallback(() => setState((s) => ({ ...s, error: null })), []);

  const aceptadas = useMemo(
    () => state.items.filter((i) => i.decision === 'accept'),
    [state.items],
  );

  return { state, aceptadas, analyze, setDecision, confirm, reset, clearError };
}
