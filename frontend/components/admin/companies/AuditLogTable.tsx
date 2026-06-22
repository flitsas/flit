"use client";

import { ChevronLeft, ChevronRight } from "lucide-react";
import type { AuditLogEntry } from "@/lib/api/types";

// Tabla del historial de auditoría (HU #10194, AC5). Columnas: Fecha, Campo
// modificado, Valor anterior, Valor nuevo, Operador. El orden DESC por fecha lo
// garantiza el backend; la tabla preserva el orden recibido.
export interface AuditLogTableProps {
  entries: AuditLogEntry[];
  totalCount: number;
  page: number;
  pageSize: number;
  onPageChange: (page: number) => void;
}

export function AuditLogTable({ entries, totalCount, page, pageSize, onPageChange }: AuditLogTableProps) {
  const totalPages = Math.max(1, Math.ceil(totalCount / pageSize));

  return (
    <div className="flex flex-col">
      <table className="w-full border-separate border-spacing-y-2 text-xs">
        <thead>
          <tr className="text-left text-[10px] font-semibold uppercase" style={{ color: "#162744" }}>
            <th className="rounded-l-xl px-4 py-2.5" style={{ background: "#DFE5ED" }}>
              Fecha
            </th>
            <th className="px-4 py-2.5" style={{ background: "#DFE5ED" }}>
              Campo modificado
            </th>
            <th className="px-4 py-2.5" style={{ background: "#DFE5ED" }}>
              Valor anterior
            </th>
            <th className="px-4 py-2.5" style={{ background: "#DFE5ED" }}>
              Valor nuevo
            </th>
            <th className="rounded-r-xl px-4 py-2.5" style={{ background: "#DFE5ED" }}>
              Operador
            </th>
          </tr>
        </thead>
        <tbody>
          {entries.map((entry, i) => (
            <tr key={`${entry.changedAt}-${i}`} className="bg-white dark:bg-[#0B0F14]">
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

      <div className="mt-2 flex items-center justify-between pt-1 text-[11px]">
        <p className="opacity-60">{totalCount} registros</p>
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
  return parsed.toLocaleString("es-CO", {
    year: "numeric",
    month: "2-digit",
    day: "2-digit",
    hour: "2-digit",
    minute: "2-digit",
  });
}
