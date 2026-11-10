"use client";

import type { ReactNode } from "react";
import { UiStateBoundary, type UiStatus } from "./UiStateBoundary";
import { Pagination } from "./Pagination";
import {
  TABLA_HEADER_BG,
  TABLA_HEADER_CELL_CLS,
  TABLA_HEADER_FG,
  TABLA_ROW_HOVER_CLS,
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
}

// Tarjeta de fila por debajo de 900px (media query de tabla del prototipo): borde propio en el `<tr>`, que es el
// que cambia al pasar el puntero; las celdas pierden el suyo.
const ROW_CARD_CLS =
  "max-[900px]:block max-[900px]:rounded-xl max-[900px]:border max-[900px]:border-[color:var(--di-border)] " +
  "max-[900px]:bg-[var(--di-surface-elevated)] max-[900px]:px-3.5 max-[900px]:pt-1 max-[900px]:pb-2 " +
  "max-[900px]:hover:border-[color:var(--di-border-control)]";

// Cada celda en la tarjeta: etiqueta de la columna a la izquierda (data-label), valor a la derecha.
const CELL_CARD_CLS =
  "max-[900px]:flex max-[900px]:items-center max-[900px]:justify-between max-[900px]:gap-3 " +
  "max-[900px]:rounded-none max-[900px]:border-0 max-[900px]:px-0 max-[900px]:py-[5px] max-[900px]:text-right " +
  "max-[900px]:text-[0.8125rem] max-[900px]:bg-transparent max-[900px]:before:shrink-0 max-[900px]:before:text-left " +
  "max-[900px]:before:text-xs max-[900px]:before:font-medium max-[900px]:before:text-[color:var(--di-text-tertiary)] " +
  "max-[900px]:before:content-[attr(data-label)]";

// La primera celda hace de título de la tarjeta, con un divisor debajo.
const FIRST_CELL_CARD_CLS =
  "max-[900px]:mb-1 max-[900px]:border-b max-[900px]:border-[color:var(--di-divider)] max-[900px]:py-2.5 " +
  "max-[900px]:text-sm max-[900px]:font-semibold";

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
    <div className="flex flex-1 flex-col">
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
            className={`w-full border-separate border-spacing-y-1.5 text-xs max-[900px]:block max-[900px]:min-w-0! ${
              allowHorizontalScroll ? "" : "table-fixed"
            }`}
            style={allowHorizontalScroll && minWidth ? { minWidth } : undefined}
            aria-label={ariaLabel}
          >
            <thead className="max-[900px]:sr-only">
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
            <tbody className="max-[900px]:grid max-[900px]:grid-cols-[repeat(auto-fill,minmax(min(300px,100%),1fr))] max-[900px]:gap-2.5">
              {showEmptyContent && (
                <tr className="max-[900px]:col-span-full max-[900px]:block">
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
        className={`bg-card ${TABLA_ROW_HOVER_CLS} ${ROW_CARD_CLS} ${onRowClick ? "cursor-pointer" : ""}`}
        onClick={onRowClick ? () => onRowClick(row) : undefined}
      >
        {columns.map((col, i) => (
          <td
            key={col.key}
            data-label={typeof col.header === "string" ? col.header : undefined}
            className={`border-y border-[color:var(--table-row-border)] px-3.5 py-3 align-middle ${CELL_CARD_CLS} ${ALIGN[col.align ?? "left"]} ${
              i === 0 ? `rounded-l-xl border-l ${FIRST_CELL_CARD_CLS}` : ""
            } ${i === last ? "rounded-r-xl border-r" : ""} ${col.cellClassName ?? ""}`}
          >
            {col.render(row)}
          </td>
        ))}
      </tr>
      {expanded != null && expanded !== false && (
        <tr className="max-[900px]:col-span-full max-[900px]:block">
          <td colSpan={columns.length} className="px-4 pb-3 max-[900px]:block">
            {expanded}
          </td>
        </tr>
      )}
    </>
  );
}
