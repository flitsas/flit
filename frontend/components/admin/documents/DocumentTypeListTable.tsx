"use client";

import { ChevronLeft, ChevronRight, Pencil } from "lucide-react";
import type { DocumentType } from "@/lib/api/types-documents";
import { SwitchToggle } from "@/components/ui/SwitchToggle";

// Tabla paginada del catálogo de tipos de documento (HU #10198, AC1). Columnas:
// Código, Nombre, Estado, Fecha de creación + acciones Editar/Desactivar.
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
  const totalPages = Math.max(1, Math.ceil(totalCount / pageSize));
  const from = totalCount === 0 ? 0 : (page - 1) * pageSize + 1;
  const to = Math.min(page * pageSize, totalCount);

  return (
    <div className="flex flex-1 flex-col">
      <table className="w-full border-separate border-spacing-y-2 text-xs">
        <thead>
          <tr className="text-left text-[10px] font-semibold uppercase" style={{ color: "#162744" }}>
            <th className="rounded-l-xl px-4 py-2.5" style={{ background: "#DFE5ED" }}>
              Código
            </th>
            <th className="px-4 py-2.5" style={{ background: "#DFE5ED" }}>
              Nombre
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
                <td className="rounded-l-xl border-y border-l px-4 py-3 font-mono" style={{ borderColor: "#DFE5ED" }}>
                  {d.codigo}
                </td>
                <td className="border-y px-4 py-3 font-semibold" style={{ borderColor: "#DFE5ED" }}>
                  {d.nombre}
                  {d.descripcion && <p className="mt-0.5 text-[10px] font-normal opacity-60">{d.descripcion}</p>}
                </td>
                <td className="border-y px-4 py-3" style={{ borderColor: "#DFE5ED" }}>
                  <span
                    className="rounded-full px-2 py-0.5 text-[10px] font-semibold text-white"
                    style={{ background: activo ? "#00DBD5" : "#FF4E00" }}
                  >
                    {activo ? "Activo" : "Inactivo"}
                  </span>
                </td>
                <td className="border-y px-4 py-3 opacity-70" style={{ borderColor: "#DFE5ED" }}>
                  {formatDate(d.fechaCreacion)}
                </td>
                <td className="rounded-r-xl border-y border-r px-4 py-3 text-right" style={{ borderColor: "#DFE5ED" }}>
                  <div className="flex items-center justify-end gap-2">
                    <button
                      type="button"
                      onClick={() => onEdit(d)}
                      aria-label={`Editar ${d.nombre}`}
                      className="inline-flex items-center gap-1.5 rounded-lg px-2.5 py-1 text-[10px] font-semibold text-white"
                      style={{ background: "#557EFF" }}
                    >
                      <Pencil className="h-3 w-3" /> Editar
                    </button>
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

      <div className="mt-auto flex items-center justify-between pt-3 text-[11px]">
        <p className="opacity-60">
          Mostrando {from}–{to} de {totalCount}
        </p>
        <div className="flex items-center gap-2">
          <button
            type="button"
            aria-label="Página anterior"
            disabled={page <= 1}
            onClick={() => onPageChange(page - 1)}
            className="flex items-center gap-1 rounded-lg border px-2.5 py-1.5 font-medium disabled:opacity-40"
            style={{ borderColor: "#DFE5ED" }}
          >
            <ChevronLeft className="h-3.5 w-3.5" /> Anterior
          </button>
          <span className="font-semibold" style={{ color: "#557EFF" }}>
            {page} / {totalPages}
          </span>
          <button
            type="button"
            aria-label="Página siguiente"
            disabled={page >= totalPages}
            onClick={() => onPageChange(page + 1)}
            className="flex items-center gap-1 rounded-lg border px-2.5 py-1.5 font-medium disabled:opacity-40"
            style={{ borderColor: "#DFE5ED" }}
          >
            Siguiente <ChevronRight className="h-3.5 w-3.5" />
          </button>
        </div>
      </div>
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
