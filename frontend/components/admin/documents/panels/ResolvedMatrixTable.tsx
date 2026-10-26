"use client";

import { ScopeBadge } from "@/components/admin/documents/panels/ScopeBadge";
import { DataTable, type DataTableColumn } from "@/components/atom/DataTable";
import { usePaginacion } from "@/components/atom/usePaginacion";
import type { ResolvedDocumentMatrixRow } from "@/lib/api/types-documents";

// Tabla de la matriz documental resuelta (HU #10198, AC5 / RF18). Muestra el orden
// final con la columna nivelAplicado (badge DEFAULT/OT/CLIENTE). Las filas llegan ya
// ordenadas por `ordenResuelto` asc desde el API; aquí no se reordena.
// HU #13918 — tabla estándar (`DataTable`) con paginación en cliente y «Filas por página».
export interface ResolvedMatrixTableProps {
  rows: ResolvedDocumentMatrixRow[];
}

const COLUMNS: DataTableColumn<ResolvedDocumentMatrixRow>[] = [
  {
    key: "orden",
    header: "Orden",
    cellClassName: "font-semibold",
    render: (row) => row.ordenResuelto,
  },
  { key: "codigo", header: "Código", cellClassName: "font-mono", render: (row) => row.codigo },
  { key: "nombre", header: "Nombre", cellClassName: "font-semibold", render: (row) => row.nombre },
  { key: "obligatorio", header: "Obligatorio", render: (row) => (row.obligatorio ? "Sí" : "No") },
  {
    key: "nivel",
    header: "Nivel aplicado",
    render: (row) => <ScopeBadge scope={row.nivelAplicado} />,
  },
];

export function ResolvedMatrixTable({ rows }: ResolvedMatrixTableProps) {
  const pg = usePaginacion("documents.ResolvedMatrixTable");
  return (
    <DataTable<ResolvedDocumentMatrixRow>
      ariaLabel="Matriz documental resuelta"
      columns={COLUMNS}
      rows={pg.paginar(rows)}
      getRowKey={(row) => row.documentTypeId}
      minWidth={720}
      pagination={{
        page: pg.page,
        pageSize: pg.pageSize,
        totalCount: rows.length,
        onPageChange: pg.setPage,
        onPageSizeChange: pg.setPageSize,
      }}
    />
  );
}
