'use client';

import { useEffect, useRef } from 'react';

/** Epic #12686 (HU #12808) — cada cuánto se piden los conteos con la pestaña visible. */
export const SONDEO_CONTEOS_MS = 60_000;

interface Options<T> {
  /** Pide los conteos con los filtros vigentes. Se lee siempre la última versión. */
  pedir: (signal: AbortSignal) => Promise<T>;
  /** Recibe los conteos nuevos. Un fallo no llega aquí: se conservan los anteriores. */
  alRecibir: (conteos: T) => void;
  /** Apagado mientras la pantalla no está lista (p. ej. cargando el alcance). */
  activo?: boolean;
  intervaloMs?: number;
}

/**
 * Epic #12686 (HU #12808) — mantiene al día la tira de conteos sin tocar la tabla.
 *
 * <p>Cada {@link SONDEO_CONTEOS_MS} pide SOLO los conteos mientras la pestaña del navegador está
 * visible; con la pestaña oculta no pide nada, y al volver pide de inmediato. Nunca deja dos
 * peticiones compitiendo: una nueva cancela la anterior, así la tira siempre muestra la más
 * reciente. Un fallo se ignora y se reintenta en el siguiente ciclo. Mismo patrón que
 * `useLiveOverview` de Reportes (setInterval + visibilitychange).</p>
 *
 * <p>La tabla no se recarga desde aquí: que las filas cambien bajo el cursor mientras alguien
 * trabaja es peor que un número que va un minuto por delante. Para eso está el aviso de cambios
 * que pinta cada pantalla.</p>
 */
export function useSondeoDeConteos<T>({
  pedir,
  alRecibir,
  activo = true,
  intervaloMs = SONDEO_CONTEOS_MS,
}: Options<T>) {
  // Refs a la última versión: cambiar de filtro no debe reiniciar el reloj del sondeo.
  const pedirRef = useRef(pedir);
  const alRecibirRef = useRef(alRecibir);
  useEffect(() => {
    pedirRef.current = pedir;
    alRecibirRef.current = alRecibir;
  });

  useEffect(() => {
    if (!activo) return;
    let enCurso: AbortController | null = null;
    let reloj: ReturnType<typeof setInterval> | null = null;

    const sondear = () => {
      enCurso?.abort();
      const controller = new AbortController();
      enCurso = controller;
      pedirRef
        .current(controller.signal)
        .then((conteos) => {
          if (!controller.signal.aborted) alRecibirRef.current(conteos);
        })
        .catch(() => {
          /* se conservan los conteos anteriores; se reintenta en el siguiente ciclo */
        });
    };

    const arrancar = () => {
      if (reloj === null) reloj = setInterval(sondear, intervaloMs);
    };
    const detener = () => {
      if (reloj !== null) clearInterval(reloj);
      reloj = null;
      enCurso?.abort();
    };

    const alCambiarVisibilidad = () => {
      if (document.visibilityState === 'hidden') {
        detener();
      } else {
        sondear();
        arrancar();
      }
    };

    if (document.visibilityState !== 'hidden') arrancar();
    document.addEventListener('visibilitychange', alCambiarVisibilidad);
    return () => {
      document.removeEventListener('visibilitychange', alCambiarVisibilidad);
      detener();
    };
  }, [activo, intervaloMs]);
}
