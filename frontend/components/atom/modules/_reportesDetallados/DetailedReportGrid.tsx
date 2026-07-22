"use client";

import { UiStateBoundary, type UiStatus } from "@/components/admin/UiStateBoundary";
import type { DetailedReportPage } from "@/lib/api/detailed-report";
import { statusLabel } from "../_reportes/categories";

interface DetailedReportGridProps {
  data: DetailedReportPage | null;
  uiStatus: UiStatus;
  errorMessage?: string;
  emptyMessage?: string;
  onRetry?: () => void;
  page: number;
  onPageChange: (page: number) => void;
}

export function DetailedReportGrid({
  data,
  uiStatus,
  errorMessage,
  emptyMessage = "No hay trámites que coincidan con los filtros seleccionados.",
  onRetry,
  page,
  onPageChange,
}: DetailedReportGridProps) {
  const totalPages = data ? Math.max(1, Math.ceil(data.totalCount / data.pageSize)) : 1;

  return (
    <UiStateBoundary
      status={uiStatus}
      emptyMessage={emptyMessage}
      errorMessage={errorMessage}
      onRetry={onRetry}
      skeletonRows={6}
    >
      <div className="overflow-x-auto rounded-2xl border">
        <table className="min-w-full text-xs">
          <thead className="bg-[#F4F7FC] dark:bg-white/5">
            <tr>
              {["Referencia", "Tipo", "Estado", "Persona", "Transformación", "Leasing", "Pago", "Traspaso", "Radicador"].map((h) => (
                <th key={h} className="px-3 py-2 text-left font-semibold">{h}</th>
              ))}
            </tr>
          </thead>
          <tbody>
            {data?.items.map((row) => (
              <tr key={row.id} className="border-t">
                <td className="px-3 py-2">{row.referenceNumber}</td>
                <td className="px-3 py-2">{row.procedureTypeName}</td>
                <td className="px-3 py-2">{statusLabel(row.status) ?? row.status}</td>
                <td className="px-3 py-2">{row.personFullName || row.personDocument || "—"}</td>
                <td className="px-3 py-2">{row.hasTransformation ? row.transformationDetail ?? "Sí" : "No"}</td>
                <td className="px-3 py-2">{row.isLeasing ? "Sí" : "No"}</td>
                <td className="px-3 py-2">{row.paymentType || "—"}</td>
                <td className="px-3 py-2">{row.transferType}</td>
                <td className="px-3 py-2">{row.createdByDisplayName}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
      {data && data.totalCount > data.pageSize && (
        <div className="flex items-center justify-between pt-3 text-xs">
          <span>Página {page} de {totalPages} · {data.totalCount} registros</span>
          <div className="flex gap-2">
            <button type="button" disabled={page <= 1} className="rounded-lg border px-3 py-1 disabled:opacity-40" onClick={() => onPageChange(page - 1)}>Anterior</button>
            <button type="button" disabled={page >= totalPages} className="rounded-lg border px-3 py-1 disabled:opacity-40" onClick={() => onPageChange(page + 1)}>Siguiente</button>
          </div>
        </div>
      )}
    </UiStateBoundary>
  );
}
