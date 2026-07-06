"use client";

import { ChevronLeft, ChevronRight } from "lucide-react";
import type { ImprontaHistorialItem } from "@/lib/api/types-improntas";
import { formatImprontaHistorialDate } from "./improntas-nav";

export interface ImprontaHistorialTableProps {
  rows: ImprontaHistorialItem[];
  totalCount: number;
  page: number;
  pageSize: number;
  onPageChange: (page: number) => void;
}

/**
 * Tabla paginada del historial de improntas (HU #10470 AC1): radicado, placa, fecha de
 * generación, operador y usuario FLIT que la generó. Presentacional pura — la carga,
 * filtros y estados de UI viven en `ImprontaHistorialSection`. Paginación embebida
 * (mismo patrón que `OtTablePagination`/`CompanyListTable`: cada feature admin mantiene
 * su propia copia local en vez de compartir un componente entre módulos).
 */
export function ImprontaHistorialTable({
  rows,
  totalCount,
  page,
  pageSize,
  onPageChange,
}: ImprontaHistorialTableProps) {
  const totalPages = Math.max(1, Math.ceil(totalCount / pageSize));
  const from = totalCount === 0 ? 0 : (page - 1) * pageSize + 1;
  const to = Math.min(page * pageSize, totalCount);

  return (
    <div className="flex flex-1 flex-col">
      <table className="w-full border-separate border-spacing-y-2 text-xs">
        <thead>
          <tr className="text-left text-[10px] font-semibold uppercase" style={{ color: "#162744" }}>
            <th className="rounded-l-xl px-4 py-2.5" style={{ background: "#DFE5ED" }} scope="col">
              Radicado
            </th>
            <th className="px-4 py-2.5" style={{ background: "#DFE5ED" }} scope="col">
              Placa
            </th>
            <th className="px-4 py-2.5" style={{ background: "#DFE5ED" }} scope="col">
              Fecha de generación
            </th>
            <th className="px-4 py-2.5" style={{ background: "#DFE5ED" }} scope="col">
              Operador
            </th>
            <th className="rounded-r-xl px-4 py-2.5" style={{ background: "#DFE5ED" }} scope="col">
              Usuario FLIT
            </th>
          </tr>
        </thead>
        <tbody>
          {rows.map((row) => (
            <tr key={row.id} className="bg-white dark:bg-[#0B0F14]">
              <td
                className="rounded-l-xl border-y border-l px-4 py-3 font-mono font-semibold"
              >
                {row.radicado}
              </td>
              <td className="border-y px-4 py-3 font-medium uppercase">
                {row.placa}
              </td>
              <td className="border-y px-4 py-3 opacity-80">
                {formatImprontaHistorialDate(row.fechaImpresa)}
              </td>
              <td className="border-y px-4 py-3">
                {row.operador}
              </td>
              <td
                className="rounded-r-xl border-y border-r px-4 py-3 opacity-80"
              >
                {row.flitUserName}
              </td>
            </tr>
          ))}
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
