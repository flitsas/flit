"use client";

import { Pencil } from "lucide-react";
import type { DocumentType } from "@/lib/api/types-documents";
import { SwitchToggle } from "@/components/ui/SwitchToggle";
import { StatusBadge } from "@/components/atom/StatusBadge";
import { RowActions } from "@/components/atom/RowActions";
import { Pagination } from "@/components/atom/Pagination";

// Tabla paginada del catálogo de tipos de documento (HU #10198, AC1). Columnas:
// Código, Nombre, Origen (cargue/autogenerado), Estado, Fecha de creación + acciones.
// Paginación server-side: la tabla solo emite el cambio de página.
export interface DocumentTypeListTableProps {
  items: DocumentType[];
  totalCount: number;
  page: number;
  pageSize: number;
  onPageChange: (page: number) => void;
  onEdit: (documentType: DocumentType) => void;
  onDeactivate: (documentType: DocumentType) => void;
  onReactivate: (documentType: DocumentType) => void;
}

export function DocumentTypeListTable({
  items,
  totalCount,
  page,
  pageSize,
  onPageChange,
  onEdit,
  onDeactivate,
  onReactivate,
}: DocumentTypeListTableProps) {
  return (
    <div className="flex flex-1 flex-col">
      <div className="overflow-x-auto">
      <table className="w-full min-w-[640px] border-separate border-spacing-y-2 text-xs">
        <thead>
          <tr className="text-left text-[10px] font-semibold uppercase" style={{ color: "#162744" }}>
            <th className="rounded-l-xl px-4 py-2.5" style={{ background: "#DFE5ED" }}>
              Código
            </th>
            <th className="px-4 py-2.5" style={{ background: "#DFE5ED" }}>
              Nombre
            </th>
            <th className="px-4 py-2.5" style={{ background: "#DFE5ED" }}>
              Origen
            </th>
            <th className="px-4 py-2.5" style={{ background: "#DFE5ED" }}>
              Estado
            </th>
            <th className="px-4 py-2.5" style={{ background: "#DFE5ED" }}>
              Fecha creación
            </th>
            <th className="rounded-r-xl px-4 py-2.5 text-right" style={{ background: "#DFE5ED" }}>
              Acciones
            </th>
          </tr>
        </thead>
        <tbody>
          {items.map((d) => {
            const activo = d.estado === "activo";
            return (
              <tr key={d.id} className="bg-white dark:bg-[#0B0F14]">
                <td className="rounded-l-xl border-y border-l px-4 py-3 font-mono">
                  {d.codigo}
                </td>
                <td className="border-y px-4 py-3 font-semibold">
                  {d.nombre}
                  {d.descripcion && <p className="mt-0.5 text-[10px] font-normal opacity-60">{d.descripcion}</p>}
                </td>
                <td className="border-y px-4 py-3">
                  <StatusBadge
                    label={d.esAutogenerado ? "Autogenerado" : "Cargue"}
                    tone={d.esAutogenerado ? "info" : "neutral"}
                  />
                </td>
                <td className="border-y px-4 py-3">
                  <StatusBadge
                    label={activo ? "Activo" : "Inactivo"}
                    tone={activo ? "success" : "danger"}
                  />
                </td>
                <td className="border-y px-4 py-3 opacity-70">
                  {formatDate(d.fechaCreacion)}
                </td>
                <td className="rounded-r-xl border-y border-r px-4 py-3 text-right">
                  <div className="flex items-center justify-end gap-1">
                    <RowActions
                      actions={[
                        {
                          icon: Pencil,
                          label: `Editar ${d.nombre}`,
                          onClick: () => onEdit(d),
                          tone: "primary",
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
  return parsed.toLocaleDateString("es-CO", { year: "numeric", month: "2-digit", day: "2-digit" });
}
