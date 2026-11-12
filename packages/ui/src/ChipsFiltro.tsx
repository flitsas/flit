"use client";

import type { ReactNode } from "react";
import { cn } from "./cn";

/**
 * Chips que filtran, con la forma del Manual UI Kit 2026 (propuesta-2):
 *  - con cifra, casillas del embudo de estados (`.embudo`): etiqueta y cifra en columna dentro de un contenedor con
 *    borde de radio 26 px; la activa con fondo --kit-hover, aro azul y cifra azul; la pendiente, en naranja;
 *  - sin cifra, píldoras de 36 px (`.filtro-si`) con contorno azul cuando están activas.
 * El chip activo lleva `aria-pressed="true"`. {@link ChipFiltro} suelto o {@link ChipsFiltro}, la fila con `role="group"`.
 */

/** Chip sin cifra: píldora de 36 px con contorno azul cuando está activo. */
export const CHIP_FILTRO_CLS =
  "inline-flex min-h-9 items-center gap-2 rounded-full border px-4 cursor-pointer text-sm font-semibold transition-colors " +
  "border-[color:var(--di-border)] bg-[var(--di-surface-elevated)] text-[color:var(--di-text-primary)] " +
  "hover:bg-[var(--kit-hover)] focus:outline-none focus-visible:outline-2 focus-visible:outline-offset-2 " +
  "focus-visible:outline-[var(--di-action)] " +
  "aria-pressed:border-[color:var(--di-action)] aria-pressed:bg-[var(--kit-hover)] " +
  "aria-pressed:text-[color:var(--di-action)] " +
  "disabled:cursor-default disabled:opacity-55 disabled:hover:bg-[var(--di-surface-elevated)]";

/** Chip con cifra: casilla del embudo (columna con etiqueta y cifra). */
export const CHIP_EMBUDO_CLS =
  "flex min-w-0 flex-col-reverse items-center justify-center gap-0.5 rounded-[9px] border-0 bg-transparent px-1 py-1.5 " +
  "cursor-pointer text-center transition-colors text-[color:var(--di-text-primary)] hover:bg-[var(--kit-hover)] " +
  "focus:outline-none focus-visible:outline-2 focus-visible:outline-[var(--di-action)] " +
  "aria-pressed:bg-[var(--kit-hover)] aria-pressed:shadow-[inset_0_0_0_1px_var(--kit-azul-texto)] " +
  "disabled:cursor-default disabled:opacity-55";

/** Fila de chips sin cifra. */
export const CHIPS_FILTRO_FILA_CLS = "flex flex-wrap gap-2";
/** Embudo: casillas iguales en un contenedor con borde (radio 9 px y deslizamiento en celular). */
export const EMBUDO_CLS =
  "grid grid-cols-[repeat(auto-fit,minmax(92px,1fr))] gap-1 rounded-[26px] border border-[color:var(--di-border)] " +
  "bg-[var(--di-surface-elevated)] p-2 max-[640px]:flex max-[640px]:snap-x max-[640px]:overflow-x-auto " +
  "max-[640px]:rounded-[9px] max-[640px]:[&>*]:w-[86px] max-[640px]:[&>*]:shrink-0 max-[640px]:[&>*]:snap-start";

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
        cifra != null ? CHIP_EMBUDO_CLS : CHIP_FILTRO_CLS,
        pendiente && !activo && (cifra != null ? "text-[color:var(--badge-warning-fg)]" : "border-[color:var(--kit-naranja)]"),
        "group",
        className,
      )}
    >
      {cifra != null ? (
        // Orden del DOM = nombre accesible de siempre («15 Todos»); `flex-col-reverse` pone la etiqueta arriba, como el kit.
        <>
          <strong className="text-xl font-bold tabular-nums group-aria-pressed:text-[color:var(--kit-azul-texto)]">{cifra}</strong>
          <span className="text-xs font-medium">{etiqueta}</span>
        </>
      ) : (
        <span>{etiqueta}</span>
      )}
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
    <div
      role="group"
      aria-label={ariaLabel}
      className={cn(opciones.some((o) => o.cifra != null) ? EMBUDO_CLS : CHIPS_FILTRO_FILA_CLS, className)}
    >
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
