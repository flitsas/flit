"use client";

import type { ReactNode } from "react";
import { cn } from "./cn";

/**
 * Chips de conteo que filtran (`.embudo` + `.chip` del prototipo «Diseño Inteligente», HU #14476):
 * «15 Todos», «2 Borrador»… El chip activo lleva `aria-pressed="true"` y se pinta con el azul de acción;
 * el que tiene algo pendiente, con el borde turquesa (`.chip--pendiente`).
 *
 * Se ofrece en dos piezas: {@link ChipFiltro} suelto, para quien ya tiene su propio marcado, y
 * {@link ChipsFiltro}, la fila completa con `role="group"`, para quien solo tiene la lista de opciones.
 */

/** Clases del chip, por si una pantalla necesita aplicarlas a su propio `<button>`. */
export const CHIP_FILTRO_CLS =
  "inline-flex min-h-11 items-center gap-2 rounded-[10px] border px-3.5 cursor-pointer transition-colors " +
  "border-[color:var(--di-border)] bg-[var(--di-surface)] text-[color:var(--di-text-secondary)] " +
  "hover:bg-[var(--di-accent-soft)] focus:outline-none focus-visible:ring-2 focus-visible:ring-flit-brand " +
  "focus-visible:ring-offset-2 " +
  "aria-pressed:border-[color:var(--di-action)] aria-pressed:bg-[var(--di-accent-soft)] " +
  "aria-pressed:text-[color:var(--di-action)] " +
  "disabled:cursor-default disabled:opacity-55 disabled:hover:bg-[var(--di-surface)]";

/** Fila de chips (`.embudo`). */
export const CHIPS_FILTRO_FILA_CLS = "flex flex-wrap gap-2";

export interface ChipFiltroProps {
  /** Conteo en negrita delante de la etiqueta. Se omite si no se pasa. */
  cifra?: ReactNode;
  etiqueta: ReactNode;
  activo: boolean;
  onClick: () => void;
  pendiente?: boolean;
  disabled?: boolean;
  className?: string;
  "aria-label"?: string;
  "data-testid"?: string;
}

export function ChipFiltro({
  cifra,
  etiqueta,
  activo,
  onClick,
  pendiente = false,
  disabled = false,
  className,
  "aria-label": ariaLabel,
  "data-testid": testId,
}: ChipFiltroProps) {
  return (
    <button
      type="button"
      aria-pressed={activo}
      onClick={onClick}
      disabled={disabled}
      aria-label={ariaLabel}
      data-testid={testId}
      className={cn(
        CHIP_FILTRO_CLS,
        pendiente && !activo && "border-[color:var(--di-highlight-decor)]",
        "group",
        className,
      )}
    >
      {cifra != null ? (
        <strong className="text-base font-bold tabular-nums text-[color:var(--di-text-primary)] group-aria-pressed:text-[color:var(--di-action)]">
          {cifra}
        </strong>
      ) : null}
      <span className="text-[0.8125rem]">{etiqueta}</span>
    </button>
  );
}

export interface OpcionChipFiltro<V extends string> {
  valor: V;
  etiqueta: ReactNode;
  cifra?: ReactNode;
  pendiente?: boolean;
  disabled?: boolean;
}

export interface ChipsFiltroProps<V extends string> {
  opciones: OpcionChipFiltro<V>[];
  /** Valor activo. */
  valor: V;
  onChange: (valor: V) => void;
  /** Nombre del grupo para lectores de pantalla, p. ej. «Filtrar por estado». */
  ariaLabel: string;
  className?: string;
}

export function ChipsFiltro<V extends string>({
  opciones,
  valor,
  onChange,
  ariaLabel,
  className,
}: ChipsFiltroProps<V>) {
  return (
    <div role="group" aria-label={ariaLabel} className={cn(CHIPS_FILTRO_FILA_CLS, className)}>
      {opciones.map((o) => (
        <ChipFiltro
          key={o.valor}
          cifra={o.cifra}
          etiqueta={o.etiqueta}
          activo={o.valor === valor}
          onClick={() => onChange(o.valor)}
          pendiente={o.pendiente}
          disabled={o.disabled}
        />
      ))}
    </div>
  );
}
