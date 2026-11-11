"use client";

import { useCallback, useEffect, useState } from "react";
import { esperaDe } from "./errores";

/**
 * Bloquea una acción mientras el back pida esperar (429/503 con `Retry-After`). La pantalla deshabilita su botón con
 * `bloqueado` y muestra `segundosRestantes`; nada reintenta solo.
 */
export function useBloqueo(ahora: () => number = Date.now) {
  const [hasta, setHasta] = useState<number | null>(null);
  const [segundosRestantes, setSegundosRestantes] = useState(0);

  useEffect(() => {
    if (hasta === null) return;
    const actualizar = () => {
      const restantes = Math.ceil((hasta - ahora()) / 1000);
      if (restantes <= 0) {
        setHasta(null);
        setSegundosRestantes(0);
      } else {
        setSegundosRestantes(restantes);
      }
    };
    actualizar();
    const id = setInterval(actualizar, 1000);
    return () => clearInterval(id);
  }, [hasta, ahora]);

  /** Se llama con el error de una llamada; si trae espera, arranca el bloqueo. Devuelve si bloqueó. */
  const registrar = useCallback(
    (error: unknown): boolean => {
      const espera = esperaDe(error);
      if (!espera) return false;
      setHasta(ahora() + espera * 1000);
      return true;
    },
    [ahora],
  );

  /** Arranca una espera conocida de antemano (p. ej. los 60 s entre dos reenvíos del enlace). */
  const iniciar = useCallback(
    (segundos: number) => {
      if (segundos > 0) setHasta(ahora() + segundos * 1000);
    },
    [ahora],
  );

  return { bloqueado: hasta !== null, segundosRestantes, registrar, iniciar };
}
