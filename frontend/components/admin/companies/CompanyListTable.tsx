"use client";

import { ChevronLeft, ChevronRight, Lock, Pencil, Settings2 } from "lucide-react";
import { isB2BTenantType } from "@/lib/api/types";
import type { CompanyListItem } from "@/lib/api/types";
import { SwitchToggle } from "@/components/ui/SwitchToggle";
import { StatusBadge } from "@/components/atom/StatusBadge";

// Tabla paginada de compañías (HU #10194, AC1). Columnas: NIT, Razón Social,
// Estado, Fecha de creación + acciones "Editar", "Activar/Desactivar" y "Configurar".
// Paginación server-side: la tabla solo emite el cambio de página vía onPageChange.
export interface CompanyListTableProps {
  items: CompanyListItem[];
  totalCount: number;
  page: number;
  pageSize: number;
  onPageChange: (page: number) => void;
  onConfigure: (tenantId: string) => void;
  /** Solicita editar los datos de la compañía (el contenedor abre el modal de edición). */
  onEdit: (company: CompanyListItem) => void;
  /** Solicita activar/desactivar la compañía (el contenedor muestra la confirmación). */
  onToggleStatus: (company: CompanyListItem) => void;
}

export function CompanyListTable({
  items,
  totalCount,
  page,
  pageSize,
  onPageChange,
  onConfigure,
  onEdit,
  onToggleStatus,
}: CompanyListTableProps) {
  const totalPages = Math.max(1, Math.ceil(totalCount / pageSize));
  const from = totalCount === 0 ? 0 : (page - 1) * pageSize + 1;
  const to = Math.min(page * pageSize, totalCount);

  return (
    <div className="flex flex-1 flex-col">
      <table className="w-full border-separate border-spacing-y-2 text-xs">
        <thead>
          <tr className="text-left text-[10px] font-semibold uppercase" style={{ color: "#162744" }}>
            <th className="rounded-l-xl px-4 py-2.5" style={{ background: "#DFE5ED" }}>
              NIT
            </th>
            <th className="px-4 py-2.5" style={{ background: "#DFE5ED" }}>
              Razón Social
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
          {items.map((c) => {
            // Solo las compañías del catálogo B2B son editables; los tenants de tipo
            // heredado (sistema / organismos de tránsito) se muestran solo-lectura.
            const editable = isB2BTenantType(c.tenantType);
            return (
            <tr key={c.id} className="bg-white dark:bg-[#0B0F14]">
              <td className="border-y border-l px-4 py-3 font-mono rounded-l-xl">
                {c.nit}
              </td>
              <td className="border-y px-4 py-3 font-semibold">
                {c.razonSocial}
              </td>
              <td className="border-y px-4 py-3">
                {c.estadoActivo ? (
                  <StatusBadge label="Activa" bg="rgba(0,219,213,0.15)" color="#0f766e" border="rgba(0,219,213,0.35)" />
                ) : (
                  <StatusBadge label="Inactiva" bg="rgba(255,78,0,0.10)" color="#c2410c" border="rgba(255,78,0,0.3)" />
                )}
              </td>
              <td className="border-y px-4 py-3 opacity-70">
                {formatDate(c.fechaCreacion)}
              </td>
              <td className="border-y border-r px-4 py-3 text-right rounded-r-xl">
                <div className="flex items-center justify-end gap-2">
                  <button
                    type="button"
                    onClick={() => editable && onEdit(c)}
                    disabled={!editable}
                    aria-disabled={!editable}
                    aria-label={`Editar ${c.razonSocial}`}
                    title={
                      editable
                        ? undefined
                        : "Compañía de tipo de sistema: no editable desde esta consola"
                    }
                    className="inline-flex items-center gap-1.5 rounded-lg border px-2.5 py-1 text-[10px] font-semibold disabled:cursor-not-allowed disabled:opacity-40"
                    style={{ borderColor: "#557EFF", color: "#557EFF" }}
                  >
                    {editable ? <Pencil className="h-3 w-3" /> : <Lock className="h-3 w-3" />} Editar
                  </button>
                  <SwitchToggle
                    checked={c.estadoActivo}
                    onChange={() => onToggleStatus(c)}
                    label={`${c.estadoActivo ? "Desactivar" : "Activar"} ${c.razonSocial}`}
                  />
                  <button
                    type="button"
                    onClick={() => onConfigure(c.id)}
                    className="inline-flex items-center gap-1.5 rounded-lg px-2.5 py-1 text-[10px] font-semibold text-white"
                    style={{ background: "#557EFF" }}
                  >
                    <Settings2 className="h-3 w-3" /> Configurar
                  </button>
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
