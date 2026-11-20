"use client";

import type { ReactNode } from "react";
import { UiStateBoundary, type UiStatus } from "./UiStateBoundary";
import { Pagination } from "./Pagination";
import {
  TABLA_HEADER_BG,
  TABLA_HEADER_CELL_CLS,
  TABLA_HEADER_FG,
  TABLA_MOVIL_CELL_CLS,
  TABLA_MOVIL_CELL_TITULO_CLS,
  TABLA_MOVIL_FILA_ANCHA_CLS,
  TABLA_MOVIL_ROW_CLS,
  TABLA_MOVIL_TABLE_CLS,
  TABLA_MOVIL_TBODY_CLS,
  TABLA_MOVIL_THEAD_CLS,
  TABLA_ROW_HOVER_CLS,
  TABLA_TARJETA_CLS,
} from "./table-styles";

// HU #10844 — Componente de tabla compartido (patrón canónico card-list de CompanyListTable).
// Encapsula la "cáscara" que cada pantalla reimplementaba a mano y que causaba el
// desalineamiento visual: cabecera-píldora (bg-muted, esquinas redondeadas por extremos),
// filas-tarjeta (bg-card con borde y esquinas redondeadas, separadas por border-spacing),
// scroll horizontal responsive, estados (UiStateBoundary) y la Pagination compartida.
// Bug #13055 — homologada con la tabla de trámites: cabecera y filas salen de `table-styles`
// (cabecera fija al hacer scroll y fila-tarjeta que se realza al pasar el puntero; colores por token)
// y la paginación es la estándar, con «Filas por página» cuando se pasa `onPageSizeChange`.
// HU #14477 — aspecto del prototipo «Diseño Inteligente» (cabecera, realce por borde, tarjetas bajo 900px).

export type CellAlign = "left" | "center" | "right";

export interface DataTableColumn<T> {
  key: string;
  header: ReactNode;
  render: (row: T) => ReactNode;
  align?: CellAlign;
  headerClassName?: string;
  cellClassName?: string;
}

export interface DataTablePagination {
  page: number;
  pageSize: number;
  totalCount: number;
  onPageChange: (page: number) => void;
  onPageSizeChange?: (pageSize: number) => void;
}

export interface DataTableProps<T> {
  columns: DataTableColumn<T>[];
  rows: T[];
  getRowKey: (row: T) => string;
  onRowClick?: (row: T) => void;
  /** Estado de la vista. Si se omite, se infiere `ready`/`empty` según `rows`. */
  status?: UiStatus;
  emptyMessage?: string;
  errorMessage?: string;
  onRetry?: () => void;
  /** Ancho mínimo (px) antes de activar el scroll horizontal. */
  minWidth?: number;
  /**
   * Si es `false`, la tabla ocupa el 100% del contenedor sin scroll horizontal
   * (paneles laterales / drawers). Default: `true` (listados anchos).
   */
  allowHorizontalScroll?: boolean;
  pagination?: DataTablePagination;
  ariaLabel?: string;
  /** Contenido expandible bajo una fila (p. ej. permisos de un módulo RBAC). Si
   *  devuelve algo distinto de null, se renderiza como fila a todo el ancho. */
  renderExpanded?: (row: T) => ReactNode;
  /**
   * Contenido para el cuerpo cuando `rows` está vacío (p. ej. «Ningún registro coincide con los
   * filtros»). Si se pasa, la tabla sigue montada con su `<thead>` (embudos de filtro visibles) y el
   * cuerpo muestra UNA fila `colSpan` con este contenido; sin él, se usa `UiStateBoundary` como siempre.
   * Solo aplica cuando el estado es `ready`/`empty` (no en `loading`/`error`); la paginación se oculta.
   */
  emptyContent?: ReactNode;
  /**
   * HU #14477 — la tabla y su paginador van dentro de una tarjeta (`TABLA_TARJETA_CLS`). `false` cuando la pantalla ya
   * la pinta dentro de una tarjeta, un panel o un modal, para no apilar dos. Default: `true`.
   */
  tarjeta?: boolean;
}

const ALIGN: Record<CellAlign, string> = {
  left: "text-left",
  center: "text-center",
  right: "text-right",
};

