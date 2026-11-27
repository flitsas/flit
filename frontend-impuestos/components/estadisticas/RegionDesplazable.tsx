"use client";

import { useEffect, useRef, useState, type ReactNode } from "react";

// Copia local de la forma de frontend-diagnostico/components/comun/RegionDesplazable.tsx (D-UI-GRAFICAS, la usa
// BarrasSerie): solo cuando el contenido desborda es `region` con nombre «…, desplazable» y parada de Tab (WCAG 2.1.1,
// axe scrollable-region-focusable); si cabe, es un div plano sin landmark ni parada vacía.
const FOCO_DESPLAZABLE = "focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-[var(--di-action)]";

const desborda = (el: HTMLElement) => el.scrollWidth > el.clientWidth || el.scrollHeight > el.clientHeight;

/** Uso: <RegionDesplazable nombre="Datos de Liquidaciones por día" className="max-h-64"><table …/></RegionDesplazable> */
export function RegionDesplazable({ nombre, className = "", children }: { nombre: string; className?: string; children: ReactNode }) {
  const ref = useRef<HTMLDivElement>(null);
  const [hayDesborde, setHayDesborde] = useState(false);

  useEffect(() => {
    const el = ref.current;
    if (!el) return;
    const medir = () => setHayDesborde(desborda(el));
    medir();
    if (typeof ResizeObserver === "undefined") return;
    const observador = new ResizeObserver(medir);
    observador.observe(el);
    if (el.firstElementChild) observador.observe(el.firstElementChild);
    return () => observador.disconnect();
  }, []);

  return (
    <div
      ref={ref}
      {...(hayDesborde ? { role: "region", "aria-label": `${nombre}, desplazable`, tabIndex: 0 } : {})}
      className={`overflow-x-auto ${FOCO_DESPLAZABLE} ${className}`.trim()}
    >
      {children}
    </div>
  );
}
