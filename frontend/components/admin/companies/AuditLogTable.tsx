"use client";

import type { AuditLogEntry } from "@/lib/api/types";
import { Pagination } from "@/components/atom/Pagination";
import {
  TABLA_HEADER_BG,
  TABLA_HEADER_CELL_CLS,
  TABLA_HEADER_FG,
  TABLA_ROW_HOVER_CLS,
} from "@/components/atom/table-styles";

import { formatFechaHora } from "@/lib/format/date";
// Tabla del historial de auditoría (HU #10194, AC5). Columnas: Fecha, Campo

// modificado, Valor anterior, Valor nuevo, Operador. El orden DESC por fecha lo
// garantiza el backend; la tabla preserva el orden recibido.
export interface AuditLogTableProps {
  entries: AuditLogEntry[];
  totalCount: number;
  page: number;
  pageSize: number;
  onPageChange: (page: number) => void;
  /** Filas por página (Bug #13055). */
  onPageSizeChange?: (pageSize: number) => void;
}

export function AuditLogTable({
  entries,
  totalCount,
  page,
  pageSize,
  onPageChange,
  onPageSizeChange,
}: AuditLogTableProps) {
  return (
    <div className="flex flex-col">
      <div className="overflow-x-auto">
      {/* Bug #13055 — tabla homologada con el modelo de trámites */}
      <table
        aria-label="Historial de auditoría"
        className="min-w-[640px] text-xs"
        style={{ width: "100%", borderCollapse: "separate", borderSpacing: "0 8px" }}
      >
        <thead>
          <tr>
            <th scope="col" className={`${TABLA_HEADER_CELL_CLS} rounded-l-xl`} style={{ background: TABLA_HEADER_BG, color: TABLA_HEADER_FG }}>
              Fecha
            </th>
            <th scope="col" className={TABLA_HEADER_CELL_CLS} style={{ background: TABLA_HEADER_BG, color: TABLA_HEADER_FG }}>
              Campo modificado
            </th>
            <th scope="col" className={TABLA_HEADER_CELL_CLS} style={{ background: TABLA_HEADER_BG, color: TABLA_HEADER_FG }}>
              Valor anterior
            </th>
            <th scope="col" className={TABLA_HEADER_CELL_CLS} style={{ background: TABLA_HEADER_BG, color: TABLA_HEADER_FG }}>
              Valor nuevo
            </th>
            <th scope="col" className={`${TABLA_HEADER_CELL_CLS} rounded-r-xl text-right`} style={{ background: TABLA_HEADER_BG, color: TABLA_HEADER_FG }}>
              Operador
            </th>
          </tr>
        </thead>
        <tbody>
          {entries.map((entry, i) => (
            <tr key={`${entry.changedAt}-${i}`} className={`bg-white dark:bg-[#0B0F14] ${TABLA_ROW_HOVER_CLS}`}>
              <td className="rounded-l-xl border-y border-l px-4 py-3 opacity-80" style={{ borderColor: "#DFE5ED" }}>
                {formatDateTime(entry.changedAt)}
              </td>
              <td className="border-y px-4 py-3 font-medium" style={{ borderColor: "#DFE5ED" }}>
                {formatField(entry)}
              </td>
              <td className="border-y px-4 py-3 font-mono opacity-70" style={{ borderColor: "#DFE5ED" }}>
                {formatValue(entry.oldValue)}
              </td>
              <td className="border-y px-4 py-3 font-mono opacity-70" style={{ borderColor: "#DFE5ED" }}>
                {formatValue(entry.newValue)}
              </td>
              <td className="rounded-r-xl border-y border-r px-4 py-3 font-mono opacity-70" style={{ borderColor: "#DFE5ED" }}>
                {formatOperator(entry.changedBy)}
              </td>
            </tr>
          ))}
        </tbody>
      </table>
      </div>

      <Pagination
        page={page}
        pageSize={pageSize}
        totalCount={totalCount}
        onPageChange={onPageChange}
        onPageSizeChange={onPageSizeChange}
        noun="registros"
      />
    </div>
  );
}

function formatField(entry: AuditLogEntry): string {
  return entry.entityName ? `${entry.entityName}.${entry.fieldName}` : entry.fieldName;
}

function formatValue(value?: string | null): string {
  return value && value.length > 0 ? value : "—";
}

function formatOperator(changedBy?: string | null): string {
  if (!changedBy) {
    return "—";
  }
  return changedBy.length > 8 ? `${changedBy.slice(0, 8)}…` : changedBy;
}

function formatDateTime(iso: string): string {
  const parsed = new Date(iso);
  if (Number.isNaN(parsed.getTime())) {
    return iso;
  }
  return formatFechaHora(parsed);
}
