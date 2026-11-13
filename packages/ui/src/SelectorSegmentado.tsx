"use client";

import { useRef, type KeyboardEvent } from "react";
import { cn } from "./cn";

/**
 * Selector segmentado (HU #14562 · UI-1): `role="radiogroup"` con tabulación itinerante. Solo la opción marcada está
 * en el orden de Tab; las flechas mueven la selección y el foco, con vuelta al principio o al final.
 *
 * HU #14562 · M10 (auditoría de la oleada), todo opcional y retrocompatible:
 * - `describedBy`: id de la ayuda o del error visible, va a `aria-describedby` del radiogroup.
 * - `etiquetadoPor`: id del rótulo visible, va a `aria-labelledby` y reemplaza a `etiqueta`, para no duplicar el nombre.
 * - `bloqueado`: el valor no cambia, pero el selector sigue enfocable. Grupo y opciones llevan `aria-disabled` y la
 *   opción marcada queda en tabIndex=0, así teclado y lector llegan a él y oyen el motivo en la descripción. Sirve,
 *   por ejemplo, para «la cabecera del lote ya se creó».
 * - `deshabilitado` sigue siendo el caso inerte (sin permiso): botones `disabled`, fuera del orden de Tab.
 */
export interface OpcionSegmentada<T extends string> {
  valor: T;
  label: string;
}

interface SelectorSegmentadoBase<T extends string> {
  opciones: OpcionSegmentada<T>[];
  valor: T;
  onCambio: (valor: T) => void;
  deshabilitado?: boolean;
  bloqueado?: boolean;
  describedBy?: string;
  className?: string;
}

export type SelectorSegmentadoProps<T extends string> = SelectorSegmentadoBase<T> &
  ({ etiqueta: string; etiquetadoPor?: undefined } | { etiquetadoPor: string; etiqueta?: undefined });

export function SelectorSegmentado<T extends string>({
  etiqueta,
  etiquetadoPor,
  opciones,
  valor,
  onCambio,
  deshabilitado = false,
  bloqueado = false,
  describedBy,
  className,
}: SelectorSegmentadoProps<T>) {
  const inactivo = deshabilitado || bloqueado;
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
    if (inactivo) return;
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
      aria-label={etiquetadoPor ? undefined : etiqueta}
      aria-labelledby={etiquetadoPor}
      aria-describedby={describedBy}
      aria-disabled={inactivo || undefined}
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
            aria-disabled={bloqueado && !deshabilitado ? "true" : undefined}
            onClick={() => {
              if (!inactivo) onCambio(o.valor);
            }}
            onKeyDown={(e) => alTeclear(e, i)}
            className={cn(
              "min-h-9 rounded-full px-4 text-sm font-semibold motion-safe:transition-colors motion-safe:duration-[var(--di-motion-fast)] focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-[var(--di-action)] disabled:opacity-60 aria-disabled:cursor-not-allowed aria-disabled:opacity-60 aria-disabled:hover:bg-transparent",
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
