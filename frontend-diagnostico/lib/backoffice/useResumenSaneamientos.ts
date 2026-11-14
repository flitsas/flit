"use client";

import { createContext, useCallback, useContext, useEffect, useMemo, useRef, useState } from "react";
import { obtenerResumenSaneamientos } from "@/lib/backoffice/saneamientos";

// Insignia de la entrada «Saneamientos» del dock (spec-ui §1.4, HU #14577 AC25): solicitudes sin tomar según
// GET /backoffice/saneamientos/resumen. DiagnosticoShell sondea y publica el valor por contexto, para que la bandeja (PS-4)
// y la gestión (PS-5) lo refresquen tras «Tomar», cerrar, reabrir o anular.

export interface ResumenSaneamientos {
  /** Solicitudes sin tomar; null mientras no hay una respuesta (o sin la capacidad). */
  solicitados: number | null;
  /** Pide el resumen en el acto. */
  refrescar: () => void;
}

/** Cada cuánto se vuelve a pedir el resumen con la pestaña visible. */
export const INTERVALO_RESUMEN_MS = 60_000;

export const ResumenSaneamientosContext = createContext<ResumenSaneamientos>({ solicitados: null, refrescar: () => {} });

/** Resumen publicado por el shell. Fuera del shell, `refrescar` no hace nada. */
export function useResumenSaneamientos(): ResumenSaneamientos {
  return useContext(ResumenSaneamientosContext);
}

const visible = () => typeof document === "undefined" || document.visibilityState === "visible";

/**
 * Sondeo del resumen. Con `activo` (capacidad gestionarSaneamiento): pide al montar, luego cada 60 s solo con la pestaña
 * visible, y en el acto al volver a estar visible. Si falla, conserva el último valor sin mostrar error (la insignia es
 * accesoria). Sin `activo` no pide nada. El número no se anuncia por región viva (sería ruido cada 60 s).
 */
export function useSondeoResumenSaneamientos(activo: boolean): ResumenSaneamientos {
  const [solicitados, setSolicitados] = useState<number | null>(null);
  const control = useRef<AbortController | null>(null);

  const pedir = useCallback(() => {
    if (!activo) return;
    control.current?.abort();
    const c = new AbortController();
    control.current = c;
    obtenerResumenSaneamientos(c.signal).then(
      (r) => {
        if (!c.signal.aborted && typeof r?.solicitados === "number" && Number.isFinite(r.solicitados)) setSolicitados(r.solicitados);
      },
      () => {},
    );
  }, [activo]);

  useEffect(() => {
    if (!activo) return;
    pedir();
    const intervalo = setInterval(() => {
      if (visible()) pedir();
    }, INTERVALO_RESUMEN_MS);
    const alCambiar = () => {
      if (visible()) pedir();
    };
    document.addEventListener("visibilitychange", alCambiar);
    return () => {
      clearInterval(intervalo);
      document.removeEventListener("visibilitychange", alCambiar);
      control.current?.abort();
    };
  }, [activo, pedir]);

  const valor = activo ? solicitados : null;
  return useMemo(() => ({ solicitados: valor, refrescar: pedir }), [valor, pedir]);
}
