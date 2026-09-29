"use client";

import { Download } from "lucide-react";
import { Pagination } from "@/components/atom/Pagination";
import { RowActions } from "@/components/atom/RowActions";
import {
  TABLA_HEADER_BG,
  TABLA_HEADER_CELL_CLS,
  TABLA_HEADER_FG,
  TABLA_ROW_HOVER_CLS,
} from "@/components/atom/table-styles";
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
  /** Bug #13055: «Filas por página»; sin este manejador el selector no se muestra. */
  onPageSizeChange?: (pageSize: number) => void;
  /** Redescarga del PDF ya generado (CF-19). Sin handler, la acción no se ofrece. */
  onDownload?: (row: StandaloneDocumentListItem) => void;
  /** Id de la fila cuya presigned URL se está pidiendo, para bloquear el doble clic. */
  downloadingId?: string | null;
}

/**
 * Tabla del historial de documentos generados (HU-01 shell; CF-17).
 *
 * Presentacional pura: tipo, escenario, empresa, usuario, fecha y resultado. Nunca
 * renderiza `document_snapshot` (PII alta) — el contrato del listado ni siquiera lo trae.
 * El estado se pinta con `StatusBadge` a partir de `status-labels.ts`: texto visible
 * siempre, el color solo acompaña (CF-22).
 *
 * La acción de descarga solo se ofrece en las filas `generated`: pedir la presigned URL de
 * un documento en error devolvería 409 y sería una acción que promete lo que no puede dar.
 */
export function HistorialTable({
  rows,
  totalCount,
  page,
  pageSize,
  onPageChange,
  onPageSizeChange,
  onDownload,
  downloadingId = null,
}: HistorialTableProps) {
  return (
    <div className="flex flex-1 flex-col">
      {/* Bug #13055 — tabla homologada con el modelo de trámites. */}
      <div className="overflow-x-auto">
        <table
          className="min-w-[720px] text-xs"
          style={{ width: "100%", borderCollapse: "separate", borderSpacing: "0 8px" }}
        >
          <caption className="sr-only">Documentos generados por la compañía</caption>
          <thead>
            <tr>
              <th
                scope="col"
                className={`${TABLA_HEADER_CELL_CLS} rounded-l-xl`}
                style={{ background: TABLA_HEADER_BG, color: TABLA_HEADER_FG }}
              >
                Tipo
              </th>
              <th
                scope="col"
                className={`${TABLA_HEADER_CELL_CLS}`}
                style={{ background: TABLA_HEADER_BG, color: TABLA_HEADER_FG }}
              >
                Escenario
              </th>
              <th
                scope="col"
                className={`${TABLA_HEADER_CELL_CLS}`}
                style={{ background: TABLA_HEADER_BG, color: TABLA_HEADER_FG }}
              >
                Empresa
              </th>
              <th
                scope="col"
                className={`${TABLA_HEADER_CELL_CLS}`}
                style={{ background: TABLA_HEADER_BG, color: TABLA_HEADER_FG }}
              >
                Usuario
              </th>
              <th
                scope="col"
                className={`${TABLA_HEADER_CELL_CLS}`}
                style={{ background: TABLA_HEADER_BG, color: TABLA_HEADER_FG }}
              >
                Fecha
              </th>
              <th
                scope="col"
                className={`${TABLA_HEADER_CELL_CLS}`}
                style={{ background: TABLA_HEADER_BG, color: TABLA_HEADER_FG }}
              >
                Resultado
              </th>
              <th
                scope="col"
                className={`${TABLA_HEADER_CELL_CLS} rounded-r-xl`}
                style={{ background: TABLA_HEADER_BG, color: TABLA_HEADER_FG }}
              >
                Acciones
              </th>
            </tr>
          </thead>
          <tbody>
            {rows.map((row) => {
              const statusView = standaloneDocumentStatusView(row.status);
              return (
                <tr key={row.id} className={`bg-white dark:bg-[#0B0F14] ${TABLA_ROW_HOVER_CLS}`}>
                <td className="border-y px-4 py-3 rounded-l-xl border-l font-medium" style={{ borderColor: "#DFE5ED" }}>
                  {generacionDocumentalTypeLabel(row.documentType)}
                </td>
                <td className="border-y px-4 py-3 opacity-80" style={{ borderColor: "#DFE5ED" }}>
                  {row.scenario ?? "—"}
                </td>
                <td className="border-y px-4 py-3 opacity-80" style={{ borderColor: "#DFE5ED" }}>
                  {row.companyName ?? "—"}
                </td>
                <td className="border-y px-4 py-3 opacity-80" style={{ borderColor: "#DFE5ED" }}>
                  {row.createdByUserName ?? "—"}
                </td>
                <td className="border-y px-4 py-3 opacity-80" style={{ borderColor: "#DFE5ED" }}>
                  {formatGeneracionDocumentalDate(row.createdAt)}
                </td>
                <td className="border-y px-4 py-3" style={{ borderColor: "#DFE5ED" }}>
                  <StatusBadge label={statusView.label} tone={statusView.tone} />
                </td>
                <td className="border-y px-4 py-3 rounded-r-xl border-r" style={{ borderColor: "#DFE5ED" }}>
                  {onDownload && row.status === "generated" ? (
                    <RowActions
                      actions={[
                        {
                          icon: Download,
                          label: `Descargar ${generacionDocumentalTypeLabel(row.documentType)} del ${formatGeneracionDocumentalDate(row.createdAt)}`,
                          tone: "primary",
                          onClick: () => onDownload(row),
                          disabled: downloadingId === row.id,
                          disabledTitle: "Preparando la descarga…",
                        },
                      ]}
                    />
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

      <Pagination
        page={page}
        pageSize={pageSize}
        totalCount={totalCount}
        onPageChange={onPageChange}
        onPageSizeChange={onPageSizeChange}
      />
    </div>
  );
}
