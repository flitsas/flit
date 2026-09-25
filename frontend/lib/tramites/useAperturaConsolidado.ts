'use client';

import { useCallback, useEffect, useRef, useState } from 'react';
import { tramitesClient } from '@/lib/api/tramites-client';
import { openLoadingDocumentTab } from '@/lib/documents/open-document-tab';
import type {
  ConsolidadoVigencia,
  GenerarConsolidadoResult,
  ProcedureAttachment,
} from '@/lib/api/types/procedure-runtime';
import { detectarFalloRegeneracion } from '@/lib/tramites/fallo-regeneracion-consolidado';
import { mensajeErrorConsolidadoAmigable } from '@/lib/tramites/errores-consolidado';
import { esDocumentoDefinitivo } from '@/lib/tramites/consolidado-entrega';

/**
 * HU #12800 (Épica #12760) — apertura NO bloqueante del expediente consolidado en el visor.
 *
 * Antes, «Ver expediente consolidado (PDF)» esperaba en silencio la petición de generación: si el
 * consolidado estaba desactualizado, el gestor veía un botón gris durante toda la reconstrucción y
 * no sabía si la aplicación seguía viva. Este hook saca esa espera del componente:
 *
 * - Vigente (o vigencia desconocida): apertura directa, sin panel de reconstrucción.
 * - Desactualizado / inexistente: fase `reconstruyendo` DE INMEDIATO (AC1). La petición va en
 *   segundo plano; nada se deshabilita salvo las dos acciones del propio consolidado (AC2).
 * - Al terminar con éxito se carga el PDF nuevo en la pestaña que se abrió con el clic (AC3).
 * - Si vence `timeoutMs`, se muestra el PDF anterior (si existe) con la advertencia de que la
 *   actualización sigue en proceso; la petición sigue en vuelo y, si termina, carga el PDF nuevo (AC4).
 *
 * Ruta (code-review M2 de la Épica): la apertura usa la ruta ÚNICA de entrega (#12785),
 * `GET …/consolidado/entrega?tipo=consolidado`, SIN `force`. El backend reconstruye solo si la
 * bandera de vigencia está abajo y, en estado final, sirve el definitivo (antes el POST de
 * generación respondía 409 `generacion_bloqueada_estado_final` en un trámite aprobado). La acción
 * explícita «Re-generar» sigue siendo el POST `generarConsolidado(…, force=true)` del visor.
 *
 * Cancelación: un `AbortController` por apertura. Desmontar el visor o cambiar de trámite lo aborta:
 * el resultado tardío se descarta (no abre pestañas ni toca el estado de un visor que ya no existe).
 * `tramitesClient.entregarConsolidado` no acepta `signal`, así que la petición HTTP no se corta: el
 * backend termina la reconstrucción y el PDF queda vigente para la siguiente apertura.
 *
 * El resultado de la entrega (`resultado`, `avisos`, `regenerado`) queda expuesto para la HU #12799.
 */

/** Espera máxima antes de servir el PDF anterior (AC4). Configurable por parámetro del hook. */
export const TIMEOUT_RECONSTRUCCION_MS = 30_000;

export const COPY_RECONSTRUCCION_EN_CURSO =
  'Reconstrucción en curso: estamos actualizando el expediente consolidado con los últimos cambios. Puedes seguir trabajando en el trámite mientras tanto.';
export const COPY_TIMEOUT_CON_ANTERIOR =
  'La actualización del expediente sigue en proceso. Se abrió la versión anterior del consolidado; cuando termine, se cargará la nueva versión automáticamente.';
export const COPY_TIMEOUT_SIN_ANTERIOR =
  'La actualización del expediente sigue en proceso y está tardando más de lo habitual. El PDF se abrirá automáticamente cuando termine.';
export const COPY_ACTUALIZADO =
  'El expediente consolidado se actualizó y se cargó la nueva versión.';