export function DataTable<T>({
  columns,
  rows,
  getRowKey,
  onRowClick,
  status,
  emptyMessage,
  errorMessage,
  onRetry,
  minWidth,
  allowHorizontalScroll = true,
  pagination,
  ariaLabel,
  renderExpanded,
  emptyContent,
  tarjeta = true,
}: DataTableProps<T>) {
  // `ready` con 0 filas → `empty`. Sin `status`, se infiere de `rows`.
  const effectiveStatus: UiStatus =
    status === undefined || status === "ready"
      ? rows.length === 0
        ? "empty"
        : "ready"
      : status;
  const last = columns.length - 1;
  const showEmptyContent =
    emptyContent != null &&
    rows.length === 0 &&
    (status === undefined || status === "ready" || status === "empty");

  return (
    <div className={`flex flex-1 flex-col ${tarjeta ? TABLA_TARJETA_CLS : "kit-tabla"}`}>
      <UiStateBoundary
        status={showEmptyContent ? "ready" : effectiveStatus}
        emptyMessage={emptyMessage}
        errorMessage={errorMessage}
        onRetry={onRetry}
        skeletonRows={6}
      >
        <div className={allowHorizontalScroll ? "overflow-x-auto" : "overflow-x-hidden"}>
          {/* HU #14477 — la tabla del prototipo «Diseño Inteligente»: cabecera hundida, filas separadas 6px y, por
              debajo de 900px (tableta y celular, como la media query del prototipo), cada fila se lee como una tarjeta
              en una rejilla de 300px: la cabecera queda para lectores de pantalla, la primera celda hace de título y
              las demás muestran su columna (data-label). Solo con CSS; se anula el ancho mínimo. */}
          <table
            className={`w-full border-separate border-spacing-y-1.5 text-xs ${TABLA_MOVIL_TABLE_CLS} ${
              allowHorizontalScroll ? "" : "table-fixed"
            }`}
            style={allowHorizontalScroll && minWidth ? { minWidth } : undefined}
            aria-label={ariaLabel}
          >
            <thead className={TABLA_MOVIL_THEAD_CLS}>
              <tr>
                {columns.map((col, i) => (
                  <th
                    key={col.key}
                    scope="col"
                    className={`${TABLA_HEADER_CELL_CLS} ${ALIGN[col.align ?? "left"]} ${
                      i === 0 ? "rounded-l-lg" : ""
                    } ${i === last ? "rounded-r-lg" : ""} ${col.headerClassName ?? ""}`}
                    style={{ background: TABLA_HEADER_BG, color: TABLA_HEADER_FG }}
                  >
                    {col.header}
                  </th>
                ))}
              </tr>
            </thead>
            <tbody className={TABLA_MOVIL_TBODY_CLS}>
              {showEmptyContent && (
                <tr className={TABLA_MOVIL_FILA_ANCHA_CLS}>
                  <td colSpan={columns.length} className="px-4 py-6 text-center max-[900px]:block">
                    {emptyContent}
                  </td>
                </tr>
              )}
              {rows.map((row) => {
                const expanded = renderExpanded?.(row);
                return (
                  <FragmentRow
                    key={getRowKey(row)}
                    row={row}
                    columns={columns}
                    last={last}
                    onRowClick={onRowClick}
                    expanded={expanded}
                  />
                );
              })}
            </tbody>
          </table>
        </div>
      </UiStateBoundary>

      {pagination && effectiveStatus === "ready" && !showEmptyContent && (
        <Pagination
          page={pagination.page}
          pageSize={pagination.pageSize}
          totalCount={pagination.totalCount}
          onPageChange={pagination.onPageChange}
          onPageSizeChange={pagination.onPageSizeChange}
          ariaLabel={ariaLabel ? `Paginación de ${ariaLabel.toLowerCase()}` : undefined}
          className="mt-auto"
        />
      )}
    </div>
  );
}

function FragmentRow<T>({
  row,
  columns,
  last,
  onRowClick,
  expanded,
}: {
  row: T;
  columns: DataTableColumn<T>[];
  last: number;
  onRowClick?: (row: T) => void;
  expanded: ReactNode;
}) {
  return (
    <>
      <tr
        className={`bg-card ${TABLA_ROW_HOVER_CLS} ${TABLA_MOVIL_ROW_CLS} ${onRowClick ? "cursor-pointer" : ""}`}
        onClick={onRowClick ? () => onRowClick(row) : undefined}
      >
        {columns.map((col, i) => (
          <td
            key={col.key}
            data-label={typeof col.header === "string" ? col.header : undefined}
            className={`border-y border-[color:var(--table-row-border)] px-3.5 py-3 align-middle ${TABLA_MOVIL_CELL_CLS} ${ALIGN[col.align ?? "left"]} ${
              i === 0 ? `rounded-l-xl border-l ${TABLA_MOVIL_CELL_TITULO_CLS}` : ""
            } ${i === last ? "rounded-r-xl border-r" : ""} ${col.cellClassName ?? ""}`}
          >
            {col.render(row)}
          </td>
        ))}
      </tr>
      {expanded != null && expanded !== false && (
        <tr className={TABLA_MOVIL_FILA_ANCHA_CLS}>
          <td colSpan={columns.length} className="px-4 pb-3 max-[900px]:block">
            {expanded}
          </td>
        </tr>
      )}
    </>
  );
}
