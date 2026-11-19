"use client";

import { useCallback, useEffect, useState } from "react";
import { consultaComoQuery, obtenerCatalogo, obtenerReporte, rutaReporte, type AmbitoReporte, type ConsultaReporte } from "@/lib/reportes/api";
import type { CatalogoReportesDto, PaginaReporteDto, TipoReporte } from "@/lib/reportes/tipos";

export type EstadoPeticion = "cargando" | "listo" | "error";

interface Resultado<T> {
  estado: EstadoPeticion;
  datos: T | null;
  error: unknown;
  reintentar: () => void;
}

/**
 * Petición GET con clave: un cambio de clave aborta la anterior (AbortController) y, mientras llega la nueva, la vista
 * está cargando. Los datos viven solo en memoria del componente: nada va a localStorage ni a sessionStorage (SP-18).
 */
function usePeticion<T>(clave: string | null, pedir: (signal: AbortSignal) => Promise<T>): Resultado<T> {
  const [intento, setIntento] = useState(0);
  const [r, setR] = useState<{ clave: string | null; intento: number; estado: EstadoPeticion; datos: T | null; error: unknown }>({
    clave: null,
    intento: -1,
    estado: "cargando",
    datos: null,
    error: null,
  });

  useEffect(() => {
    if (clave === null) return;
    const control = new AbortController();
    pedir(control.signal).then(
      (datos) => {
        if (!control.signal.aborted) setR({ clave, intento, estado: "listo", datos, error: null });
      },
      (error: unknown) => {
        if (!control.signal.aborted) setR({ clave, intento, estado: "error", datos: null, error });
      },
    );
    return () => control.abort();
    // `clave` resume todo lo que cambia la petición; `pedir` cambia de identidad en cada render.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [clave, intento]);

  const reintentar = useCallback(() => setIntento((i) => i + 1), []);
  return r.clave === clave && r.intento === intento
    ? { estado: r.estado, datos: r.datos, error: r.error, reintentar }
    : { estado: "cargando", datos: null, error: null, reintentar };
}

export function useCatalogoReportes(vistaGlobal: boolean): Resultado<CatalogoReportesDto> {
  return usePeticion(vistaGlobal ? "backoffice" : "compania", (signal) => obtenerCatalogo(vistaGlobal, signal));
}

/** Página del reporte activo. Sin tipo (catálogo aún sin cargar) no pide nada. */
export function useReporte(ambito: AmbitoReporte, tipo: TipoReporte | null, consulta: ConsultaReporte): Resultado<PaginaReporteDto> {
  const clave = tipo ? `${rutaReporte(ambito, tipo)}?${consultaComoQuery(ambito, consulta, true)}` : null;
  return usePeticion(clave, (signal) => obtenerReporte(ambito, tipo as TipoReporte, consulta, signal));
}
