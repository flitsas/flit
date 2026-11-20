"use client";

import { useEffect, type RefObject } from "react";

/** Variable CSS con el alto del pie del layout `app` (la reserva del contenedor desde `lg`). */
export const VARIABLE_PIE_ALTO = "--suite-pie-alto";

/**
 * Responsive iOS (S1, ronda 2): el pie del layout `app` vive al final de `[data-shell-scroll]` y desde `lg` pasa a
 * `position: fixed` abajo. Como allí sale del flujo, el contenedor reserva su alto con
 * `padding-bottom: var(--suite-pie-alto, 2rem)`. Los 2rem son el pie de Trámites a una línea (32 px), así el primer
 * pintado de escritorio ya es el de siempre. Este hook solo corrige la variable si el pie mide otra cosa (otro pie, o
 * uno que se parta en dos líneas). Escribe el `style` directamente, sin estado de React: no hay re-render.
 */
export function usePieAlto(pie: RefObject<HTMLElement | null>, contenedor: RefObject<HTMLElement | null>) {
  useEffect(() => {
    const el = pie.current;
    const destino = contenedor.current;
    if (!el || !destino || typeof ResizeObserver === "undefined") return;
    const observador = new ResizeObserver(() => {
      destino.style.setProperty(VARIABLE_PIE_ALTO, `${el.offsetHeight}px`);
    });
    observador.observe(el);
    return () => {
      observador.disconnect();
      destino.style.removeProperty(VARIABLE_PIE_ALTO);
    };
  }, [pie, contenedor]);
}