export type FaseApertura =
  /** Sin apertura en curso. */
  | 'inactivo'
  /** Apertura directa (PDF vigente): sin panel de reconstrucción. */
  | 'abriendo'
  /** El PDF estaba desactualizado: reconstrucción en segundo plano con indicador de progreso. */
  | 'reconstruyendo'
  /** Venció la espera: se sirvió el anterior (o nada) y la reconstrucción sigue en vuelo. */
  | 'timeout'
  /** La reconstrucción terminó después del timeout y el PDF nuevo ya se cargó. */
  | 'actualizado';

export type AbrirAdjuntoFn = (
  instanceId: string,
  attachment: Pick<ProcedureAttachment, 'id' | 'tipo' | 'filename' | 'mimetype'>,
  existingWin?: Window | null,
) => Promise<void>;

export interface UseAperturaConsolidadoOptions {
  instanceId: string | null;
  /** `ProcedureInstanceDetail.consolidadoWizard` (HU #12791). `undefined`/`null` = desconocida. */
  vigencia?: ConsolidadoVigencia | null;
  /** Consolidado anterior conocido (lista de adjuntos): lo que se abre si vence la espera. */
  consolidadoPrevio?: Pick<ProcedureAttachment, 'id' | 'tipo' | 'filename' | 'mimetype'> | null;
  /** Abre un adjunto en pestaña (reutilizando `existingWin` si se pasa). */
  abrirAdjunto: AbrirAdjuntoFn;
  onBeforeGenerate?: () => Promise<void>;
  /** Se invoca cuando la entrega terminó con éxito (avisos + refrescar adjuntos del padre). */
  onEntregado?: (resultado: GenerarConsolidadoResult | null) => void;
  timeoutMs?: number;
}

export interface UseAperturaConsolidadoResult {
  fase: FaseApertura;
  /** `true` mientras la petición de entrega está en vuelo (incluye la fase `timeout`). */
  enVuelo: boolean;
  /** Mensaje de error de la última apertura; `null` si no hubo. */
  error: string | null;
  /** Última respuesta de la entrega (para avisos de la HU #12799). */
  resultado: GenerarConsolidadoResult | null;
  /** `true` si la última entrega sirvió el PDF DEFINITIVO del trámite (estado final, AC3 #12785). */
  definitivo: boolean;
  avisos: string[];
  regenerado: boolean | null;
  /** `true` si en el timeout se abrió el PDF anterior. */
  sirvioAnterior: boolean;
  abrir: () => Promise<void>;
  /**
   * Candado síncrono compartido con «Re-generar» (HU #12788): una acción del gestor = una única
   * petición. `tomarCandado()` devuelve `false` si ya hay una petición en vuelo.
   */
  tomarCandado: () => boolean;
  liberarCandado: () => void;
  /** Aborta la apertura en curso (descarta el resultado tardío). */
  cancelar: () => void;
  limpiarError: () => void;
}

/** `true` si abrir el visor implica reconstruir el PDF (AC1). Vigencia desconocida → no. */
export function requiereReconstruccion(vigencia: ConsolidadoVigencia | null | undefined): boolean {
  if (!vigencia || vigencia.definitivo) return false;
  return vigencia.estado === 'desactualizado' || vigencia.estado === 'inexistente';
}

/** `attachmentId`/`filename` del consolidado devuelto (respuesta anidada o plana de mocks antiguos). */
export function consolidadoDeResultado(
  generado: GenerarConsolidadoResult | null | undefined,
): { id: string; filename: string } | null {
  const nested = generado?.document;
  if (nested?.attachmentId) {
    return { id: nested.attachmentId, filename: nested.filename || 'consolidado.pdf' };
  }
  const flat = generado as { attachmentId?: string; filename?: string } | null | undefined;
  if (flat?.attachmentId) {
    return { id: flat.attachmentId, filename: flat.filename || 'consolidado.pdf' };
  }
  return null;
}

