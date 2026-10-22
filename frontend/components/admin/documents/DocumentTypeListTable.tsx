"use client";

import { Pencil, Trash2 } from "lucide-react";
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
    <div className="flex flex-1 flex-col">
      <div className="overflow-x-auto">
      {/* Bug #13055 — tabla homologada con el modelo de trámites */}
      <table
        aria-label="Catálogo de tipos de documento"
        className="min-w-[640px] text-xs"
        style={{ width: "100%", borderCollapse: "separate", borderSpacing: "0 8px" }}
      >
        <thead>
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
        <tbody>
          {items.map((d) => {
            const activo = d.estado === "activo";
            return (
              <tr key={d.id} className={`bg-white dark:bg-[#0B0F14] ${TABLA_ROW_HOVER_CLS}`}>
                <td className="rounded-l-xl border-y border-l px-4 py-3 font-mono" style={{ borderColor: "#DFE5ED" }}>
                  {d.codigo}
                </td>
                <td className="border-y px-4 py-3 font-semibold" style={{ borderColor: "#DFE5ED" }}>
                  {d.nombre}
                  {d.descripcion && <p className="mt-0.5 text-xs font-normal opacity-60">{d.descripcion}</p>}
                </td>
                <td className="border-y px-4 py-3" style={{ borderColor: "#DFE5ED" }}>
                  <StatusBadge
                    label={d.esAutogenerado ? "Autogenerado" : "Cargue"}
                    tone={d.esAutogenerado ? "info" : "neutral"}
                  />
                </td>
                <td className="border-y px-4 py-3" style={{ borderColor: "#DFE5ED" }}>
                  <StatusBadge
                    label={activo ? "Activo" : "Inactivo"}
                    tone={activo ? "success" : "danger"}
                  />
                </td>
                <td className="border-y px-4 py-3 opacity-70" style={{ borderColor: "#DFE5ED" }}>
                  {formatDate(d.fechaCreacion)}
                </td>
                <td className="rounded-r-xl border-y border-r px-4 py-3 text-right" style={{ borderColor: "#DFE5ED" }}>
                  <div className="flex items-center justify-end gap-1">
                    <RowActions
                      actions={[
                        {
                          icon: Pencil,
                          label: `Editar ${d.nombre}`,
                          onClick: () => onEdit(d),
                          tone: "primary",
                        },
                        {
                          icon: Trash2,
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
