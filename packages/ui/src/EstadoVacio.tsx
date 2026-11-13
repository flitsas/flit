import { createElement, type ReactNode } from "react";
import { cn } from "./cn";

/**
 * Estado vacío del Manual UI Kit 2026 (p. 39, HU #14562 · UI-1): ícono en disco, título, mensaje y como máximo dos
 * acciones. `anunciar` lo vuelve role="status" para el vacío que aparece al filtrar. Solo tokens de @flit/ui.
 */
export interface EstadoVacioProps {
  titulo: ReactNode;
  mensaje?: ReactNode;
  /** Ícono decorativo (se pinta en el disco del kit, aria-hidden). */
  icono?: ReactNode;
  /** Acciones; se pintan como máximo 2. */
  acciones?: ReactNode[];
  anunciar?: boolean;
  nivelTitulo?: 2 | 3 | 4;
  className?: string;
}

export function EstadoVacio({ titulo, mensaje, icono, acciones, anunciar = false, nivelTitulo = 2, className }: EstadoVacioProps) {
  const visibles = (acciones ?? []).slice(0, 2);
  return (
    <div
      role={anunciar ? "status" : undefined}
      className={cn(
        "flex flex-col items-center gap-3 rounded-[9px] border border-[color:var(--di-border)] bg-[var(--di-surface-elevated)] px-6 py-10 text-center",
        className,
      )}
    >
      {icono ? (
        <span data-parte="icono" aria-hidden="true" className="kit-disco h-14 w-14">
          {icono}
        </span>
      ) : null}
      {createElement(
        `h${nivelTitulo}`,
        { className: "text-lg font-semibold text-[color:var(--di-text-primary)]" },
        titulo,
      )}
      {mensaje ? <p className="max-w-prose text-sm text-[color:var(--di-text-tertiary)]">{mensaje}</p> : null}
      {visibles.length > 0 ? (
        <div data-parte="acciones" className="mt-1 flex flex-wrap items-center justify-center gap-3">
          {visibles}
        </div>
      ) : null}
    </div>
  );
}
