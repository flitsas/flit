import { ChevronLeft, ChevronRight } from "lucide-react";

export interface PaginationProps {
  /** Página actual (1-based). */
  page: number;
  pageSize: number;
  totalCount: number;
  onPageChange: (page: number) => void;
  className?: string;
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
}: PaginationProps) {
  const totalPages = Math.max(1, Math.ceil(totalCount / pageSize));
  const from = totalCount === 0 ? 0 : (page - 1) * pageSize + 1;
  const to = Math.min(page * pageSize, totalCount);

  return (
    <nav
      className={`mt-3 flex flex-wrap items-center justify-center gap-x-4 gap-y-2 text-[11px] ${className}`}
      aria-label="Paginación"
    >
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
