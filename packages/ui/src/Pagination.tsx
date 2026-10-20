"use client";

import { PageNav } from "./PageNav";
import { TAMANOS_DE_PAGINA } from "./usePaginacion";

export interface PaginationProps {
  /** Página actual (1-based). */
  page: number;
  pageSize: number;
  totalCount: number;
  onPageChange: (page: number) => void;
  className?: string;
  /**
   * Cambio de «Filas por página». Con este manejador se muestra el selector (el estándar: todas las
   * tablas lo llevan, Bug #13055); el cambio debe volver a la página 1 — `usePaginacion` ya lo hace.
   */
  onPageSizeChange?: (pageSize: number) => void;
  /** Tamaños ofrecidos. Por defecto los de trámites: 10, 25, 50, 100. */
  pageSizeOptions?: readonly number[];
  /** Nombre accesible de la navegación; distingue varias paginaciones en una misma pantalla. */
  ariaLabel?: string;
  /** Qué se cuenta, para la línea vacía («Sin representantes que mostrar»). Default: «registros». */
  noun?: string;
}

/** Mismo selector que el listado de trámites (control de 36 px, borde neutro). */
const SELECT_CLS =
  "inline-flex h-9 shrink-0 items-center rounded-xl border border-[#DFE5ED] bg-white px-3 text-xs " +
  "font-semibold text-[#1E293B] transition hover:bg-[#EFF6FF] focus:outline-none focus-visible:ring-2 " +
  "focus-visible:ring-[#557EFF] focus-visible:ring-offset-2 dark:border-white/15 dark:bg-[#0B0F14] dark:text-white";

/**
 * Paginación ESTÁNDAR de las tablas FLIT (Bug #13055): la misma del listado de trámites.
 * «Filas por página» a la izquierda y, a la derecha, «Mostrando X–Y de N» con la navegación
 * numerada ‹ 1 2 … N › ({@link PageNav}). Antes había dos paginaciones: esta solo ofrecía
 * Anterior / Siguiente con «1 / 39», y la de trámites, numerada; cada módulo se veía distinto.
 *
 * El conteo se muestra siempre; los botones de página, solo si hay más de una.
 */
export function Pagination({
  page,
  pageSize,
  totalCount,
  onPageChange,
  className = "",
  onPageSizeChange,
  pageSizeOptions = TAMANOS_DE_PAGINA,
  ariaLabel = "Paginación",
  noun = "registros",
}: PaginationProps) {
  const totalPages = Math.max(1, Math.ceil(totalCount / pageSize));
  const actual = Math.min(page, totalPages);
  const from = totalCount === 0 ? 0 : (actual - 1) * pageSize + 1;
  const to = Math.min(actual * pageSize, totalCount);
  // Un tamaño que no está en la lista (una pantalla con tamaño propio) se ofrece igual, para que el
  // selector no mienta sobre lo que se está mostrando.
  const opciones = pageSizeOptions.includes(pageSize)
    ? pageSizeOptions
    : [...pageSizeOptions, pageSize].sort((a, b) => a - b);

  return (
    <div className={`flex flex-wrap items-center justify-between gap-4 ${className}`}>
      {onPageSizeChange ? (
        <label className="flex items-center gap-2 pt-3 text-xs opacity-70">
          Filas por página
          <select
            value={pageSize}
            onChange={(e) => onPageSizeChange(Number(e.target.value))}
            className={SELECT_CLS}
            aria-label="Filas por página"
          >
            {opciones.map((n) => (
              <option key={n} value={n}>
                {n}
              </option>
            ))}
          </select>
        </label>
      ) : (
        <span />
      )}
      <PageNav
        page={actual}
        totalPages={totalPages}
        resumen={totalCount === 0 ? `Sin ${noun} que mostrar` : `Mostrando ${from}–${to} de ${totalCount}`}
        ariaLabel={ariaLabel}
        onPageChange={onPageChange}
        className="flex-1"
      />
    </div>
  );
}
