"use client";

import { useCallback, useEffect, useState } from "react";
import { obtenerEstadisticas, rutaEstadisticas, type AmbitoConsulta } from "@/lib/estadisticas/api";
import type { EstadisticasDto, Rango } from "@/lib/estadisticas/tipos";

export type EstadoCarga = "cargando" | "listo" | "error";

export interface ResultadoEstadisticas {
  estado: EstadoCarga;
  datos: EstadisticasDto | null;
  error: unknown;
  reintentar: () => void;
}

/**
 * Una petición por vista (spec-ui §4.3, A1): cada cambio de ámbito o de rango aborta la anterior (AbortController, el
 * patrón de lib/api.ts) y pide de nuevo. Nada se guarda en localStorage ni sessionStorage.
 */
export function useEstadisticas(ambito: AmbitoConsulta, rango: Rango): ResultadoEstadisticas {
  const clave = rutaEstadisticas(ambito, rango);
  const [intento, setIntento] = useState(0);
  const [resultado, setResultado] = useState<{ clave: string; intento: number; estado: EstadoCarga; datos: EstadisticasDto | null; error: unknown }>({
    clave: "",
    intento: -1,
    estado: "cargando",
    datos: null,
    error: null,
  });

  useEffect(() => {
    const control = new AbortController();
    obtenerEstadisticas(ambito, rango, control.signal).then(
      (datos) => {
        if (!control.signal.aborted) setResultado({ clave, intento, estado: "listo", datos, error: null });
      },
      (error: unknown) => {
        if (!control.signal.aborted) setResultado({ clave, intento, estado: "error", datos: null, error });
      },
    );
    return () => control.abort();
    // `clave` resume ámbito y rango; los objetos cambian de identidad en cada render.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [clave, intento]);

  const reintentar = useCallback(() => setIntento((i) => i + 1), []);
  // Mientras llega la respuesta de la petición vigente, la vista está cargando (no muestra los datos del rango anterior).
  const vigente = resultado.clave === clave && resultado.intento === intento;
  return vigente
    ? { estado: resultado.estado, datos: resultado.datos, error: resultado.error, reintentar }
    : { estado: "cargando", datos: null, error: null, reintentar };
}
