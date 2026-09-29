"use client";

import { useCallback, useMemo, useState } from "react";

/**
 * Tamaños de página estándar de TODAS las tablas FLIT (los del listado de trámites). Bug #13055:
 * cada pantalla tenía su propio tamaño fijo y ninguna dejaba elegirlo.
 */
export const TAMANOS_DE_PAGINA = [10, 25, 50, 100] as const;

export interface PaginacionEstado {
  /** Página actual, 1-based. */
  page: number;
  pageSize: number;
  setPage: (page: number) => void;
  /** Cambia el tamaño y vuelve a la página 1 (la página N de otro tamaño es otra ventana de datos). */
  setPageSize: (pageSize: number) => void;
  /** Recorta una lista ya cargada a la página actual (paginación en cliente). */
  paginar: <T>(items: readonly T[]) => T[];
}

/**
 * Estado de paginación de una tabla, con «Filas por página». Sirve igual a la paginación en cliente
 * (con {@link PaginacionEstado.paginar}) y a la de servidor (pasando `page`/`pageSize` a la API).
 *
 * Uso de ejemplo:
 * ```tsx
 * const pg = usePaginacion();
 * const visibles = pg.paginar(filas);
 * <Pagination page={pg.page} pageSize={pg.pageSize} totalCount={filas.length}
 *             onPageChange={pg.setPage} onPageSizeChange={pg.setPageSize} />
 * ```
 */
export function usePaginacion(tamanoInicial: number = TAMANOS_DE_PAGINA[0]): PaginacionEstado {
  const [page, setPage] = useState(1);
  const [pageSize, setPageSizeState] = useState(tamanoInicial);

  const setPageSize = useCallback((size: number) => {
    setPageSizeState(size);
    setPage(1);
  }, []);

  const paginar = useCallback(
    <T,>(items: readonly T[]): T[] => {
      // Si la lista se achicó (un filtro, un borrado) la página actual puede quedar fuera: se
      // muestra la última que existe en vez de una tabla vacía.
      const ultima = Math.max(1, Math.ceil(items.length / pageSize));
      const actual = Math.min(page, ultima);
      return items.slice((actual - 1) * pageSize, actual * pageSize);
    },
    [page, pageSize],
  );

  return useMemo(
    () => ({ page, pageSize, setPage, setPageSize, paginar }),
    [page, pageSize, setPageSize, paginar],
  );
}
