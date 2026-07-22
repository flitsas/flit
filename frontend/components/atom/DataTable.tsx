"use client";

import type { ReactNode } from "react";
import { UiStateBoundary, type UiStatus } from "@/components/admin/UiStateBoundary";
import { Pagination } from "./Pagination";

// HU #10844 — Componente de tabla compartido (patrón canónico card-list de CompanyListTable).
// Encapsula la "cáscara" que cada pantalla reimplementaba a mano y que causaba el
// desalineamiento visual: cabecera-píldora (bg-muted, esquinas redondeadas por extremos),
// filas-tarjeta (bg-card con borde y esquinas redondeadas, separadas por border-spacing),
// scroll horizontal responsive, estados (UiStateBoundary) y la Pagination compartida.
// Usa tokens (bg-muted/bg-card/text-foreground/border) → theme-aware por defecto.

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
  pagination?: DataTablePagination;
  ariaLabel?: string;
  /** Contenido expandible bajo una fila (p. ej. permisos de un módulo RBAC). Si
   *  devuelve algo distinto de null, se renderiza como fila a todo el ancho. */
  renderExpanded?: (row: T) => ReactNode;
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
  pagination,
  ariaLabel,
  renderExpanded,
}: DataTableProps<T>) {
  // `ready` con 0 filas → `empty`. Sin `status`, se infiere de `rows`.
  const effectiveStatus: UiStatus =
    status === undefined || status === "ready"
      ? rows.length === 0
        ? "empty"
        : "ready"
      : status;
  const last = columns.length - 1;

  return (
    <div className="flex flex-1 flex-col">
      <UiStateBoundary
        status={effectiveStatus}
        emptyMessage={emptyMessage}
        errorMessage={errorMessage}
        onRetry={onRetry}
        skeletonRows={6}
      >
        <div className="overflow-x-auto">
          <table
            className="w-full border-separate border-spacing-y-2 text-xs"
            style={minWidth ? { minWidth } : undefined}
            aria-label={ariaLabel}
          >
            <thead>
              <tr className="text-[10px] font-semibold uppercase text-foreground">
                {columns.map((col, i) => (
                  <th
                    key={col.key}
                    className={`bg-muted px-4 py-2.5 ${ALIGN[col.align ?? "left"]} ${
                      i === 0 ? "rounded-l-xl" : ""
                    } ${i === last ? "rounded-r-xl" : ""} ${col.headerClassName ?? ""}`}
                  >
                    {col.header}
                  </th>
                ))}
              </tr>
            </thead>
            <tbody>
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

      {pagination && effectiveStatus === "ready" && (
        <Pagination
          page={pagination.page}
          pageSize={pagination.pageSize}
          totalCount={pagination.totalCount}
          onPageChange={pagination.onPageChange}
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
        className={`bg-card ${onRowClick ? "cursor-pointer" : ""}`}
        onClick={onRowClick ? () => onRowClick(row) : undefined}
      >
        {columns.map((col, i) => (
          <td
            key={col.key}
            className={`border-y px-4 py-3 ${ALIGN[col.align ?? "left"]} ${
              i === 0 ? "rounded-l-xl border-l" : ""
            } ${i === last ? "rounded-r-xl border-r" : ""} ${col.cellClassName ?? ""}`}
          >
            {col.render(row)}
          </td>
        ))}
      </tr>
      {expanded != null && expanded !== false && (
        <tr>
          <td colSpan={columns.length} className="px-4 pb-3">
            {expanded}
          </td>
        </tr>
      )}
    </>
  );
}
