"use client";

import { ScopeBadge } from "@/components/admin/documents/panels/ScopeBadge";
import type { ResolvedDocumentMatrixRow } from "@/lib/api/types-documents";

// Tabla de la matriz documental resuelta (HU #10198, AC5 / RF18). Muestra el orden
// final con la columna nivelAplicado (badge DEFAULT/OT/CLIENTE). Las filas llegan ya
// ordenadas por `ordenResuelto` asc desde el API; aquí no se reordena.
export interface ResolvedMatrixTableProps {
  rows: ResolvedDocumentMatrixRow[];
}

export function ResolvedMatrixTable({ rows }: ResolvedMatrixTableProps) {
  return (
    <table className="w-full border-separate border-spacing-y-2 text-xs">
      <thead>
        <tr className="text-left text-[10px] font-semibold uppercase" style={{ color: "#162744" }}>
          <th className="rounded-l-xl px-4 py-2.5" style={{ background: "#DFE5ED" }}>
            Orden
          </th>
          <th className="px-4 py-2.5" style={{ background: "#DFE5ED" }}>
            Código
          </th>
          <th className="px-4 py-2.5" style={{ background: "#DFE5ED" }}>
            Nombre
          </th>
          <th className="px-4 py-2.5" style={{ background: "#DFE5ED" }}>
            Obligatorio
          </th>
          <th className="rounded-r-xl px-4 py-2.5" style={{ background: "#DFE5ED" }}>
            Nivel aplicado
          </th>
        </tr>
      </thead>
      <tbody>
        {rows.map((row) => (
          <tr key={row.documentTypeId} className="bg-white dark:bg-[#0B0F14]">
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
  );
}
