"use client";

import type { ImprontaHistorialItem } from "@/lib/api/types-improntas";
import { formatImprontaHistorialDate } from "./improntas-nav";
import { Pagination } from "@/components/atom/Pagination";

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
  return (
    <div className="flex flex-1 flex-col">
      <div className="overflow-x-auto">
      <table className="w-full min-w-[640px] border-separate border-spacing-y-2 text-xs">
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
