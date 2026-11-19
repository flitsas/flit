import Link from "next/link";
import type { ReactNode } from "react";

export interface FilaBarra {
  clave: string;
  etiqueta: ReactNode;
  /** Lo que mide la barra. */
  valor: number;
  /** Texto a la derecha (la cifra ya formateada). */
  cifra: ReactNode;
  /** Línea pequeña bajo la etiqueta. */
  detalle?: ReactNode;
  /** Si lleva a algún lado (la bandeja filtrada). */
  href?: string;
  /** Nombre accesible del enlace (la etiqueta sola no dice la cifra). */
  ariaLabel?: string;
  /** Texto completo cuando la etiqueta se corta a dos líneas (descripciones largas de infracciones). */
  titulo?: string;
}

/**
 * Barras horizontales hechas con CSS, sin recharts (rediseño R2): el Inicio las usa para listas cortas (antigüedad,
 * vehículos, infracciones) y así no carga la librería de gráficas para la gestora (HANNAH). Cada fila es texto real —se
 * lee igual con lector de pantalla— y, si tiene `href`, es un enlace con foco visible.
 */
export function BarrasSimples({ filas, color = "var(--kit-azul)", vacio }: { filas: readonly FilaBarra[]; color?: string; vacio?: ReactNode }) {
  const maximo = Math.max(1, ...filas.map((f) => f.valor));
  if (filas.length === 0) return <p className="text-sm text-[color:var(--di-text-tertiary)]">{vacio ?? "Nada por mostrar."}</p>;
  return (
    <ul className="m-0 flex list-none flex-col gap-3 p-0">
      {filas.map((f) => {
        const contenido = (
          <>
            <span className="flex items-baseline justify-between gap-3">
              <span title={f.titulo} className="line-clamp-2 min-w-0 text-sm font-medium text-[color:var(--di-text-primary)]">
                {f.etiqueta}
              </span>
              <span className="shrink-0 text-sm font-semibold tabular-nums text-[color:var(--di-text-primary)]">{f.cifra}</span>
            </span>
            {f.detalle && <span className="text-xs text-[color:var(--di-text-tertiary)]">{f.detalle}</span>}
            <span aria-hidden="true" className="mt-1 block h-2 overflow-hidden rounded-full bg-[var(--kit-hover)]">
              <span className="block h-full rounded-full" style={{ width: `${Math.max(2, (f.valor / maximo) * 100)}%`, background: color }} />
            </span>
          </>
        );
        return (
          <li key={f.clave}>
            {f.href ? (
              <Link
                href={f.href}
                aria-label={f.ariaLabel}
                className="-mx-2 flex flex-col rounded-[9px] px-2 py-1 no-underline hover:bg-[var(--kit-hover)] focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-[var(--kit-azul-texto)]"
              >
                {contenido}
              </Link>
            ) : (
              <div className="flex flex-col">{contenido}</div>
            )}
          </li>
        );
      })}
    </ul>
  );
}
