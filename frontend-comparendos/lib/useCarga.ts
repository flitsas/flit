"use client";

import { useCallback, useEffect, useState } from "react";

export interface Carga<T> {
  datos: T | null;
  /** loading mientras la clave actual no tiene respuesta; luego ready o error. */
  estado: "loading" | "ready" | "error";
  error: string | null;
  recargar: () => void;
}

/**
 * Carga datos de la API cada vez que cambia `clave` (la query, el id…). El estado de «cargando» se deriva de si la
 * respuesta guardada es de la clave actual, así el efecto nunca llama a setState de forma síncrona (regla
 * react-hooks/set-state-in-effect) y una respuesta vieja no pisa la nueva.
 */
export function useCarga<T>(clave: string | null, cargar: (signal: AbortSignal) => Promise<T>): Carga<T> {
  const [version, setVersion] = useState(0);
  const [resultado, setResultado] = useState<{ clave: string; datos: T | null; error: string | null } | null>(null);
  const claveEfectiva = clave === null ? null : `${clave}#${version}`;

  useEffect(() => {
    if (claveEfectiva === null) return;
    const controller = new AbortController();
    cargar(controller.signal).then(
      (datos) => {
        if (!controller.signal.aborted) setResultado({ clave: claveEfectiva, datos, error: null });
      },
      (e: unknown) => {
        if (!controller.signal.aborted)
          setResultado({ clave: claveEfectiva, datos: null, error: e instanceof Error ? e.message : "No se pudo cargar la información." });
      },
    );
    return () => controller.abort();
    // `cargar` cambia en cada render; lo que decide recargar es la clave.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [claveEfectiva]);

  const recargar = useCallback(() => setVersion((v) => v + 1), []);
  const vigente = resultado !== null && resultado.clave === claveEfectiva;
  return {
    datos: resultado?.datos ?? null,
    estado: claveEfectiva === null ? "ready" : !vigente ? "loading" : resultado.error ? "error" : "ready",
    error: vigente ? resultado.error : null,
    recargar,
  };
}
