"use client";

import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { fetchMyApps, type SuiteApp } from "./apps";

/** Estado compartido de `me/apps` entre el menú de productos (▦) y el grupo «Tus productos» del menú de usuario. */
export interface SuiteAppsState {
  /** `null` mientras no se haya cargado (o mientras carga). */
  apps: SuiteApp[] | null;
  failed: boolean;
  /** Pide `me/apps` si todavía no se pidió en este montaje; las llamadas siguientes no hacen nada. */
  request: () => void;
}

// HU #14562 · D1: a ≤ 640 px los productos también se listan en el menú de usuario; los dos menús comparten una sola
// carga por montaje. Si el host ya trae `apps`, no se pide nada.
export function useSuiteApps(
  given: SuiteApp[] | undefined,
  loadApps: (signal: AbortSignal) => Promise<SuiteApp[]> = fetchMyApps,
): SuiteAppsState {
  const [apps, setApps] = useState<SuiteApp[] | null>(given ?? null);
  const [failed, setFailed] = useState(false);
  const requested = useRef(given !== undefined);
  const controller = useRef<AbortController | null>(null);

  useEffect(() => {
    if (given) setApps(given);
  }, [given]);

  useEffect(() => () => controller.current?.abort(), []);

  const request = useCallback(() => {
    if (requested.current) return;
    requested.current = true;
    const current = new AbortController();
    controller.current = current;
    loadApps(current.signal)
      .then((list) => !current.signal.aborted && setApps(list))
      .catch(() => !current.signal.aborted && setFailed(true));
  }, [loadApps]);

  return useMemo(() => ({ apps, failed, request }), [apps, failed, request]);
}
