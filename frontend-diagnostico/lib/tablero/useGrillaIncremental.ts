"use client";

// Sondeo incremental de la grilla del lote (HU #13748, F04-C3). Primero pide la grilla completa (?desde=0); mientras quede
// alguna celda PENDIENTE o EN_CURSO, cada 3 s pide solo los cambios desde la última versión y los fusiona. Sin pendientes
// se detiene; `reanudar()` (tras un reproceso) vuelve a sondear y `refrescar()` pide una vez, de inmediato (tras cerrar el
// panel o un 409). Un error de un sondeo conserva la grilla y detiene el sondeo hasta reanudar.
import { useCallback, useEffect, useRef, useState } from "react";
import { obtenerGrilla, type Grilla } from "@/lib/tablero/api";
import { fusionarCeldas, hayPendientes } from "@/lib/tablero/fusionarCeldas";

export const INTERVALO_SONDEO_MS = 3000;

export interface EstadoGrilla {
  grilla: Grilla | null;
  /** Primera carga en curso (sin grilla todavía). */
  cargando: boolean;
  /** Falla de la primera carga: no hay grilla que mostrar. */
  error: unknown;
  /** Falla de un sondeo posterior: la grilla sigue a la vista y el sondeo se detiene. */
  errorSondeo: unknown;
  /** Hay un siguiente sondeo programado o en vuelo. */
  sondeando: boolean;
  /** Tras un error de la primera carga: vuelve a pedir la grilla completa. */
  reintentar: () => void;
  /** Vuelve a sondear cada 3 s desde la última versión (tras un reproceso). */
  reanudar: () => void;
  /** Pide una vez, de inmediato, los cambios desde la última versión. */
  refrescar: () => void;
  /** Cambia la grilla en memoria sin pedirla (reproceso optimista, estado tras cerrar el panel). */
  actualizar: (cambio: (grilla: Grilla) => Grilla) => void;
}

interface Opciones {
  /** Sin permiso de ver diagnósticos no se consulta. */
  habilitado?: boolean;
  intervaloMs?: number;
}

/** Cada ciclo arranca un sondeo: de inmediato (primera carga, reintento, refresco) o tras el intervalo (reanudar). */
type Ciclo = { n: number; inmediato: boolean };

export function useGrillaIncremental(id: string, { habilitado = true, intervaloMs = INTERVALO_SONDEO_MS }: Opciones = {}): EstadoGrilla {
  const [grilla, setGrilla] = useState<Grilla | null>(null);
  const [error, setError] = useState<unknown>(null);
  const [errorSondeo, setErrorSondeo] = useState<unknown>(null);
  const [sondeando, setSondeando] = useState(habilitado);
  const [ciclo, setCiclo] = useState<Ciclo>({ n: 0, inmediato: true });
  const grillaRef = useRef<Grilla | null>(null);

  useEffect(() => {
    if (!habilitado) return;
    let vivo = true;
    let temporizador: ReturnType<typeof setTimeout> | undefined;
    const controller = new AbortController();

    async function pedir() {
      try {
        const cambios = await obtenerGrilla(id, grillaRef.current?.version ?? 0, controller.signal);
        if (!vivo) return;
        const vigente = fusionarCeldas(grillaRef.current, cambios);
        grillaRef.current = vigente;
        setGrilla(vigente);
        setError(null);
        setErrorSondeo(null);
        if (hayPendientes(vigente)) temporizador = setTimeout(() => void pedir(), intervaloMs);
        else setSondeando(false);
      } catch (e) {
        if (!vivo || controller.signal.aborted) return;
        if (grillaRef.current) setErrorSondeo(e);
        else setError(e);
        setSondeando(false);
      }
    }

    if (ciclo.inmediato) void pedir();
    else temporizador = setTimeout(() => void pedir(), intervaloMs);

    return () => {
      vivo = false;
      controller.abort();
      clearTimeout(temporizador);
    };
  }, [id, habilitado, intervaloMs, ciclo]);

  const arrancar = useCallback((inmediato: boolean) => {
    setSondeando(true);
    setErrorSondeo(null);
    setCiclo((c) => ({ n: c.n + 1, inmediato }));
  }, []);

  const reintentar = useCallback(() => {
    grillaRef.current = null;
    setGrilla(null);
    setError(null);
    arrancar(true);
  }, [arrancar]);

  const reanudar = useCallback(() => arrancar(false), [arrancar]);
  const refrescar = useCallback(() => arrancar(true), [arrancar]);

  const actualizar = useCallback((cambio: (g: Grilla) => Grilla) => {
    if (!grillaRef.current) return;
    grillaRef.current = cambio(grillaRef.current);
    setGrilla(grillaRef.current);
  }, []);

  return {
    grilla,
    cargando: habilitado && !grilla && !error,
    error,
    errorSondeo,
    sondeando: habilitado && sondeando,
    reintentar,
    reanudar,
    refrescar,
    actualizar,
  };
}
