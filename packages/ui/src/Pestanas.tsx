"use client";

import { useId, useRef, type KeyboardEvent, type ReactNode } from "react";
import { cn } from "./cn";

/**
 * Pestañas del Manual UI Kit 2026 (propuesta-2, `.pestanas`): sin contenedor, línea inferior gris de 2 px en cada una y
 * la activa en azul con línea de 3 px; texto de 14 px en negrita y mayúsculas. Se desliza en horizontal si no cabe.
 *
 * Dos formas de usarlas:
 *  - `<Pestanas>`: la pieza completa (estado local, mismos roles y teclado que `SectionTabs`).
 *  - `PESTANAS_LISTA_CLS` + `pestanaClass(activa)`: para las barras propias que ya tienen su lógica (enlaces por ruta,
 *    `role="tab"`, contadores). Se cambia SOLO la clase; roles, foco y manejadores quedan como están.
 */
export const PESTANAS_LISTA_CLS = "kit-pestanas flex items-end gap-0 overflow-x-auto whitespace-nowrap bg-transparent";

export function pestanaClass(activa: boolean, className?: string): string {
  return cn("kit-pestana", activa && "kit-pestana--activa", className);
}

export interface Pestana<Id extends string = string> {
  id: Id;
  label: ReactNode;
  /** Cantidad opcional junto al nombre («Mandatarios · 6»). */
  count?: number;
  countLabel?: string;
  title?: string;
  content: ReactNode;
}

export function Pestanas<Id extends string>({
  tabs,
  active,
  onChange,
  ariaLabel,
  className,
}: {
  tabs: readonly Pestana<Id>[];
  active: Id;
  onChange: (id: Id) => void;
  ariaLabel: string;
  className?: string;
}) {
  const baseId = useId();
  const refs = useRef<Array<HTMLButtonElement | null>>([]);
  const onKeyDown = (e: KeyboardEvent<HTMLButtonElement>, index: number) => {
    const last = tabs.length - 1;
    const next =
      e.key === "ArrowRight" ? (index === last ? 0 : index + 1)
      : e.key === "ArrowLeft" ? (index === 0 ? last : index - 1)
      : e.key === "Home" ? 0
      : e.key === "End" ? last
      : null;
    if (next === null) return;
    e.preventDefault();
    onChange(tabs[next].id);
    refs.current[next]?.focus();
  };
  return (
    <div className={cn("flex flex-col gap-4", className)}>
      <div className={PESTANAS_LISTA_CLS} role="tablist" aria-label={ariaLabel}>
        {tabs.map((tab, i) => {
          const selected = tab.id === active;
          return (
            <button
              key={tab.id}
              ref={(el) => {
                refs.current[i] = el;
              }}
              id={`${baseId}-tab-${tab.id}`}
              type="button"
              role="tab"
              aria-selected={selected}
              aria-controls={`${baseId}-panel-${tab.id}`}
              tabIndex={selected ? 0 : -1}
              title={tab.title}
              onClick={() => onChange(tab.id)}
              onKeyDown={(e) => onKeyDown(e, i)}
              className={pestanaClass(selected)}
            >
              {tab.label}
              {tab.count !== undefined && (
                <span className="ml-1.5 font-normal normal-case tracking-normal opacity-80">
                  · {tab.count}
                  {tab.countLabel ? ` ${tab.countLabel}` : ""}
                </span>
              )}
            </button>
          );
        })}
      </div>
      {tabs.map((tab) => (
        <div
          key={tab.id}
          id={`${baseId}-panel-${tab.id}`}
          role="tabpanel"
          aria-labelledby={`${baseId}-tab-${tab.id}`}
          hidden={tab.id !== active}
        >
          {tab.content}
        </div>
      ))}
    </div>
  );
}
