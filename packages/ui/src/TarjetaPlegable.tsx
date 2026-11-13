"use client";

import { createElement, useId, useState, type ReactNode } from "react";
import { cn } from "./cn";

/** Tarjeta plegable (HU #14562 · UI-1): botón con aria-expanded/aria-controls dentro de un encabezado. */
export interface TarjetaPlegableProps {
  titulo: ReactNode;
  children: ReactNode;
  abiertaInicial?: boolean;
  abierta?: boolean;
  onAlternar?: (siguiente: boolean) => void;
  nivelTitulo?: 2 | 3 | 4;
  className?: string;
}

export function TarjetaPlegable({
  titulo,
  children,
  abiertaInicial = false,
  abierta,
  onAlternar,
  nivelTitulo = 3,
  className,
}: TarjetaPlegableProps) {
  const id = useId();
  const [interna, setInterna] = useState(abiertaInicial);
  const expandida = abierta ?? interna;
  const alternar = () => {
    if (abierta === undefined) setInterna(!expandida);
    onAlternar?.(!expandida);
  };
  return (
    <div className={cn("rounded-[9px] border border-[color:var(--di-border)] bg-[var(--di-surface-elevated)]", className)}>
      {createElement(
        `h${nivelTitulo}`,
        { className: "m-0" },
        <button
          type="button"
          aria-expanded={expandida}
          aria-controls={id}
          onClick={alternar}
          className="flex w-full items-center justify-between gap-3 px-4 py-3 text-left text-base font-semibold text-[color:var(--di-text-primary)] hover:bg-[color:var(--kit-hover)] focus-visible:outline-2 focus-visible:outline-[var(--di-action)]"
        >
          {titulo}
          <svg
            aria-hidden="true"
            focusable="false"
            viewBox="0 0 24 24"
            width="16"
            height="16"
            fill="none"
            stroke="currentColor"
            strokeWidth={2}
            className={cn("shrink-0 transition-transform duration-[var(--di-motion-fast)] motion-reduce:transition-none", expandida && "rotate-180")}
          >
            <path d="M6 9l6 6 6-6" />
          </svg>
        </button>,
      )}
      <div id={id} hidden={!expandida} className="border-t border-[color:var(--di-divider)] px-4 py-3">
        {children}
      </div>
    </div>
  );
}
