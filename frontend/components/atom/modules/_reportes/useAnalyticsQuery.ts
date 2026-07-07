"use client";

// Hook de carga de métricas por pestaña (Reportes 2.0, HU-C): fetch on-mount y al
// cambiar filtros, con AbortController, estados UiStateBoundary y reintento.
import { useCallback, useEffect, useRef, useState } from "react";
import type { UiStatus } from "@/components/admin/UiStateBoundary";
import { describeMetricsError } from "./errors";

export interface AnalyticsQueryOptions<T> {
  /** No consulta (p. ej. SuperAdmin sin compañía elegida). */
  skip?: boolean;
  /** Decide si la respuesta cuenta como "sin datos" (estado vacío). */
  isEmpty?: (data: T) => boolean;
}

export interface AnalyticsQueryState<T> {
  data: T | null;
  status: UiStatus;
  errorMessage?: string;
  retry: () => void;
}

/**
 * Ejecuta `fetcher` al montar y cada vez que cambien `deps` (los filtros ya
 * serializados). Cancela la petición en curso al re-disparar o desmontar.
 */
export function useAnalyticsQuery<T>(
  fetcher: (signal: AbortSignal) => Promise<T>,
  deps: readonly unknown[],
  options: AnalyticsQueryOptions<T> = {},
): AnalyticsQueryState<T> {
  const skip = options.skip === true;
  const [data, setData] = useState<T | null>(null);
  const [status, setStatus] = useState<UiStatus>(skip ? "empty" : "loading");
  const [errorMessage, setErrorMessage] = useState<string>();
  const [reloadKey, setReloadKey] = useState(0);

  // Refs para no re-disparar el efecto cuando cambie solo la identidad de las
  // funciones. Se sincronizan en un efecto previo al de carga (orden de declaración).
  const fetcherRef = useRef(fetcher);
  const isEmptyRef = useRef(options.isEmpty);
  useEffect(() => {
    fetcherRef.current = fetcher;
    isEmptyRef.current = options.isEmpty;
  });

  useEffect(() => {
    if (skip) return;
    const controller = new AbortController();

    async function load() {
      setStatus("loading");
      try {
        const res = await fetcherRef.current(controller.signal);
        if (controller.signal.aborted) return;
        setData(res);
        setStatus(isEmptyRef.current?.(res) ? "empty" : "ready");
      } catch (error) {
        if (controller.signal.aborted || (error as Error).name === "AbortError") return;
        setErrorMessage(describeMetricsError(error));
        setStatus("error");
      }
    }

    void load();
    return () => controller.abort();
    // eslint-disable-next-line react-hooks/exhaustive-deps -- deps son los filtros serializados del caller
  }, [skip, reloadKey, ...deps]);

  const retry = useCallback(() => setReloadKey((k) => k + 1), []);
  return { data, status, errorMessage, retry };
}
