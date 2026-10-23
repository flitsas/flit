"use client";

import { ScopeBadge } from "@/components/admin/documents/panels/ScopeBadge";
import type { ResolvedDocumentMatrixRow } from "@/lib/api/types-documents";
import {
  TABLA_HEADER_BG,
  TABLA_HEADER_CELL_CLS,
  TABLA_HEADER_FG,
  TABLA_ROW_HOVER_CLS,
} from "@/components/atom/table-styles";

// Tabla de la matriz documental resuelta (HU #10198, AC5 / RF18). Muestra el orden
// final con la columna nivelAplicado (badge DEFAULT/OT/CLIENTE). Las filas llegan ya
// ordenadas por `ordenResuelto` asc desde el API; aquí no se reordena.
export interface ResolvedMatrixTableProps {
  rows: ResolvedDocumentMatrixRow[];
}

export function ResolvedMatrixTable({ rows }: ResolvedMatrixTableProps) {
  return (
    <div className="overflow-x-auto">
    {/* Bug #13055 — tabla homologada con el modelo de trámites */}
<table
 aria-label="Matriz documental resuelta"
 className="min-w-[720px] text-xs"
 style={{ width: "100%", borderCollapse: "separate", borderSpacing: "0 8px" }}
>
      <thead>
        <tr>
          <th scope="col" className={`${TABLA_HEADER_CELL_CLS} rounded-l-xl`} style={{ background: TABLA_HEADER_BG, color: TABLA_HEADER_FG }}>
            Orden
          </th>
          <th scope="col" className={`${TABLA_HEADER_CELL_CLS}`} style={{ background: TABLA_HEADER_BG, color: TABLA_HEADER_FG }}>
            Código
          </th>
          <th scope="col" className={`${TABLA_HEADER_CELL_CLS}`} style={{ background: TABLA_HEADER_BG, color: TABLA_HEADER_FG }}>
            Nombre
          </th>
          <th scope="col" className={`${TABLA_HEADER_CELL_CLS}`} style={{ background: TABLA_HEADER_BG, color: TABLA_HEADER_FG }}>
            Obligatorio
          </th>
          <th scope="col" className={`${TABLA_HEADER_CELL_CLS} rounded-r-xl`} style={{ background: TABLA_HEADER_BG, color: TABLA_HEADER_FG }}>
            Nivel aplicado
          </th>
        </tr>
      </thead>
      <tbody>
        {rows.map((row) => (
          <tr key={row.documentTypeId} className={`bg-white dark:bg-[#0B0F14] ${TABLA_ROW_HOVER_CLS}`}>
            <td className="rounded-l-xl border-y border-l px-4 py-3 font-semibold" style={{ borderColor: "#DFE5ED" }}>
              {row.ordenResuelto}
            </td>
            <td className="border-y px-4 py-3 font-mono" style={{ borderColor: "#DFE5ED" }}>
              {row.codigo}
            </td>
            <td className="border-y px-4 py-3 font-semibold" style={{ borderColor: "#DFE5ED" }}>
              {row.nombre}
            </td>
            <td className="border-y px-4 py-3" style={{ borderColor: "#DFE5ED" }}>
              {row.obligatorio ? "Sí" : "No"}
            </td>
            <td className="rounded-r-xl border-y border-r px-4 py-3" style={{ borderColor: "#DFE5ED" }}>
              <ScopeBadge scope={row.nivelAplicado} />
            </td>
          </tr>
        ))}
      </tbody>
    </table>
    </div>
  );
}
