"use client";

import { useEffect, useRef, useState, type ReactNode } from "react";

// HU #14562 · D2 (axe scrollable-region-focusable, WCAG 2.1.1) + O-4 (auditoría D1–D3): contenedor con scroll horizontal
// de una tabla. Siempre es `region` con nombre «…, desplazable»; solo es parada de Tab (tabIndex 0) cuando el contenido
// desborda (scrollWidth > clientWidth), para que con la tabla entera a la vista no sume una parada vacía. Se vuelve a
// medir con ResizeObserver (cambio de ancho de la pantalla o del contenido). Foco visible con --di-action.
const FOCO_DESPLAZABLE = "focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-[var(--di-action)]";

/** `true` si el elemento tiene más contenido que ancho visible; se actualiza con ResizeObserver. */
export function useDesbordeHorizontal<T extends HTMLElement>() {
  const ref = useRef<T>(null);
  const [desborda, setDesborda] = useState(false);

  useEffect(() => {
    const el = ref.current;
    if (!el) return;
    const medir = () => setDesborda(el.scrollWidth > el.clientWidth);
    medir();
    if (typeof ResizeObserver === "undefined") return;
    const observador = new ResizeObserver(medir);
    observador.observe(el);
    // El ancho de la tabla puede cambiar sin que cambie el del contenedor.
    if (el.firstElementChild) observador.observe(el.firstElementChild);
    return () => observador.disconnect();
  }, []);

  return [ref, desborda] as const;
}

/** Uso: <RegionDesplazable nombre="Personas consultadas" className="rounded-xl border"><table …/></RegionDesplazable> */
export function RegionDesplazable({ nombre, className = "", children }: { nombre: string; className?: string; children: ReactNode }) {
  const [ref, desborda] = useDesbordeHorizontal<HTMLDivElement>();
  return (
    <div
      ref={ref}
      role="region"
      aria-label={`${nombre}, desplazable`}
      tabIndex={desborda ? 0 : undefined}
      className={`overflow-x-auto ${FOCO_DESPLAZABLE} ${className}`.trim()}
    >
      {children}
    </div>
  );
}
