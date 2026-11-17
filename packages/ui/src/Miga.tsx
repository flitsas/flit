import type { ReactNode } from "react";
import { cn } from "./cn";

/**
 * Miga de pan del Manual UI Kit 2026 (propuesta-2, `nav.miga`): niveles anteriores en gris, separados por «›», y el
 * actual en azul con una línea de 2 px debajo (`aria-current="page"`). Solo presentación: los textos y los enlaces son
 * los que la pantalla ya tiene (su título y su «Volver a…»); la miga no inventa niveles.
 */
export interface MigaItem {
  etiqueta: ReactNode;
  /** Enlace del nivel. Sin él (o en el último) es texto. */
  href?: string;
  /** Alternativa a `href` cuando el nivel navega con un manejador propio (p. ej. `router.back`). */
  onClick?: () => void;
}

export interface MigaProps {
  items: MigaItem[];
  /** Componente de enlace de la app (p. ej. `next/link`). Por defecto un `<a>`. */
  enlace?: (p: { href: string; className: string; children: ReactNode }) => ReactNode;
  className?: string;
}

const PREVIO_CLS = "text-[color:var(--kit-gris)] no-underline hover:text-[color:var(--di-action)] focus-visible:outline-2 focus-visible:outline-[var(--di-action)]";

export function Miga({ items, enlace, className }: MigaProps) {
  if (items.length === 0) return null;
  return (
    <nav aria-label="Miga de pan" className={cn("kit-miga mb-1 flex flex-wrap items-center gap-1.5 text-sm font-medium", className)}>
      {items.map((it, i) => {
        const ultimo = i === items.length - 1;
        const contenido = ultimo ? (
          <span aria-current="page" className="border-b-2 border-[color:var(--kit-azul-texto)] text-[color:var(--kit-azul-texto)]">
            {it.etiqueta}
          </span>
        ) : it.href ? (
          enlace ? enlace({ href: it.href, className: PREVIO_CLS, children: it.etiqueta }) : <a href={it.href} className={PREVIO_CLS}>{it.etiqueta}</a>
        ) : it.onClick ? (
          <button type="button" onClick={it.onClick} className={cn(PREVIO_CLS, "bg-transparent p-0")}>{it.etiqueta}</button>
        ) : (
          <span className="text-[color:var(--kit-gris)]">{it.etiqueta}</span>
        );
        return (
          <span key={i} className="inline-flex items-center gap-1.5">
            {i > 0 && <span aria-hidden="true" className="text-[color:var(--kit-gris)]">›</span>}
            {contenido}
          </span>
        );
      })}
    </nav>
  );
}
