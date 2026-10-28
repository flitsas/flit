"use client";

import { useCallback, useMemo, useState, useSyncExternalStore } from "react";

/**
 * Tamaños de página estándar de TODAS las tablas FLIT. Bug #13055 los unificó (cada pantalla tenía
 * su propio tamaño fijo y ninguna dejaba elegirlo); HU #13916 los fija en 10/20/30/50: el 100 se
 * retira porque alarga la página más allá de lo que se alcanza a leer.
 */
export const TAMANOS_DE_PAGINA = [10, 20, 30, 50] as const;

/** Tamaño con el que abre toda tabla que no tenga uno recordado. */
export const TAMANO_DE_PAGINA_POR_DEFECTO: number = TAMANOS_DE_PAGINA[0];

/** Prefijo de las claves en `sessionStorage`; la clave completa es `flit.filasPorPagina.<tabla>`. */
const PREFIJO_CLAVE = "flit.filasPorPagina.";

/**
 * `useSyncExternalStore` exige una suscripción; aquí no hay ninguna a la que suscribirse —
 * `sessionStorage` solo lo cambia esta misma pantalla, y cuando lo hace ya actualiza su estado.
 */
function suscripcionInerte(): () => void {
  return () => {};
}

/**
 * Lee el tamaño recordado de una tabla. Un valor que ya no está en {@link TAMANOS_DE_PAGINA} (un 25
 * o un 100 de antes de HU #13916, o basura) vuelve al de por defecto en vez de pintar un selector
 * que no lo ofrece. Con guarda: en una ventana privada el acceso puede lanzar.
 */
export function leerTamanoDePaginaGuardado(clave: string): number {
  try {
    const guardado = Number(sessionStorage.getItem(PREFIJO_CLAVE + clave));
    return (TAMANOS_DE_PAGINA as readonly number[]).includes(guardado) ? guardado : TAMANO_DE_PAGINA_POR_DEFECTO;
  } catch {
    return TAMANO_DE_PAGINA_POR_DEFECTO;
  }
}

/** Recuerda el tamaño de una tabla para el resto de la sesión. */
export function guardarTamanoDePagina(clave: string, tamano: number): void {
  try {
    sessionStorage.setItem(PREFIJO_CLAVE + clave, String(tamano));
  } catch {
    // Ventana privada o almacenamiento bloqueado: el tamaño vale para esta pantalla y no se
    // recuerda. Es una comodidad, no un dato: no merece un aviso.
  }
}

/**
 * Tamaño de página recordado de una tabla que maneja su propia página (p. ej. la de trámites, cuya
 * página vive junto a los filtros). Devuelve el tamaño vigente y el cambio, que lo recuerda; volver
 * a la página 1 queda a cargo de quien lo usa.
 */
export function useTamanoDePagina(
  clave: string | null,
  tamanoInicial: number = TAMANO_DE_PAGINA_POR_DEFECTO,
): [number, (tamano: number) => void] {
  // Se lee con `useSyncExternalStore` y no en el inicializador de un `useState` porque
  // `sessionStorage` no existe en el render del servidor: leerlo ahí daría una hidratación distinta
  // a la del cliente.
  const guardado = useSyncExternalStore(
    suscripcionInerte,
    () => (clave === null ? tamanoInicial : leerTamanoDePaginaGuardado(clave)),
    () => (clave === null ? tamanoInicial : TAMANO_DE_PAGINA_POR_DEFECTO),
  );
  const [elegido, setElegido] = useState<number | null>(null);
  const cambiar = useCallback(
    (tamano: number) => {
      setElegido(tamano);
      if (clave !== null) guardarTamanoDePagina(clave, tamano);
    },
    [clave],
  );
  return [elegido ?? guardado, cambiar];
}

export interface PaginacionEstado {
  /** Página actual, 1-based. */
  page: number;
  pageSize: number;
  setPage: (page: number) => void;
  /** Cambia el tamaño, lo recuerda para la sesión y vuelve a la página 1. */
  setPageSize: (pageSize: number) => void;
  /** Recorta una lista ya cargada a la página actual (paginación en cliente). */
  paginar: <T>(items: readonly T[]) => T[];
}

/**
 * Estado de paginación de una tabla, con «Filas por página». Sirve igual a la paginación en cliente
 * (con {@link PaginacionEstado.paginar}) y a la de servidor (pasando `page`/`pageSize` a la API).
 *
 * `clave` identifica la tabla para recordar su tamaño durante la sesión (HU #13917): quien eligió 50
 * en Usuarios lo encuentra al volver, sin que eso cambie el de Auditoría. Es única por tabla
 * —`"<modulo>.<tabla>"`—, no por pantalla: una pantalla con dos tablas lleva dos claves. En FLIT 2.0
 * la clave es obligatoria (la exige la guarda del estándar de tablas, HU #13920); el número —tamaño
 * inicial sin recuerdo— se conserva para las otras apps de la suite que aún lo usan.
 *
 * Uso de ejemplo:
 * ```tsx
 * const pg = usePaginacion("usuarios.lista");
 * const visibles = pg.paginar(filas);
 * <Pagination page={pg.page} pageSize={pg.pageSize} totalCount={filas.length}
 *             onPageChange={pg.setPage} onPageSizeChange={pg.setPageSize} />
 * ```
 */
export function usePaginacion(clave?: string | number): PaginacionEstado {
  const [page, setPage] = useState(1);
  const [pageSize, cambiarTamano] = useTamanoDePagina(
    typeof clave === "string" ? clave : null,
    typeof clave === "number" ? clave : TAMANO_DE_PAGINA_POR_DEFECTO,
  );

  const setPageSize = useCallback(
    (size: number) => {
      cambiarTamano(size);
      setPage(1);
    },
    [cambiarTamano],
  );

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
