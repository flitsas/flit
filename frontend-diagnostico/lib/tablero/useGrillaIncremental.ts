"use client";

// Sondeo incremental de la grilla del lote (HU #13748, F04-C3; HU #14562 AC26/AC29). Primero pide la grilla completa
// (?desde=0); mientras quede alguna celda PENDIENTE o EN_CURSO, cada 3 s pide solo los cambios desde la última versión y los
// fusiona. Sin pendientes pasa a reposo: cada 30 s, siempre con ?desde=, para que un dictamen ajustado por soporte FLIT se vea
// sin recargar (H3). Con la pestaña oculta el sondeo se pausa y, al volver a estar visible, pide de inmediato. `reanudar()`
// (tras un reproceso) vuelve al ritmo de 3 s y `refrescar()` pide una vez, de inmediato. Un fallo aislado se reintenta; tras
// FALLOS_ANTES_DE_DETENER fallos seguidos la grilla se conserva, el sondeo se detiene y `actualizadoEn` dice desde cuándo.
import { useCallback, useEffect, useRef, useState } from "react";
import { obtenerGrilla, type Grilla } from "@/lib/tablero/api";
import { fusionarCeldas, hayPendientes } from "@/lib/tablero/fusionarCeldas";

export const INTERVALO_SONDEO_MS = 3000;
/** HU #14562 AC29 (H3): sondeo de reposo sin pendientes. */
export const INTERVALO_REPOSO_MS = 30000;
/** HU #14562 AC26: fallos seguidos del sondeo antes de detenerlo y avisar que los datos están desactualizados. */
export const FALLOS_ANTES_DE_DETENER = 3;

export interface EstadoGrilla {
  grilla: Grilla | null;
  /** Primera carga en curso (sin grilla todavía). */
  cargando: boolean;
  /** Falla de la primera carga: no hay grilla que mostrar. */
  error: unknown;
  /** El sondeo falló de forma repetida: la grilla sigue a la vista y el sondeo se detiene. */
  errorSondeo: unknown;
  /** Hay un siguiente sondeo de 3 s programado o en vuelo (el reposo de 30 s no cuenta). */
  sondeando: boolean;
  /** Hora de la última lectura buena de la grilla (null antes de la primera). */
  actualizadoEn: Date | null;
  /** Tras un error de la primera carga: vuelve a pedir la grilla completa. */
  reintentar: () => void;
  /** Vuelve a sondear cada 3 s desde la última versión (tras un reproceso). */
  reanudar: () => void;
  /** Pide una vez, de inmediato, los cambios desde la última versión, y retoma el sondeo. */
  refrescar: () => void;
  /**
   * Pide una vez, de inmediato, la grilla completa (?desde=0) y la fusiona sin quitarla de la pantalla. Red de seguridad ante
   * un error; los cambios de fila llegan por su versión en el sondeo incremental (HU #14562 AC30).
   */
  recargar: () => void;
  /** Cambia la grilla en memoria sin pedirla (reproceso optimista, estado tras cerrar el panel). */
  actualizar: (cambio: (grilla: Grilla) => Grilla) => void;
}

interface Opciones {
  /** Sin permiso de ver diagnósticos no se consulta. */
  habilitado?: boolean;
  intervaloMs?: number;
  intervaloReposoMs?: number;
}

/** Cada ciclo arranca un sondeo: de inmediato (primera carga, reintento, refresco) o tras el intervalo (reanudar). */
type Ciclo = { n: number; inmediato: boolean; completo?: boolean };

const oculta = () => typeof document !== "undefined" && document.visibilityState === "hidden";

export function useGrillaIncremental(
  id: string,
  { habilitado = true, intervaloMs = INTERVALO_SONDEO_MS, intervaloReposoMs = INTERVALO_REPOSO_MS }: Opciones = {},
): EstadoGrilla {
  const [grilla, setGrilla] = useState<Grilla | null>(null);
  const [error, setError] = useState<unknown>(null);
  const [errorSondeo, setErrorSondeo] = useState<unknown>(null);
  const [sondeando, setSondeando] = useState(habilitado);
  const [actualizadoEn, setActualizadoEn] = useState<Date | null>(null);
  const [ciclo, setCiclo] = useState<Ciclo>({ n: 0, inmediato: true });
  const grillaRef = useRef<Grilla | null>(null);

  useEffect(() => {
    if (!habilitado) return;
    let vivo = true;
    let temporizador: ReturnType<typeof setTimeout> | undefined;
    // Espera del siguiente sondeo; se conserva mientras la pestaña está oculta para retomarlo al volver.
    let siguiente: number | null = null;
    let enVuelo = false;
    let fallos = 0;
    const controller = new AbortController();

    function programar(ms: number) {
      siguiente = ms;
      if (oculta()) return;
      temporizador = setTimeout(() => {
        siguiente = null;
        void pedir();
      }, ms);
    }

    let completo = ciclo.completo === true;
    async function pedir() {
      enVuelo = true;
      try {
        const desde = completo ? 0 : (grillaRef.current?.version ?? 0);
        completo = false;
        const cambios = await obtenerGrilla(id, desde, controller.signal);
        if (!vivo) return;
        fallos = 0;
        const vigente = fusionarCeldas(grillaRef.current, cambios);
        grillaRef.current = vigente;
        setGrilla(vigente);
        setActualizadoEn(new Date());
        setError(null);
        setErrorSondeo(null);
        if (hayPendientes(vigente)) {
          setSondeando(true);
          programar(intervaloMs);
        } else {
          setSondeando(false);
          programar(intervaloReposoMs);
        }
      } catch (e) {
        if (!vivo || controller.signal.aborted) return;
        if (!grillaRef.current) {
          setError(e);
          setSondeando(false);
          return;
        }
        fallos += 1;
        if (fallos < FALLOS_ANTES_DE_DETENER) {
          programar(intervaloMs);
          return;
        }
        setErrorSondeo(e);
        setSondeando(false);
      } finally {
        enVuelo = false;
      }
    }

    function alCambiarVisibilidad() {
      if (oculta()) {
        clearTimeout(temporizador);
        return;
      }
      if (siguiente == null || enVuelo) return;
      clearTimeout(temporizador);
      siguiente = null;
      void pedir();
    }
    document.addEventListener("visibilitychange", alCambiarVisibilidad);

    if (ciclo.inmediato) void pedir();
    else programar(intervaloMs);

    return () => {
      vivo = false;
      controller.abort();
      clearTimeout(temporizador);
      document.removeEventListener("visibilitychange", alCambiarVisibilidad);
    };
  }, [id, habilitado, intervaloMs, intervaloReposoMs, ciclo]);

  const arrancar = useCallback((inmediato: boolean, completo = false) => {
    setSondeando(true);
    setErrorSondeo(null);
    setCiclo((c) => ({ n: c.n + 1, inmediato, completo }));
  }, []);

  const reintentar = useCallback(() => {
    grillaRef.current = null;
    setGrilla(null);
    setError(null);
    arrancar(true);
  }, [arrancar]);

  const reanudar = useCallback(() => arrancar(false), [arrancar]);
  const refrescar = useCallback(() => arrancar(true), [arrancar]);
  const recargar = useCallback(() => arrancar(true, true), [arrancar]);

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
    actualizadoEn,
    reintentar,
    reanudar,
    refrescar,
    recargar,
    actualizar,
  };
}
