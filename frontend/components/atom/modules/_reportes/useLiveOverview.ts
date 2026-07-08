"use client";

// Hook del panel "Ahora mismo" (Reportes 2.0, HU-C): consulta /live-overview con
// auto-refresh configurable 30–60 s (default 45 s), pausa manual, pausa automática
// cuando la pestaña del navegador está oculta (visibilitychange) e indicador de
// "actualizado hace Xs". Polling con el patrón BiometricStep (setInterval + cleanup).
import { useCallback, useEffect, useRef, useState } from "react";
import type { UiStatus } from "@/components/admin/UiStateBoundary";
import {
  fetchLiveOverview,
  type LiveOverviewParams,
  type LiveOverviewResponse,
} from "@/lib/api/analytics-v2";
import { describeMetricsError } from "./errors";

export const LIVE_REFRESH_MIN_S = 30;
export const LIVE_REFRESH_MAX_S = 60;
export const LIVE_REFRESH_DEFAULT_S = 45;

export interface LiveOverviewState {
  data: LiveOverviewResponse | null;
  status: UiStatus;
  errorMessage?: string;
  /** Pausa manual del usuario (botón pausar/reanudar). */
  paused: boolean;
  setPaused: (paused: boolean) => void;
  /** Intervalo de refresco vigente, ya acotado a 30–60 s. */
  intervalSec: number;
  setIntervalSec: (seconds: number) => void;
  /** Segundos transcurridos desde la última actualización exitosa. */
  secondsAgo: number | null;
  retry: () => void;
}

export function useLiveOverview(
  params: LiveOverviewParams,
  options: { skip?: boolean } = {},
): LiveOverviewState {
  const skip = options.skip === true;
  const [data, setData] = useState<LiveOverviewResponse | null>(null);
  const [status, setStatus] = useState<UiStatus>(skip ? "empty" : "loading");
  const [errorMessage, setErrorMessage] = useState<string>();
  const [paused, setPaused] = useState(false);
  const [hidden, setHidden] = useState(false);
  const [intervalSec, setIntervalSecState] = useState(LIVE_REFRESH_DEFAULT_S);
  const [secondsAgo, setSecondsAgo] = useState<number | null>(null);
  const [reloadKey, setReloadKey] = useState(0);
  const updatedAtRef = useRef<number | null>(null);

  const tenantId = params.tenantId;
  const stuckDays = params.stuckDays;

  const load = useCallback(
    async (signal: AbortSignal, initial: boolean) => {
      if (initial) setStatus("loading");
      try {
        const res = await fetchLiveOverview({ tenantId, stuckDays }, signal);
        if (signal.aborted) return;
        setData(res);
        setStatus("ready");
        updatedAtRef.current = Date.now();
        setSecondsAgo(0);
      } catch (error) {
        if (signal.aborted || (error as Error).name === "AbortError") return;
        if (initial) {
          setErrorMessage(describeMetricsError(error));
          setStatus("error");
        }
        // Refresh silencioso fallido: se conservan los datos y se reintenta al siguiente tick.
      }
    },
    [tenantId, stuckDays],
  );

  // Carga inicial + recarga al cambiar filtros o reintentar.
  useEffect(() => {
    if (skip) return;
    const controller = new AbortController();
    // eslint-disable-next-line react-hooks/set-state-in-effect -- patrón de carga del repo: skeleton inmediato antes del fetch
    void load(controller.signal, true);
    return () => controller.abort();
  }, [skip, load, reloadKey]);

  // Pausa automática cuando el documento no es visible (§9).
  useEffect(() => {
    function onVisibility() {
      setHidden(document.visibilityState === "hidden");
    }
    onVisibility();
    document.addEventListener("visibilitychange", onVisibility);
    return () => document.removeEventListener("visibilitychange", onVisibility);
  }, []);

  const active = !skip && !paused && !hidden;

  // Auto-refresh — patrón BiometricStep.tsx (setInterval en useEffect + cleanup).
  useEffect(() => {
    if (!active) return;
    const id = setInterval(() => {
      const controller = new AbortController();
      void load(controller.signal, false);
    }, intervalSec * 1000);
    return () => clearInterval(id);
  }, [active, intervalSec, load]);

  // Ticker de "actualizado hace Xs" (1 s).
  useEffect(() => {
    if (skip || data === null) return;
    const id = setInterval(() => {
      const at = updatedAtRef.current;
      if (at !== null) setSecondsAgo(Math.max(0, Math.round((Date.now() - at) / 1000)));
    }, 1000);
    return () => clearInterval(id);
  }, [skip, data]);

  const setIntervalSec = useCallback((seconds: number) => {
    const clamped = Math.min(LIVE_REFRESH_MAX_S, Math.max(LIVE_REFRESH_MIN_S, Math.round(seconds)));
    setIntervalSecState(clamped);
  }, []);
  const retry = useCallback(() => setReloadKey((k) => k + 1), []);

  return { data, status, errorMessage, paused, setPaused, intervalSec, setIntervalSec, secondsAgo, retry };
}
