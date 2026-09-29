"use client";

import type { ImprontaHistorialItem } from "@/lib/api/types-improntas";
import { formatImprontaHistorialDate } from "./improntas-nav";
import { Pagination } from "@/components/atom/Pagination";
import {
  TABLA_HEADER_BG,
  TABLA_HEADER_CELL_CLS,
  TABLA_HEADER_FG,
  TABLA_ROW_HOVER_CLS,
} from "@/components/atom/table-styles";

export interface ImprontaHistorialTableProps {
  rows: ImprontaHistorialItem[];
  totalCount: number;
  page: number;
  pageSize: number;
  onPageChange: (page: number) => void;
  /** Bug #13055: «Filas por página»; sin este manejador el selector no se muestra. */
  onPageSizeChange?: (pageSize: number) => void;
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
  onPageSizeChange,
}: ImprontaHistorialTableProps) {
  return (
    <div className="flex flex-1 flex-col">
      {/* Bug #13055 — tabla homologada con el modelo de trámites. */}
      <div className="overflow-x-auto">
        <table
          aria-label="Historial de improntas"
          className="min-w-[640px] text-xs"
          style={{ width: "100%", borderCollapse: "separate", borderSpacing: "0 8px" }}
        >
          <thead>
            <tr>
              <th
                scope="col"
                className={`${TABLA_HEADER_CELL_CLS} rounded-l-xl`}
                style={{ background: TABLA_HEADER_BG, color: TABLA_HEADER_FG }}
              >
                Radicado
              </th>
              <th
                scope="col"
                className={`${TABLA_HEADER_CELL_CLS}`}
                style={{ background: TABLA_HEADER_BG, color: TABLA_HEADER_FG }}
              >
                Placa
              </th>
              <th
                scope="col"
                className={`${TABLA_HEADER_CELL_CLS}`}
                style={{ background: TABLA_HEADER_BG, color: TABLA_HEADER_FG }}
              >
                Fecha de generación
              </th>
              <th
                scope="col"
                className={`${TABLA_HEADER_CELL_CLS}`}
                style={{ background: TABLA_HEADER_BG, color: TABLA_HEADER_FG }}
              >
                Operador
              </th>
              <th
                scope="col"
                className={`${TABLA_HEADER_CELL_CLS} rounded-r-xl`}
                style={{ background: TABLA_HEADER_BG, color: TABLA_HEADER_FG }}
              >
                Usuario FLIT
              </th>
            </tr>
          </thead>
          <tbody>
            {rows.map((row) => (
              <tr key={row.id} className={`bg-white dark:bg-[#0B0F14] ${TABLA_ROW_HOVER_CLS}`}>
                <td className="border-y px-4 py-3 rounded-l-xl border-l font-mono font-semibold" style={{ borderColor: "#DFE5ED" }}>
                  {row.radicado}
                </td>
                <td className="border-y px-4 py-3 font-medium uppercase" style={{ borderColor: "#DFE5ED" }}>
                  {row.placa}
                </td>
                <td className="border-y px-4 py-3 opacity-80" style={{ borderColor: "#DFE5ED" }}>
                  {formatImprontaHistorialDate(row.fechaImpresa)}
                </td>
                <td className="border-y px-4 py-3" style={{ borderColor: "#DFE5ED" }}>
                  {row.operador}
                </td>
                <td className="border-y px-4 py-3 rounded-r-xl border-r opacity-80" style={{ borderColor: "#DFE5ED" }}>
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
        onPageSizeChange={onPageSizeChange}
        className="mt-auto"
      />
    </div>
  );
}
