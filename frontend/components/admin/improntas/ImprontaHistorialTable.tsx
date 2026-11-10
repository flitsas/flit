"use client";

import type { ImprontaHistorialItem } from "@/lib/api/types-improntas";
import { formatImprontaHistorialDate } from "./improntas-nav";
import { Pagination } from "@/components/atom/Pagination";
import {
  TABLA_HEADER_BG,
  TABLA_HEADER_CELL_CLS,
  TABLA_HEADER_FG,
  TABLA_ROW_HOVER_CLS,
  TABLA_TARJETA_CLS,
  TABLA_MOVIL_CELL_CLS,
  TABLA_MOVIL_CELL_TITULO_CLS,
  TABLA_MOVIL_ROW_CLS,
  TABLA_MOVIL_TABLE_CLS,
  TABLA_MOVIL_TBODY_CLS,
  TABLA_MOVIL_THEAD_CLS,
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
    <div className={`flex flex-1 flex-col ${TABLA_TARJETA_CLS}`}>
      {/* Bug #13055 — tabla homologada con el modelo de trámites. */}
      <div className="overflow-x-auto">
        <table
          aria-label="Historial de improntas"
          className={`min-w-[640px] text-xs ${TABLA_MOVIL_TABLE_CLS}`}
          style={{ width: "100%", borderCollapse: "separate", borderSpacing: "0 8px" }}
        >
          <thead className={TABLA_MOVIL_THEAD_CLS}>
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
          <tbody className={TABLA_MOVIL_TBODY_CLS}>
            {rows.map((row) => (
              <tr key={row.id} className={`bg-card ${TABLA_ROW_HOVER_CLS} ${TABLA_MOVIL_ROW_CLS}`}>
                <td className={`border-y px-4 py-3 rounded-l-xl border-l font-mono font-semibold ${TABLA_MOVIL_CELL_CLS} ${TABLA_MOVIL_CELL_TITULO_CLS}`} data-label="Radicado" style={{ borderColor: "var(--border)" }}>
                  {row.radicado}
                </td>
                <td className={`border-y px-4 py-3 font-medium uppercase ${TABLA_MOVIL_CELL_CLS}`} data-label="Placa" style={{ borderColor: "var(--border)" }}>
                  {row.placa}
                </td>
                <td className={`border-y px-4 py-3 opacity-80 ${TABLA_MOVIL_CELL_CLS}`} data-label="Fecha de generación" style={{ borderColor: "var(--border)" }}>
                  {formatImprontaHistorialDate(row.fechaImpresa)}
                </td>
                <td className={`border-y px-4 py-3 ${TABLA_MOVIL_CELL_CLS}`} data-label="Operador" style={{ borderColor: "var(--border)" }}>
                  {row.operador}
                </td>
                <td className={`border-y px-4 py-3 rounded-r-xl border-r opacity-80 ${TABLA_MOVIL_CELL_CLS}`} data-label="Usuario FLIT" style={{ borderColor: "var(--border)" }}>
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
