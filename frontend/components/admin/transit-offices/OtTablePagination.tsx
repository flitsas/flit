"use client";

import { Pagination } from "@/components/atom/Pagination";

/**
 * Bug #13055 — paginación homologada con el modelo de trámites. Reexport fino de `Pagination`
 * (numerada, «Filas por página» y «Mostrando X–Y de N»); se conserva la firma para no tocar a los
 * consumidores que aún la importan. Con `onPageSizeChange` muestra el selector de filas.
 */
export interface OtTablePaginationProps {
  totalCount: number;
  page: number;
  pageSize: number;
  onPageChange: (page: number) => void;
  onPageSizeChange?: (pageSize: number) => void;
  ariaLabel?: string;
}

export function OtTablePagination({
  totalCount,
  page,
  pageSize,
  onPageChange,
  onPageSizeChange,
  ariaLabel = "Paginación de trámites del organismo",
}: OtTablePaginationProps) {
  return (
    <Pagination
      page={page}
      pageSize={pageSize}
      totalCount={totalCount}
      onPageChange={onPageChange}
      onPageSizeChange={onPageSizeChange}
      ariaLabel={ariaLabel}
      noun="trámites"
      className="mt-auto"
    />
  );
}