/**
 * Mensaje de error de la apertura / «Re-generar». Security B2 (Épica #12760): el código o `detail`
 * del backend NUNCA se pinta crudo; se traduce con el mapa compartido y, si no hay código conocido,
 * cae en este respaldo.
 */
export const COPY_ERROR_CONSOLIDADO_GENERICO =
  'No se pudo generar el consolidado. Revisa la conexión e inténtalo de nuevo.';

export function mensajeErrorConsolidado(err: unknown): string {
  return mensajeErrorConsolidadoAmigable(err, COPY_ERROR_CONSOLIDADO_GENERICO);
}

export function useAperturaConsolidado({
  instanceId,
  vigencia,
  consolidadoPrevio,
  abrirAdjunto,
  onBeforeGenerate,
  onEntregado,
  timeoutMs = TIMEOUT_RECONSTRUCCION_MS,
}: UseAperturaConsolidadoOptions): UseAperturaConsolidadoResult {
  const [fase, setFase] = useState<FaseApertura>('inactivo');
  const [enVuelo, setEnVuelo] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [resultado, setResultado] = useState<GenerarConsolidadoResult | null>(null);
  const [sirvioAnterior, setSirvioAnterior] = useState(false);
  // Tras una entrega exitosa el PDF queda vigente aunque el padre no haya recargado el detalle:
  // la siguiente apertura es directa. Se reinicia cuando llega una vigencia nueva del backend.
  const [entregadoTrasVigencia, setEntregadoTrasVigencia] = useState(false);
  // Se compara por VALOR (no por identidad): el padre puede reconstruir el objeto en cada render.
  const claveVigencia = vigencia
    ? `${vigencia.estado}|${vigencia.generadoEn ?? ''}|${vigencia.definitivo}`
    : '';
  const [vigenciaVista, setVigenciaVista] = useState(claveVigencia);
  if (vigenciaVista !== claveVigencia) {
    setVigenciaVista(claveVigencia);
    setEntregadoTrasVigencia(false);
  }
  // Cambio de trámite: el estado de la apertura anterior no pertenece a este (el efecto de abajo
  // aborta la petición; aquí se limpia lo que se pinta).
  const [instanciaVista, setInstanciaVista] = useState(instanceId);
  if (instanciaVista !== instanceId) {
    setInstanciaVista(instanceId);
    setFase('inactivo');
    setEnVuelo(false);
    setError(null);
    setResultado(null);
    setSirvioAnterior(false);
    setEntregadoTrasVigencia(false);
  }

  const lock = useRef(false);
  const controllerRef = useRef<AbortController | null>(null);
  const timerRef = useRef<ReturnType<typeof setTimeout> | null>(null);
  // Pestaña abierta con el clic que todavía muestra el loader (sin PDF): se cierra si se aborta.
  const winPendienteRef = useRef<Window | null>(null);

  const limpiarTimer = () => {
    if (timerRef.current !== null) {
      clearTimeout(timerRef.current);
      timerRef.current = null;
    }
  };

  /** Aborta la apertura en curso. Devuelve `true` si había una. No toca el estado de React. */
  const abortarEnCurso = useCallback((): boolean => {
    if (timerRef.current !== null) {
      clearTimeout(timerRef.current);
      timerRef.current = null;
    }
    const pendiente = winPendienteRef.current;
    winPendienteRef.current = null;
    if (pendiente && !pendiente.closed) pendiente.close();
    const controller = controllerRef.current;
    if (!controller) return false;
    controller.abort();
    controllerRef.current = null;
    lock.current = false;
    return true;
  }, []);

  const cancelar = useCallback(() => {
    if (abortarEnCurso()) {
      setEnVuelo(false);
      setFase('inactivo');
    }
  }, [abortarEnCurso]);

  // Cambio de trámite o desmontaje: abortar la apertura en curso y liberar el candado.
  useEffect(() => {
    return () => {
      abortarEnCurso();
    };
  }, [instanceId, abortarEnCurso]);

  const abrir = useCallback(async () => {
    if (!instanceId || lock.current) return;
    lock.current = true;
    const controller = new AbortController();
    controllerRef.current = controller;
    const { signal } = controller;

    const reconstruir = requiereReconstruccion(vigencia) && !entregadoTrasVigencia;
    const previo = consolidadoPrevio ?? null;
    setError(null);
    setSirvioAnterior(false);
    setEnVuelo(true);
    setFase(reconstruir ? 'reconstruyendo' : 'abriendo');

    // Reconstrucción larga: la pestaña se abre YA, dentro del gesto del usuario. Abrirla al terminar
    // (30 s después) la dejaría a merced del bloqueador de ventanas emergentes.
    const win = reconstruir ? openLoadingDocumentTab() : null;
    winPendienteRef.current = win;
    let vencido = false;

    if (reconstruir) {
      timerRef.current = setTimeout(() => {
        timerRef.current = null;
        if (signal.aborted) return;
        vencido = true;
        setFase('timeout');
        if (previo) {
          winPendienteRef.current = null;
          setSirvioAnterior(true);
          void abrirAdjunto(instanceId, previo, win).catch(() => undefined);
        }
      }, timeoutMs);
    }

    try {
      await onBeforeGenerate?.();
      if (signal.aborted) return;
      // Ruta única de entrega, sin force: el backend decide si reconstruye o sirve el vigente/definitivo.
      const generado = await tramitesClient.entregarConsolidado(instanceId, { tipo: 'consolidado' });
      if (signal.aborted) return;
      limpiarTimer();
      winPendienteRef.current = null;
      setResultado(generado ?? null);
      const doc = consolidadoDeResultado(generado);
      if (doc) {
        await abrirAdjunto(
          instanceId,
          { id: doc.id, tipo: 'consolidado', filename: doc.filename, mimetype: 'application/pdf' },
          win ?? undefined,
        );
      } else if (win && !win.closed) {
        win.close();
      }
      if (signal.aborted) return;
      // HU #12799 — con fallo de regeneración se sirvió el PDF anterior: NO queda vigente (la
      // siguiente apertura vuelve a intentar) y tampoco se anuncia «se cargó la nueva versión».
      const fallo = detectarFalloRegeneracion(generado);
      if (reconstruir && !fallo) setEntregadoTrasVigencia(true);
      setFase(vencido && !fallo ? 'actualizado' : 'inactivo');
      onEntregado?.(generado ?? null);
    } catch (err) {
      if (signal.aborted) return;
      limpiarTimer();
      winPendienteRef.current = null;
      setError(mensajeErrorConsolidado(err));
      setFase('inactivo');
      // Si ya se sirvió el anterior, la pestaña muestra ese PDF: no se pisa con el error.
      if (win && !win.closed && !vencido) win.close();
    } finally {
      if (controllerRef.current === controller) {
        controllerRef.current = null;
        lock.current = false;
        setEnVuelo(false);
      }
    }
  }, [
    instanceId,
    vigencia,
    entregadoTrasVigencia,
    consolidadoPrevio,
    abrirAdjunto,
    onBeforeGenerate,
    onEntregado,
    timeoutMs,
  ]);

  const limpiarError = useCallback(() => setError(null), []);
  const tomarCandado = useCallback(() => {
    if (lock.current) return false;
    lock.current = true;
    return true;
  }, []);
  const liberarCandado = useCallback(() => {
    lock.current = false;
  }, []);

  return {
    fase,
    enVuelo,
    error,
    resultado,
    definitivo: esDocumentoDefinitivo(resultado),
    avisos: resultado?.avisosCascada ?? [],
    regenerado: resultado?.regenerado ?? null,
    sirvioAnterior,
    abrir,
    tomarCandado,
    liberarCandado,
    cancelar,
    limpiarError,
  };
}
