"use client";

import { Lock, Pencil, Settings2 } from "lucide-react";
import { isB2BTenantType } from "@/lib/api/types";
import type { CompanyListItem } from "@/lib/api/types";
import { SwitchToggle } from "@/components/ui/SwitchToggle";
import { StatusBadge } from "@/components/atom/StatusBadge";
import { RowActions } from "@/components/atom/RowActions";
import { Pagination } from "@/components/atom/Pagination";

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
  return (
    <div className="flex flex-1 flex-col">
      <div className="overflow-x-auto">
      <table className="w-full min-w-[640px] border-separate border-spacing-y-2 text-xs">
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
                  <StatusBadge label="Activa" tone="success" />
                ) : (
                  <StatusBadge label="Inactiva" tone="danger" />
                )}
              </td>
              <td className="border-y px-4 py-3 opacity-70">
                {formatDate(c.fechaCreacion)}
              </td>
              <td className="border-y border-r px-4 py-3 text-right rounded-r-xl">
                <div className="flex items-center justify-end gap-1">
                  <RowActions
                    actions={[
                      {
                        icon: editable ? Pencil : Lock,
                        label: `Editar ${c.razonSocial}`,
                        onClick: () => editable && onEdit(c),
                        tone: "primary",
                        disabled: !editable,
                        disabledTitle:
                          "Compañía de tipo de sistema: no editable desde esta consola",
                      },
                    ]}
                  />
                  <SwitchToggle
                    checked={c.estadoActivo}
                    onChange={() => onToggleStatus(c)}
                    label={`${c.estadoActivo ? "Desactivar" : "Activar"} ${c.razonSocial}`}
                  />
                  <RowActions
                    actions={[
                      {
                        icon: Settings2,
                        label: `Configurar ${c.razonSocial}`,
                        onClick: () => onConfigure(c.id),
                        tone: "primary",
                      },
                    ]}
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
