"use client";

import { useEffect, useRef, useState, type ReactNode } from "react";

// HU #14562 · D2 (axe scrollable-region-focusable, WCAG 2.1.1) + O-4 (auditoría D1–D3): contenedor con scroll horizontal
// de una tabla. Solo cuando el contenido desborda (scrollWidth > clientWidth) es `region` con nombre «…, desplazable» y
// parada de Tab (tabIndex 0); con la tabla entera a la vista es un div plano, sin landmark ni parada vacía. Se vuelve a
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
      // O-4 (reauditoría): rol, nombre y tabIndex van juntos y solo con desborde; sin él es un div plano (sin landmark
      // huérfano, y un aria-label en un div sin rol es ARIA prohibido).
      {...(desborda ? { role: "region", "aria-label": `${nombre}, desplazable`, tabIndex: 0 } : {})}
      className={`overflow-x-auto ${FOCO_DESPLAZABLE} ${className}`.trim()}
    >
      {children}
    </div>
  );
}
