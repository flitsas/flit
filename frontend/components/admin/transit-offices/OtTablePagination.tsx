"use client";

import { PageNav } from "@/components/atom/PageNav";

/**
 * Paginación server-side de las tablas OT. Delega en `PageNav`, la MISMA pieza que usa el listado
 * del gestor: páginas numeradas con elipsis, alineadas a la derecha, en el azul de marca sobre su
 * propio tinte — que es la paginación del diseño.
 *
 * Antes delegaba en `Pagination`, centrada y con "Anterior / Siguiente". Las dos hacían lo mismo
 * con distinta cara, así que la bandeja y el listado del gestor paginaban de dos formas.
 *
 * Se conserva la firma (`totalCount` + `pageSize`) para no tocar a los consumidores
 * (ClientProceduresTable, bitácora de WebhooksSection); el total de páginas y la línea de conteo
 * se derivan aquí.
 */
export interface OtTablePaginationProps {
  totalCount: number;
  page: number;
  pageSize: number;
  onPageChange: (page: number) => void;
}

export function OtTablePagination({
  totalCount,
  page,
  pageSize,
  onPageChange,
}: OtTablePaginationProps) {
  const totalPages = Math.max(1, Math.ceil(totalCount / pageSize));
  const desde = totalCount === 0 ? 0 : (page - 1) * pageSize + 1;
  const hasta = Math.min(page * pageSize, totalCount);

  return (
    <PageNav
      page={page}
      totalPages={totalPages}
      onPageChange={onPageChange}
      // Rango y no un solo número: la bandeja pagina en SERVIDOR, así que "Mostrando 5 de 420"
      // no diría en qué punto de las 420 se está.
      resumen={`Mostrando ${desde}–${hasta} de ${totalCount}`}
      ariaLabel="Paginación de trámites del organismo"
      className="mt-auto"
    />
  );
}
