"use client";

import type { ReactNode } from "react";
import { cn } from "./cn";
import { useRegionVivaLista } from "./useRegionViva";

/**
 * Barra de selección con acciones en bloque (HU #14562 · UI-2): aparece solo con algo seleccionado, es una región con
 * nombre y anuncia el conteo en aria-live polite. Genérica: el texto del conteo y las acciones los pone quien la usa.
 * HU #14562 · M11: la barra aparece al pasar de 0 a 1 y su región viva nace vacía. El conteo («Selección: 1
 * seleccionado») llega un instante después para que se anuncie también el primero. El conteo visible queda fuera del
 * árbol accesible, para que el lector no lo lea dos veces.
 */
export interface BarraSeleccionProps {
  cantidad: number;
  acciones: ReactNode;
  textoSeleccion?: (cantidad: number) => ReactNode;
  onQuitar?: () => void;
  etiquetaQuitar?: string;
  /** Regla de elegibilidad u otra aclaración. */
  ayuda?: ReactNode;
  etiqueta?: string;
  className?: string;
}

const textoPorDefecto = (n: number) => `${n} ${n === 1 ? "seleccionado" : "seleccionados"}`;

export function BarraSeleccion({
  cantidad,
  acciones,
  textoSeleccion = textoPorDefecto,
  onQuitar,
  etiquetaQuitar = "Quitar selección",
  ayuda,
  etiqueta = "Selección",
  className,
}: BarraSeleccionProps) {
  if (cantidad <= 0) return null;
  return (
    <BarraVisible
      cantidad={cantidad}
      acciones={acciones}
      textoSeleccion={textoSeleccion}
      onQuitar={onQuitar}
      etiquetaQuitar={etiquetaQuitar}
      ayuda={ayuda}
      etiqueta={etiqueta}
      className={className}
    />
  );
}

function BarraVisible({
  cantidad,
  acciones,
  textoSeleccion,
  onQuitar,
  etiquetaQuitar,
  ayuda,
  etiqueta,
  className,
}: Required<Pick<BarraSeleccionProps, "cantidad" | "acciones" | "textoSeleccion" | "etiquetaQuitar" | "etiqueta">> &
  Pick<BarraSeleccionProps, "onQuitar" | "ayuda" | "className">) {
  const lista = useRegionVivaLista();
  return (
    <div
      role="region"
      aria-label={etiqueta}
      className={cn(
        "flex flex-wrap items-center gap-3 rounded-[9px] border border-[color:var(--kit-azul-texto)] bg-[var(--di-accent-soft)] px-4 py-2.5",
        className,
      )}
    >
      <span aria-hidden="true" className="text-sm font-semibold text-[color:var(--di-text-primary)]">
        {textoSeleccion(cantidad)}
      </span>
      <span aria-live="polite" aria-atomic="true" className="sr-only">
        {lista ? (
          <>
            {etiqueta}: {textoSeleccion(cantidad)}
          </>
        ) : null}
      </span>
      {ayuda ? <span className="text-xs text-[color:var(--di-text-tertiary)]">{ayuda}</span> : null}
      <div className="ml-auto flex flex-wrap items-center gap-2">
        {acciones}
        {onQuitar ? (
          <button
            type="button"
            onClick={onQuitar}
            className="min-h-9 rounded-full px-3 text-sm font-semibold text-[color:var(--di-action)] underline underline-offset-[3px] hover:bg-[color:var(--kit-hover)] focus-visible:outline-2 focus-visible:outline-[var(--di-action)]"
          >
            {etiquetaQuitar}
          </button>
        ) : null}
      </div>
    </div>
  );
}
