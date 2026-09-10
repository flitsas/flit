import { ChevronLeft, ChevronRight } from "lucide-react";

export interface PaginationProps {
  /** Página actual (1-based). */
  page: number;
  pageSize: number;
  totalCount: number;
  onPageChange: (page: number) => void;
  className?: string;
  /**
   * Tamaños de página que el usuario puede elegir («Filas por página», como en /tramites). Solo se
   * muestra el selector si vienen las opciones y el manejador; el cambio debe volver a la página 1.
   */
  pageSizeOptions?: readonly number[];
  onPageSizeChange?: (pageSize: number) => void;
}

/**
 * Paginación unificada (HU #10495). Centrada respecto a la tabla (regla del
 * feedback de diseño), con el conteo "Mostrando X–Y de N". La usan las tablas
 * server-side y client-side por igual. Se oculta cuando todo cabe en una página.
 */
export function Pagination({
  page,
  pageSize,
  totalCount,
  onPageChange,
  className = "",
  pageSizeOptions,
  onPageSizeChange,
}: PaginationProps) {
  const totalPages = Math.max(1, Math.ceil(totalCount / pageSize));
  const from = totalCount === 0 ? 0 : (page - 1) * pageSize + 1;
  const to = Math.min(page * pageSize, totalCount);

  return (
    <nav
      className={`mt-3 flex flex-wrap items-center justify-center gap-x-4 gap-y-2 text-[11px] ${className}`}
      aria-label="Paginación"
    >
      {pageSizeOptions && onPageSizeChange && (
        <label className="flex items-center gap-2 opacity-70">
          Filas por página
          <select
            value={pageSize}
            onChange={(e) => onPageSizeChange(Number(e.target.value))}
            aria-label="Filas por página"
            className="rounded-lg border border-[#DFE5ED] bg-white px-2 py-1 text-[11px] text-[#162744] dark:border-white/10 dark:bg-[#0B0F14] dark:text-white"
          >
            {pageSizeOptions.map((n) => (
              <option key={n} value={n}>
                {n}
              </option>
            ))}
          </select>
        </label>
      )}
      <p className="opacity-60" role="status" aria-live="polite">
        Mostrando {from}–{to} de {totalCount}
      </p>
      {totalPages > 1 && (
      <div className="flex items-center gap-2">
        <button
          type="button"
          aria-label="Página anterior"
          disabled={page <= 1}
          onClick={() => onPageChange(page - 1)}
          className="flex items-center gap-1 rounded-lg border px-2.5 py-1.5 font-medium transition disabled:opacity-40"
        >
          <ChevronLeft className="h-3.5 w-3.5" /> Anterior
        </button>
        <span className="font-semibold tabular-nums" style={{ color: "#557EFF" }}>
          {page} / {totalPages}
        </span>
        <button
          type="button"
          aria-label="Página siguiente"
          disabled={page >= totalPages}
          onClick={() => onPageChange(page + 1)}
          className="flex items-center gap-1 rounded-lg border border-[#557EFF] px-2.5 py-1.5 font-medium transition disabled:opacity-40"
          style={{ color: "#557EFF" }}
        >
          Siguiente <ChevronRight className="h-3.5 w-3.5" />
        </button>
      </div>
      )}
    </nav>
  );
}
