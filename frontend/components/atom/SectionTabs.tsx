"use client";

import { useId, useRef, type KeyboardEvent, type ReactNode } from "react";

/**
 * Pestañas de una pantalla (estado local, sin cambiar de ruta). Mismo aspecto que las barras de los demás
 * módulos (Improntas, ficha de compañía): subrayado azul en la activa. Cada panel se monta siempre y se
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
      <div className="flex items-center gap-1 overflow-x-auto border-b" role="tablist" aria-label={ariaLabel}>
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
              className={`relative shrink-0 px-4 py-2.5 text-xs font-semibold transition focus-visible:outline focus-visible:outline-2 focus-visible:outline-[#557EFF] ${
                selected ? "text-[#557EFF]" : "text-[#162744] opacity-70 dark:text-white/[0.78] dark:opacity-100"
              }`}
            >
              {tab.label}
              {tab.count !== undefined && <span className="ml-1.5 font-normal opacity-80">· {tab.count}{tab.countLabel ? ` ${tab.countLabel}` : ""}</span>}
              {selected && (
                <span className="absolute inset-x-0 bottom-0 h-0.5 rounded-full bg-[#557EFF]" />
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
