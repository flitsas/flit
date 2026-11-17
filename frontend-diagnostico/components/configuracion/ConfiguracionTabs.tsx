"use client";

import { useId, useRef, useState, type KeyboardEvent, type ReactNode } from "react";
import { FOCO_CLS } from "./estilos";

export interface Pestana {
  id: string;
  etiqueta: string;
  contenido: ReactNode;
}

/**
 * Pestañas con el patrón WAI-ARIA «Tabs» de activación automática: una sola pestaña en el orden de tabulación (tabindex
 * itinerante), flechas izquierda/derecha con vuelta, Inicio y Fin. Los paneles quedan montados y ocultos para no perder
 * lo digitado al cambiar de pestaña. La pestaña activa va en la tinta AA del azul de marca
 * (texto blanco 4,8:1), con la sombra del activo del dock.
 */
export function ConfiguracionTabs({ etiqueta, pestanas }: { etiqueta: string; pestanas: Pestana[] }) {
  const base = useId();
  const [activa, setActiva] = useState(0);
  const refs = useRef<Array<HTMLButtonElement | null>>([]);
  const seleccionada = Math.min(activa, pestanas.length - 1);

  function ir(indice: number) {
    setActiva(indice);
    refs.current[indice]?.focus();
  }

  function alPulsar(e: KeyboardEvent<HTMLButtonElement>, indice: number) {
    const total = pestanas.length;
    const destino =
      e.key === "ArrowRight" ? (indice + 1) % total : e.key === "ArrowLeft" ? (indice - 1 + total) % total : e.key === "Home" ? 0 : e.key === "End" ? total - 1 : -1;
    if (destino < 0) return;
    e.preventDefault();
    ir(destino);
  }

  return (
    <div className="flex flex-col gap-4">
      <div role="tablist" aria-label={etiqueta} className="flex w-fit gap-1 rounded-full border border-[var(--nav-borde)] bg-[var(--nav-panel-bg)] p-1 dark:border-white/10 dark:bg-white/[0.04]">
        {pestanas.map((p, i) => {
          const activo = i === seleccionada;
          return (
            <button
              key={p.id}
              ref={(el) => {
                refs.current[i] = el;
              }}
              type="button"
              role="tab"
              id={`${base}-tab-${p.id}`}
              aria-selected={activo}
              aria-controls={`${base}-panel-${p.id}`}
              tabIndex={activo ? 0 : -1}
              onClick={() => setActiva(i)}
              onKeyDown={(e) => alPulsar(e, i)}
              className={`rounded-full px-4 py-2 text-sm font-semibold transition ${FOCO_CLS} ${
                activo ? "bg-[var(--flit-brand-ink)] text-white shadow-[var(--nav-sombra-activo)]" : "text-[var(--nav-texto)] dark:text-slate-300"
              }`}
            >
              {p.etiqueta}
            </button>
          );
        })}
      </div>
      {pestanas.map((p, i) => (
        <div
          key={p.id}
          role="tabpanel"
          id={`${base}-panel-${p.id}`}
          aria-labelledby={`${base}-tab-${p.id}`}
          tabIndex={0}
          hidden={i !== seleccionada}
          className={`flex flex-col gap-4 rounded-2xl ${FOCO_CLS}`}
        >
          {p.contenido}
        </div>
      ))}
    </div>
  );
}
