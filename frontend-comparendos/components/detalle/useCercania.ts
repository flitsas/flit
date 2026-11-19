"use client";

import { useEffect, useState, type RefObject } from "react";

/** El contenedor con scroll del shell (la página no se desplaza en la ventana sino dentro de él). */
export function contenedorScroll(el: Element | null): Element | null {
  return el?.closest("[data-shell-scroll]") ?? null;
}

/**
 * ¿La sección ya se acercó a la vista? Una vez que sí, se queda en sí (no se desmonta al alejarse). Sin
 * IntersectionObserver (navegadores muy viejos) se da por cerca para no esconder acciones.
 */
export function useCercania(ref: RefObject<Element | null>, margen = "600px"): boolean {
  const [cerca, setCerca] = useState(false);
  useEffect(() => {
    const el = ref.current;
    if (!el || cerca) return;
    if (typeof IntersectionObserver === "undefined") {
      const t = window.setTimeout(() => setCerca(true), 0);
      return () => window.clearTimeout(t);
    }
    const obs = new IntersectionObserver((entradas) => {
      if (entradas.some((e) => e.isIntersecting)) setCerca(true);
    }, { root: contenedorScroll(el), rootMargin: `${margen} 0px` });
    obs.observe(el);
    return () => obs.disconnect();
  }, [ref, margen, cerca]);
  return cerca;
}
