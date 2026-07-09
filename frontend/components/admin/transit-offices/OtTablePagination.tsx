"use client";

import { Pagination } from "@/components/atom/Pagination";

/**
 * Paginación server-side de las tablas OT. Delega en el componente unificado
 * `Pagination` (HU #10495) — una sola implementación centrada, "Mostrando X–Y de N".
 * Se conserva la firma para no tocar a los consumidores (ClientProceduresTable,
 * bitácora de WebhooksSection).
 */
export interface OtTablePaginationProps {
  totalCount: number;
  page: number;
  pageSize: number;
  onPageChange: (page: number) => void;
}

export function OtTablePagination(props: OtTablePaginationProps) {
  return <Pagination {...props} className="mt-auto" />;
}
