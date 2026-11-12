"use client";

import { iconoComo } from "@flit/ui/Icono";
import type { DocumentType } from "@/lib/api/types-documents";
import { SwitchToggle } from "@/components/ui/SwitchToggle";
import { StatusBadge } from "@/components/atom/StatusBadge";
import { RowActions } from "@/components/atom/RowActions";
import { Pagination } from "@/components/atom/Pagination";
import {
  TABLA_HEADER_BG,
  TABLA_HEADER_CELL_CLS,
  TABLA_HEADER_FG,
  TABLA_ROW_HOVER_CLS,
  TABLA_TARJETA_CLS,
  TABLA_MOVIL_CELL_CLS,
  TABLA_MOVIL_CELL_TITULO_CLS,
  TABLA_MOVIL_ROW_CLS,
  TABLA_MOVIL_TABLE_CLS,
  TABLA_MOVIL_TBODY_CLS,
  TABLA_MOVIL_THEAD_CLS,
} from "@/components/atom/table-styles";

import { formatFechaHora } from "@/lib/format/date";
// Tabla paginada del catálogo de tipos de documento (HU #10198, AC1). Columnas:

// Código, Nombre, Origen (cargue/autogenerado), Estado, Fecha de creación + acciones.
// Paginación server-side: la tabla solo emite el cambio de página.
export interface DocumentTypeListTableProps {
  items: DocumentType[];
  totalCount: number;
  page: number;
  pageSize: number;
  onPageChange: (page: number) => void;
  /** Filas por página (Bug #13055). */
  onPageSizeChange?: (pageSize: number) => void;
  onEdit: (documentType: DocumentType) => void;
  onDeactivate: (documentType: DocumentType) => void;
  onReactivate: (documentType: DocumentType) => void;
  onDelete: (documentType: DocumentType) => void;
}

export function DocumentTypeListTable({
  items,
  totalCount,
  page,
  pageSize,
  onPageChange,
  onPageSizeChange,
  onEdit,
  onDeactivate,
  onReactivate,
  onDelete,
}: DocumentTypeListTableProps) {
  return (
    <div className={`flex flex-1 flex-col ${TABLA_TARJETA_CLS}`}>
      <div className="overflow-x-auto">
      {/* Bug #13055 — tabla homologada con el modelo de trámites */}
      <table
        aria-label="Catálogo de tipos de documento"
        className={`min-w-[640px] text-xs ${TABLA_MOVIL_TABLE_CLS}`}
        style={{ width: "100%", borderCollapse: "separate", borderSpacing: "0 8px" }}
      >
        <thead className={TABLA_MOVIL_THEAD_CLS}>
          <tr>
            <th scope="col" className={`${TABLA_HEADER_CELL_CLS} rounded-l-xl`} style={{ background: TABLA_HEADER_BG, color: TABLA_HEADER_FG }}>
              Código
            </th>
            <th scope="col" className={TABLA_HEADER_CELL_CLS} style={{ background: TABLA_HEADER_BG, color: TABLA_HEADER_FG }}>
              Nombre
            </th>
            <th scope="col" className={TABLA_HEADER_CELL_CLS} style={{ background: TABLA_HEADER_BG, color: TABLA_HEADER_FG }}>
              Origen
            </th>
            <th scope="col" className={TABLA_HEADER_CELL_CLS} style={{ background: TABLA_HEADER_BG, color: TABLA_HEADER_FG }}>
              Estado
            </th>
            <th scope="col" className={TABLA_HEADER_CELL_CLS} style={{ background: TABLA_HEADER_BG, color: TABLA_HEADER_FG }}>
              Fecha creación
            </th>
            <th scope="col" className={`${TABLA_HEADER_CELL_CLS} rounded-r-xl text-right`} style={{ background: TABLA_HEADER_BG, color: TABLA_HEADER_FG }}>
              Acciones
            </th>
          </tr>
        </thead>
        <tbody className={TABLA_MOVIL_TBODY_CLS}>
          {items.map((d) => {
            const activo = d.estado === "activo";
            return (
              <tr key={d.id} className={`bg-card ${TABLA_ROW_HOVER_CLS} ${TABLA_MOVIL_ROW_CLS}`}>
                <td className={`rounded-l-xl border-y border-l px-4 py-3 font-mono ${TABLA_MOVIL_CELL_CLS} ${TABLA_MOVIL_CELL_TITULO_CLS}`} data-label="Código" style={{ borderColor: "var(--border)" }}>
                  {d.codigo}
                </td>
                <td className={`border-y px-4 py-3 font-semibold ${TABLA_MOVIL_CELL_CLS}`} data-label="Nombre" style={{ borderColor: "var(--border)" }}>
                  {d.nombre}
                  {d.descripcion && <p className="mt-0.5 text-xs font-normal opacity-60">{d.descripcion}</p>}
                </td>
                <td className={`border-y px-4 py-3 ${TABLA_MOVIL_CELL_CLS}`} data-label="Origen" style={{ borderColor: "var(--border)" }}>
                  <StatusBadge
                    label={d.esAutogenerado ? "Autogenerado" : "Cargue"}
                    tone={d.esAutogenerado ? "info" : "neutral"}
                  />
                </td>
                <td className={`border-y px-4 py-3 ${TABLA_MOVIL_CELL_CLS}`} data-label="Estado" style={{ borderColor: "var(--border)" }}>
                  <StatusBadge
                    label={activo ? "Activo" : "Inactivo"}
                    tone={activo ? "success" : "danger"}
                  />
                </td>
                <td className={`border-y px-4 py-3 opacity-70 ${TABLA_MOVIL_CELL_CLS}`} data-label="Fecha creación" style={{ borderColor: "var(--border)" }}>
                  {formatDate(d.fechaCreacion)}
                </td>
                <td className={`rounded-r-xl border-y border-r px-4 py-3 text-right ${TABLA_MOVIL_CELL_CLS}`} data-label="Acciones" style={{ borderColor: "var(--border)" }}>
                  <div className="flex items-center justify-end gap-1">
                    <RowActions
                      actions={[
                        {
                          icon: iconoComo("lapiz"),
                          label: `Editar ${d.nombre}`,
                          onClick: () => onEdit(d),
                          tone: "primary",
                        },
                        {
                          icon: iconoComo("papelera"),
                          label: `Eliminar ${d.nombre}`,
                          onClick: () => onDelete(d),
                          tone: "danger",
                        },
                      ]}
                    />
                    <SwitchToggle
                      checked={activo}
                      onChange={() => (activo ? onDeactivate(d) : onReactivate(d))}
                      label={`${activo ? "Desactivar" : "Activar"} ${d.nombre}`}
                    />
                  </div>
                </td>
              </tr>
            );
          })}
        </tbody>
      </table>
      </div>

      <Pagination
        page={page}
        pageSize={pageSize}
        totalCount={totalCount}
        onPageChange={onPageChange}
        onPageSizeChange={onPageSizeChange}
        className="mt-auto"
      />
    </div>
  );
}

function formatDate(iso: string): string {
  const parsed = new Date(iso);
  if (Number.isNaN(parsed.getTime())) {
    return iso;
  }
  return formatFechaHora(parsed);
}
