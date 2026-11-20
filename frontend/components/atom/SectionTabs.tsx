"use client";

import { useId, useRef, type KeyboardEvent, type ReactNode } from "react";
import { PESTANAS_LISTA_CLS, pestanaClass } from "@flit/ui/Pestanas";

/**
 * Pestañas de una pantalla (estado local, sin cambiar de ruta), con el aspecto de las del Manual UI Kit 2026
 * (`@flit/ui/Pestanas`): línea inferior gris y la activa en azul. Cada panel se monta siempre y se
 * oculta con `hidden`, así una tabla conserva su búsqueda y su página al volver a ella.
 */
export interface SectionTab<Id extends string = string> {
  id: Id;
  label: string;
  /** Cantidad opcional que se muestra junto al nombre, p. ej. «Mandatarios · 6». */
  count?: number;
  /** Texto que acompaña a `count` («· 0 por revisar»): aclara qué se está contando cuando no son todas las filas. */
  countLabel?: string;
  /** Ayuda al pasar el cursor sobre la pestaña. */
  title?: string;
  content: ReactNode;
}

export function SectionTabs<Id extends string>({
  tabs,
  active,
  onChange,
  ariaLabel,
}: {
  tabs: readonly SectionTab<Id>[];
  active: Id;
  onChange: (id: Id) => void;
  ariaLabel: string;
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
    <div className="flex flex-col gap-4">
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
              className={pestanaClass(selected, "relative shrink-0")}
            >
              {tab.label}
              {tab.count !== undefined && <span className="ml-1.5 font-normal normal-case tracking-normal opacity-80">· {tab.count}{tab.countLabel ? ` ${tab.countLabel}` : ""}</span>}
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
