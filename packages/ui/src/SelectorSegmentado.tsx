"use client";

import { useRef, type KeyboardEvent } from "react";
import { cn } from "./cn";

/**
 * Selector segmentado (HU #14562 · UI-1): `role="radiogroup"` con tabulación itinerante. Solo la opción marcada está
 * en el orden de Tab; las flechas mueven la selección y el foco, con vuelta al principio o al final.
 */
export interface OpcionSegmentada<T extends string> {
  valor: T;
  label: string;
}

export interface SelectorSegmentadoProps<T extends string> {
  etiqueta: string;
  opciones: OpcionSegmentada<T>[];
  valor: T;
  onCambio: (valor: T) => void;
  deshabilitado?: boolean;
  className?: string;
}

export function SelectorSegmentado<T extends string>({
  etiqueta,
  opciones,
  valor,
  onCambio,
  deshabilitado = false,
  className,
}: SelectorSegmentadoProps<T>) {
  const refs = useRef<(HTMLButtonElement | null)[]>([]);
  const indiceActual = Math.max(
    0,
    opciones.findIndex((o) => o.valor === valor),
  );

  const mover = (desde: number, paso: number) => {
    const n = opciones.length;
    if (n === 0) return;
    const destino = (desde + paso + n) % n;
    onCambio(opciones[destino].valor);
    refs.current[destino]?.focus();
  };

  const alTeclear = (e: KeyboardEvent<HTMLButtonElement>, i: number) => {
    if (deshabilitado) return;
    if (e.key === "ArrowRight" || e.key === "ArrowDown") {
      e.preventDefault();
      mover(i, 1);
    } else if (e.key === "ArrowLeft" || e.key === "ArrowUp") {
      e.preventDefault();
      mover(i, -1);
    } else if (e.key === "Home") {
      e.preventDefault();
      mover(0, 0);
    } else if (e.key === "End") {
      e.preventDefault();
      mover(opciones.length - 1, 0);
    }
  };

  return (
    <div
      role="radiogroup"
      aria-label={etiqueta}
      aria-disabled={deshabilitado || undefined}
      className={cn(
        "inline-flex flex-wrap gap-1 rounded-full border border-[color:var(--di-border)] bg-[var(--di-surface-sunken)] p-1",
        className,
      )}
    >
      {opciones.map((o, i) => {
        const marcada = i === indiceActual;
        return (
          <button
            key={o.valor}
            ref={(el) => {
              refs.current[i] = el;
            }}
            type="button"
            role="radio"
            aria-checked={marcada ? "true" : "false"}
            tabIndex={marcada ? 0 : -1}
            disabled={deshabilitado}
            onClick={() => {
              if (!deshabilitado) onCambio(o.valor);
            }}
            onKeyDown={(e) => alTeclear(e, i)}
            className={cn(
              "min-h-9 rounded-full px-4 text-sm font-semibold motion-safe:transition-colors motion-safe:duration-[var(--di-motion-fast)] focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-[var(--di-action)] disabled:opacity-60",
              marcada
                ? "bg-[var(--di-surface)] text-[color:var(--di-action)] shadow-[var(--kit-sombra)]"
                : "text-[color:var(--di-text-secondary)] hover:bg-[color:var(--kit-hover)]",
            )}
          >
            {o.label}
          </button>
        );
      })}
    </div>
  );
}
