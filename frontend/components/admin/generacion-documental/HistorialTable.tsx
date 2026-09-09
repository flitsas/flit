"use client";

import { Download } from "lucide-react";
import { Pagination } from "@/components/atom/Pagination";
import { StatusBadge } from "@/components/atom/StatusBadge";
import type { StandaloneDocumentListItem } from "@/lib/api/types-generacion-documental";
import { formatGeneracionDocumentalDate, generacionDocumentalTypeLabel } from "./generacion-documental-nav";
import { standaloneDocumentStatusView } from "./status-labels";

export interface HistorialTableProps {
  rows: StandaloneDocumentListItem[];
  totalCount: number;
  page: number;
  pageSize: number;
  onPageChange: (page: number) => void;
  /** Redescarga del PDF ya generado (HU-03). Sin handler, la acción no se ofrece. */
  onDownload?: (row: StandaloneDocumentListItem) => void;
}

/**
 * Tabla del historial de documentos generados (HU-01 shell; CF-17).
 *
 * Presentacional pura: tipo, escenario, empresa, usuario, fecha y resultado. Nunca
 * renderiza `document_snapshot` (PII alta) — el contrato del listado ni siquiera lo trae.
 * El estado se pinta con `StatusBadge` a partir de `status-labels.ts`: texto visible
 * siempre, el color solo acompaña (CF-22). Los filtros y la descarga real son HU-03.
 */
export function HistorialTable({
  rows,
  totalCount,
  page,
  pageSize,
  onPageChange,
  onDownload,
}: HistorialTableProps) {
  return (
    <div className="flex flex-1 flex-col">
      <div className="overflow-x-auto">
        <table className="w-full min-w-[720px] border-separate border-spacing-y-2 text-xs">
          <caption className="sr-only">Documentos generados por la compañía</caption>
          <thead>
            <tr className="text-left text-[10px] font-semibold uppercase" style={{ color: "#162744" }}>
              <th className="rounded-l-xl px-4 py-2.5" style={{ background: "#DFE5ED" }} scope="col">
                Tipo
              </th>
              <th className="px-4 py-2.5" style={{ background: "#DFE5ED" }} scope="col">
                Escenario
              </th>
              <th className="px-4 py-2.5" style={{ background: "#DFE5ED" }} scope="col">
                Empresa
              </th>
              <th className="px-4 py-2.5" style={{ background: "#DFE5ED" }} scope="col">
                Usuario
              </th>
              <th className="px-4 py-2.5" style={{ background: "#DFE5ED" }} scope="col">
                Fecha
              </th>
              <th className="px-4 py-2.5" style={{ background: "#DFE5ED" }} scope="col">
                Resultado
              </th>
              <th className="rounded-r-xl px-4 py-2.5" style={{ background: "#DFE5ED" }} scope="col">
                Acciones
              </th>
            </tr>
          </thead>
          <tbody>
            {rows.map((row) => {
              const statusView = standaloneDocumentStatusView(row.status);
              return (
                <tr key={row.id} className="bg-white dark:bg-[#0B0F14]">
                  <td className="rounded-l-xl border-y border-l px-4 py-3 font-medium">
                    {generacionDocumentalTypeLabel(row.documentType)}
                  </td>
                  <td className="border-y px-4 py-3 opacity-80">{row.scenario ?? "—"}</td>
                  <td className="border-y px-4 py-3 opacity-80">{row.companyName ?? "—"}</td>
                  <td className="border-y px-4 py-3 opacity-80">{row.createdByUserName ?? "—"}</td>
                  <td className="border-y px-4 py-3 opacity-80">
                    {formatGeneracionDocumentalDate(row.createdAt)}
                  </td>
                  <td className="border-y px-4 py-3">
                    <StatusBadge label={statusView.label} tone={statusView.tone} />
                  </td>
                  <td className="rounded-r-xl border-y border-r px-4 py-3">
                    {onDownload && row.status === "generated" ? (
                      <button
                        type="button"
                        onClick={() => onDownload(row)}
                        aria-label={`Descargar ${generacionDocumentalTypeLabel(row.documentType)} del ${formatGeneracionDocumentalDate(row.createdAt)}`}
                        className="inline-flex items-center gap-1.5 rounded-xl border px-3 py-1.5 text-[11px] font-semibold focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[#557EFF]"
                        style={{ borderColor: "#DFE5ED", color: "#557EFF" }}
                      >
                        <Download className="h-3.5 w-3.5" aria-hidden="true" />
                        Descargar
                      </button>
                    ) : (
                      <span className="opacity-60">—</span>
                    )}
                  </td>
                </tr>
              );
            })}
          </tbody>
        </table>
      </div>

      <Pagination page={page} pageSize={pageSize} totalCount={totalCount} onPageChange={onPageChange} />
    </div>
  );
}
