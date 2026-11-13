import type { ReactNode } from "react";
import { cn } from "./cn";

/**
 * Barra de selección con acciones en bloque (HU #14562 · UI-2): aparece solo con algo seleccionado, es una región con
 * nombre y anuncia el conteo en aria-live polite. Genérica: el texto del conteo y las acciones los pone quien la usa.
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
    <div
      role="region"
      aria-label={etiqueta}
      className={cn(
        "flex flex-wrap items-center gap-3 rounded-[9px] border border-[color:var(--kit-azul-texto)] bg-[var(--di-accent-soft)] px-4 py-2.5",
        className,
      )}
    >
      <span aria-live="polite" className="text-sm font-semibold text-[color:var(--di-text-primary)]">
        {textoSeleccion(cantidad)}
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
