"use client";

import { useCallback, useEffect, useRef, useState } from "react";
import { fetchStandaloneBatch } from "@/lib/api/admin-generacion-documental";
import type { StandaloneBatchStatusResult } from "@/lib/api/types-generacion-documental";
import { isStandaloneBatchTerminal } from "./status-labels";

/** Cadencia del sondeo, en milisegundos. Es el AC literal de CF-14: cada 4 segundos. */
export const BATCH_POLL_INTERVAL_MS = 4_000;

export type BatchPollingStatus = "loading" | "ready" | "error" | "notFound";

export interface UseBatchPollingResult {
  batch: StandaloneBatchStatusResult | null;
  status: BatchPollingStatus;
  /** `true` mientras hay un sondeo programado. Alimenta la interfaz y las pruebas. */
  polling: boolean;
  /** Reintento manual: sirve para el estado de error y para reanudar un sondeo detenido. */
  refresh: () => void;
}

/**
 * Seguimiento de un lote por polling (CF-14, HU #12211).
 *
 * <p><b>El sondeo se detiene por dos motivos, y los dos son criterios de aceptación:</b></p>
 * <ol>
 *   <li><b>Estado terminal.</b> En cuanto la respuesta trae `isTerminal` —o el estado es uno de
 *       los tres terminales, como respaldo local—, el temporizador se cancela y NO se vuelve a
 *       programar. Un lote cerrado ya no cambia: seguir preguntando es gasto puro contra la API.</li>
 *   <li><b>La pestaña pierde el foco.</b> Con `visibilityState === "hidden"` el temporizador se
 *       cancela; al volver se consulta de inmediato —esperar cuatro segundos haría parecer
 *       congelada la vista— y el ciclo se reanuda. Sin esto, una pestaña olvidada en segundo plano
 *       sondearía indefinidamente algo que nadie está mirando.</li>
 * </ol>
 *
 * <p><b>Ni correo ni notificación push</b> (CF-14): este hook es todo el mecanismo de aviso de un
 * lote. El avance se consulta, no se empuja.</p>
 *
 * <p>Toda la maquinaria vive DENTRO del efecto —temporizador, bandera de detenido, suscripción a
 * `visibilitychange`— para que cambiar de lote la desmonte entera y no queden dos ciclos vivos
 * sondeando lotes distintos.</p>
 *
 * <p>Uso de ejemplo:</p>
 * <pre>
 * const { batch, status, polling, refresh } = useBatchPolling(batchId);
 * </pre>
 */
export function useBatchPolling(batchId: string | undefined): UseBatchPollingResult {
  const [batch, setBatch] = useState<StandaloneBatchStatusResult | null>(null);
  const [status, setStatus] = useState<BatchPollingStatus>("loading");
  const [polling, setPolling] = useState(false);

  const refreshRef = useRef<() => void>(() => {});

  useEffect(() => {
    if (!batchId) {
      return undefined;
    }

    let cancelado = false;
    let detenido = false;
    let temporizador: ReturnType<typeof setTimeout> | null = null;

    const cancelar = () => {
      if (temporizador !== null) {
        clearTimeout(temporizador);
        temporizador = null;
      }
      setPolling(false);
    };

    const programar = () => {
      if (temporizador !== null) {
        clearTimeout(temporizador);
      }
      setPolling(true);
      temporizador = setTimeout(() => {
        void consultar();
      }, BATCH_POLL_INTERVAL_MS);
    };

    const consultar = async () => {
      try {
        const resultado = await fetchStandaloneBatch(batchId);
        if (cancelado) {
          return;
        }

        setBatch(resultado);
        setStatus("ready");

        if (resultado.isTerminal || isStandaloneBatchTerminal(resultado.status)) {
          detenido = true;
          cancelar();
          return;
        }
      } catch (error) {
        if (cancelado) {
          return;
        }

        // 404 = no existe o no es de esta compañía. No se distingue, y no se reintenta en bucle
        // contra algo que no va a aparecer.
        const noEncontrado =
          typeof error === "object" && error !== null && (error as { status?: number }).status === 404;

        setStatus(noEncontrado ? "notFound" : "error");

        if (noEncontrado) {
          detenido = true;
          cancelar();
          return;
        }
      }

      if (!detenido && !pestanaOculta()) {
        programar();
      }
    };

    refreshRef.current = () => {
      detenido = false;
      void consultar();
    };

    void consultar();

    const alCambiarVisibilidad = () => {
      if (pestanaOculta()) {
        cancelar();
        return;
      }

      if (!detenido) {
        void consultar();
      }
    };

    document.addEventListener("visibilitychange", alCambiarVisibilidad);

    return () => {
      cancelado = true;
      document.removeEventListener("visibilitychange", alCambiarVisibilidad);
      cancelar();
      refreshRef.current = () => {};
    };
  }, [batchId]);

  const refresh = useCallback(() => refreshRef.current(), []);

  return { batch, status, polling, refresh };
}

/** `true` si la pestaña está en segundo plano. Aislado para poder simularlo en las pruebas. */
function pestanaOculta(): boolean {
  return typeof document !== "undefined" && document.visibilityState === "hidden";
}
