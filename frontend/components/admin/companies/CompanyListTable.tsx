"use client";

import { Lock, Pencil, Settings2 } from "lucide-react";
import { isB2BTenantType, tenantTypeLabel } from "@/lib/api/types";
import type { CompanyListItem } from "@/lib/api/types";
import { SwitchToggle } from "@/components/ui/SwitchToggle";
import { StatusBadge } from "@/components/atom/StatusBadge";
import { RowActions } from "@/components/atom/RowActions";
import { TablaFiltrosVacio } from "@/components/atom/TablaFiltrosBar";
import { Pagination } from "@/components/atom/Pagination";
import { EncabezadoFiltrable } from "@/components/atom/FiltroColumna";
import type { TablaFiltrosEstado } from "@/lib/tablas/useTablaFiltros";
import {
  TABLA_HEADER_BG,
  TABLA_HEADER_CELL_CLS,
  TABLA_HEADER_FG,
  TABLA_ROW_HOVER_CLS,
} from "@/components/atom/table-styles";

import { formatFechaHora } from "@/lib/format/date";
// Tabla paginada de compañías (HU #10194, AC1). Columnas: Código (HU #13608, consecutivo COMP-XXXX),
// NIT, Razón Social, Tipo, Estado, Fecha de creación + acciones "Editar", "Activar/Desactivar" y "Configurar".
// Paginación server-side: la tabla solo emite el cambio de página vía onPageChange.
export interface CompanyListTableProps {
  items: CompanyListItem[];
  totalCount: number;
  page: number;
  pageSize: number;
  onPageChange: (page: number) => void;
  /** Filas por página (Bug #13055). */
  onPageSizeChange?: (pageSize: number) => void;
  onConfigure: (tenantId: string) => void;
  /** Solicita editar los datos de la compañía (el contenedor abre el modal de edición). */
  onEdit: (company: CompanyListItem) => void;
  /** Solicita activar/desactivar la compañía (el contenedor muestra la confirmación). */
  onToggleStatus: (company: CompanyListItem) => void;
  /** Filtros por columna (embudo en el encabezado, HU #13999). Sin esto, encabezados planos. */
  filtros?: TablaFiltrosEstado;
  /** Si se pasa y no hay filas, la tabla sigue montada (embudos visibles) con una fila de «sin coincidencias». */
  onLimpiarFiltros?: () => void;
}

export function CompanyListTable({
  items,
  totalCount,
  page,
  pageSize,
  onPageChange,
  onPageSizeChange,
  onConfigure,
  onEdit,
  onToggleStatus,
  filtros,
  onLimpiarFiltros,
}: CompanyListTableProps) {
  const enc = (id: string, titulo: string) =>
    filtros ? (
      <EncabezadoFiltrable filtros={filtros} columnaId={id}>
        {titulo}
      </EncabezadoFiltrable>
    ) : (
      titulo
    );
  return (
    <div className="flex flex-1 flex-col">
      <div className="overflow-x-auto">
      {/* Bug #13055 — tabla homologada con el modelo de trámites */}
      <table
        aria-label="Compañías"
        className="min-w-[760px] text-xs"
        style={{ width: "100%", borderCollapse: "separate", borderSpacing: "0 8px" }}
      >
        <thead>
          <tr>
            <th scope="col" className={`${TABLA_HEADER_CELL_CLS} rounded-l-xl`} style={{ background: TABLA_HEADER_BG, color: TABLA_HEADER_FG }}>
              {enc("codigo", "Código")}
            </th>
            <th scope="col" className={TABLA_HEADER_CELL_CLS} style={{ background: TABLA_HEADER_BG, color: TABLA_HEADER_FG }}>
              {enc("nit", "NIT")}
            </th>
            <th scope="col" className={TABLA_HEADER_CELL_CLS} style={{ background: TABLA_HEADER_BG, color: TABLA_HEADER_FG }}>
              {enc("razonSocial", "Razón Social")}
            </th>
            <th scope="col" className={TABLA_HEADER_CELL_CLS} style={{ background: TABLA_HEADER_BG, color: TABLA_HEADER_FG }}>
              {enc("tipo", "Tipo")}
            </th>
            <th scope="col" className={TABLA_HEADER_CELL_CLS} style={{ background: TABLA_HEADER_BG, color: TABLA_HEADER_FG }}>
              {enc("estado", "Estado")}
            </th>
            <th scope="col" className={TABLA_HEADER_CELL_CLS} style={{ background: TABLA_HEADER_BG, color: TABLA_HEADER_FG }}>
              {enc("fechaCreacion", "Fecha creación")}
            </th>
            <th scope="col" className={`${TABLA_HEADER_CELL_CLS} rounded-r-xl text-right`} style={{ background: TABLA_HEADER_BG, color: TABLA_HEADER_FG }}>
              Acciones
            </th>
          </tr>
        </thead>
        <tbody>
          {items.length === 0 && onLimpiarFiltros ? (
            <tr>
              <td colSpan={7}>
                <TablaFiltrosVacio
                  onLimpiar={onLimpiarFiltros}
                  mensaje="Ninguna compañía coincide con los filtros"
                />
              </td>
            </tr>
          ) : null}
          {items.map((c) => {
            // Solo las compañías del catálogo B2B son editables; los tenants de tipo
            // heredado (sistema / organismos de tránsito) se muestran solo-lectura.
            const editable = isB2BTenantType(c.tenantType) && !c.isTransitOffice;
            return (
            <tr key={c.id} className={`bg-white dark:bg-[#0B0F14] ${TABLA_ROW_HOVER_CLS}`}>
              <td className="whitespace-nowrap border-y border-l px-4 py-3 font-mono font-semibold rounded-l-xl" style={{ borderColor: "#DFE5ED" }}>
                {c.code}
              </td>
              <td className="border-y px-4 py-3 font-mono" style={{ borderColor: "#DFE5ED" }}>
                {c.nit}
              </td>
              <td className="border-y px-4 py-3 font-semibold" style={{ borderColor: "#DFE5ED" }}>
                {c.razonSocial}
              </td>
              <td className="border-y px-4 py-3" style={{ borderColor: "#DFE5ED" }}>
                {c.isTransitOffice ? (
                  <StatusBadge label="OT" tone="info" ariaLabel="Tipo: Organismo de Tránsito" />
                ) : (
                  <StatusBadge
                    label={tenantTypeLabel(c.tenantType)}
                    tone="neutral"
                    ariaLabel={`Tipo: ${tenantTypeLabel(c.tenantType)}`}
                  />
                )}
              </td>
              <td className="border-y px-4 py-3" style={{ borderColor: "#DFE5ED" }}>
                {c.estadoActivo ? (
                  <StatusBadge label="Activa" tone="success" />
                ) : (
                  <StatusBadge label="Inactiva" tone="danger" />
                )}
              </td>
              <td className="border-y px-4 py-3 opacity-70" style={{ borderColor: "#DFE5ED" }}>
                {formatDate(c.fechaCreacion)}
              </td>
              <td className="border-y border-r px-4 py-3 text-right rounded-r-xl" style={{ borderColor: "#DFE5ED" }}>
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

      {items.length === 0 && onLimpiarFiltros ? null : (
      <Pagination
        page={page}
        pageSize={pageSize}
        totalCount={totalCount}
        onPageChange={onPageChange}
        onPageSizeChange={onPageSizeChange}
        className="mt-auto"
      />
      )}
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
