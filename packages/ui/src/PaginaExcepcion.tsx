import type { ElementType, ReactNode } from "react";
import { cn } from "./cn";
import { buttonClass } from "./Button";

/**
 * Página de excepción 403/404 del Manual UI Kit 2026 (HU #14562 · UI-1): código grande, un único h1, mensaje y una
 * acción como enlace. `enlace` permite pasar `Link` de next. Solo tokens de @flit/ui.
 */
export interface PaginaExcepcionProps {
  codigo: 403 | 404;
  titulo: ReactNode;
  mensaje: ReactNode;
  accion: { etiqueta: string; href: string };
  enlace?: ElementType;
  className?: string;
}

export function PaginaExcepcion({ codigo, titulo, mensaje, accion, enlace: Enlace = "a", className }: PaginaExcepcionProps) {
  return (
    <section
      data-codigo={String(codigo)}
      className={cn("mx-auto flex max-w-xl flex-col items-center gap-4 px-6 py-16 text-center", className)}
    >
      <p className="text-[4rem] font-bold leading-none tabular-nums text-[color:var(--kit-azul-texto)]">{codigo}</p>
      <h1 className="text-[2rem] font-bold leading-tight text-[color:var(--di-heading)]">{titulo}</h1>
      <p className="text-base text-[color:var(--di-text-tertiary)]">{mensaje}</p>
      <Enlace href={accion.href} className={buttonClass({ variant: "primary" })}>
        {accion.etiqueta}
      </Enlace>
    </section>
  );
}
