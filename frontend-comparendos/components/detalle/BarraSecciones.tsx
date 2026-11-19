"use client";

import { useEffect, useRef, useState } from "react";
import { pestanaClass } from "@flit/ui/Pestanas";
import { contenedorScroll } from "@/components/detalle/useCercania";

export interface SeccionBarra {
  id: string;
  /** Rótulo completo y, en celular, el corto. */
  texto: string;
  corto?: string;
}

/**
 * Barra de secciones del detalle (rediseño S1, R2): fija arriba, lleva a cada sección con un clic y subraya la que se está
 * viendo (scrollspy). Es navegación dentro de la página: todo el contenido sigue a la vista y nada se esconde.
 */
export function BarraSecciones({ secciones, onIr }: { secciones: SeccionBarra[]; onIr: (id: string) => void }) {
  const [activa, setActiva] = useState(secciones[0]?.id ?? "");
  const barra = useRef<HTMLElement>(null);

  useEffect(() => {
    if (typeof IntersectionObserver === "undefined") return;
    const visibles = new Map<string, boolean>();
    const obs = new IntersectionObserver(
      (entradas) => {
        for (const e of entradas) visibles.set(e.target.id, e.isIntersecting);
        // La activa es la primera sección (en orden de la página) que cruza la franja bajo la barra.
        const primera = secciones.find((s) => visibles.get(s.id));
        if (primera) setActiva(primera.id);
      },
      { root: contenedorScroll(barra.current), rootMargin: "-25% 0px -65% 0px" },
    );
    for (const s of secciones) {
      const el = document.getElementById(s.id);
      if (el) obs.observe(el);
    }
    return () => obs.disconnect();
  }, [secciones]);

  // En celular la barra desliza dentro de sí misma: la activa se mantiene a la vista.
  useEffect(() => {
    barra.current?.querySelector('[aria-current="location"]')?.scrollIntoView?.({ block: "nearest", inline: "nearest" });
  }, [activa]);

  return (
    <nav
      ref={barra}
      aria-label="Secciones del comparendo"
      className="sticky top-0 z-20 -mx-1 border-b border-[color:var(--di-divider)] bg-[var(--di-surface)] px-1"
      data-testid="detalle-barra"
    >
      <div className="kit-pestanas flex items-end gap-0 overflow-x-auto whitespace-nowrap">
        {secciones.map((s) => (
          <button
            key={s.id}
            type="button"
            aria-current={activa === s.id ? "location" : undefined}
            className={pestanaClass(activa === s.id)}
            onClick={() => {
              setActiva(s.id);
              onIr(s.id);
            }}
          >
            {s.corto ? (
              <>
                <span className="sm:hidden">{s.corto}</span>
                <span className="hidden sm:inline">{s.texto}</span>
              </>
            ) : (
              s.texto
            )}
          </button>
        ))}
      </div>
    </nav>
  );
}
