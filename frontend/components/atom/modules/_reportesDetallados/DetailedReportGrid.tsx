"use client";

import { Pagination } from "@/components/atom/Pagination";
import {
  CARDLIST_CELL,
  CARDLIST_HEAD_ROW,
  CARDLIST_ROW,
  CARDLIST_SCROLL,
  CARDLIST_TABLE,
  CARDLIST_TH,
} from "@/components/atom/table-cardlist";
import { UiStateBoundary, type UiStatus } from "@/components/admin/UiStateBoundary";
import type { DetailedReportPage, NetworkDetailedReportPage } from "@/lib/api/detailed-report";
import { statusLabel } from "../_reportes/categories";

const COLUMNAS = ["Referencia", "Tipo", "Estado", "Persona", "Transformación", "Leasing", "Pago", "Traspaso", "Radicador"];

interface DetailedReportGridProps {
  data: DetailedReportPage | NetworkDetailedReportPage | null;
  /** HU #12364 — filas de la red: se añade la columna «Cliente» (dueño del trámite, AC2). */
  networkScope?: boolean;
  uiStatus: UiStatus;
  errorMessage?: string;
  emptyMessage?: string;
  onRetry?: () => void;
  page: number;
  /** Tamaño de página elegido; si no llega, se usa el que informa la API. */
  pageSize?: number;
  onPageChange: (page: number) => void;
  /** Bug #13055 — «Filas por página»; sin este manejador el selector no se muestra. */
  onPageSizeChange?: (pageSize: number) => void;
}

export function DetailedReportGrid({
  data,
  uiStatus,
  errorMessage,
  emptyMessage = "No hay trámites que coincidan con los filtros seleccionados.",
  onRetry,
  page,
  pageSize,
  onPageChange,
  onPageSizeChange,
  networkScope = false,
}: DetailedReportGridProps) {
  const columnas = networkScope ? ["Cliente", ...COLUMNAS] : COLUMNAS;

  return (
    <UiStateBoundary
      status={uiStatus}
      emptyMessage={emptyMessage}
      errorMessage={errorMessage}
      onRetry={onRetry}
      skeletonRows={6}
    >
      {/* Bug #13055 — tabla homologada con el modelo de trámites (sin tarjeta envolvente). */}
      <div className={CARDLIST_SCROLL}>
        <table className={`min-w-full ${CARDLIST_TABLE}`} aria-label="Reporte detallado de trámites">
          <thead>
            <tr className={CARDLIST_HEAD_ROW}>
              {columnas.map((h) => (
                <th key={h} scope="col" className={CARDLIST_TH}>{h}</th>
              ))}
            </tr>
          </thead>
          <tbody>
            {data?.items.map((row) => (
              <tr key={row.id} className={CARDLIST_ROW}>
                {networkScope && (
                  <td className={`${CARDLIST_CELL} font-medium`} data-testid="detallado-fila-cliente">
                    {"tenantName" in row && row.tenantName ? row.tenantName : "—"}
                  </td>
                )}
                <td className={CARDLIST_CELL}>{row.referenceNumber}</td>
                <td className={CARDLIST_CELL}>{row.procedureTypeName}</td>
                <td className={CARDLIST_CELL}>{statusLabel(row.status) ?? row.status}</td>
                <td className={CARDLIST_CELL}>{row.personFullName || row.personDocument || "—"}</td>
                <td className={CARDLIST_CELL}>{row.hasTransformation ? row.transformationDetail ?? "Sí" : "No"}</td>
                <td className={CARDLIST_CELL}>{row.isLeasing ? "Sí" : "No"}</td>
                <td className={CARDLIST_CELL}>{row.paymentType || "—"}</td>
                <td className={CARDLIST_CELL}>{row.transferType}</td>
                <td className={CARDLIST_CELL}>{row.createdByDisplayName}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
      {data && (
        <Pagination
          page={page}
          pageSize={pageSize ?? data.pageSize}
          totalCount={data.totalCount}
          onPageChange={onPageChange}
          onPageSizeChange={onPageSizeChange}
          ariaLabel="Paginación del reporte detallado"
          noun="trámites"
        />
      )}
    </UiStateBoundary>
  );
}
