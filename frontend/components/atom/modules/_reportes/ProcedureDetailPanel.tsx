"use client";

import { useCallback, useEffect, useState } from "react";
import { X } from "lucide-react";
import { Pagination } from "@/components/atom/Pagination";
import {
  CARDLIST_CELL,
  CARDLIST_HEAD_ROW,
  CARDLIST_ROW,
  CARDLIST_SCROLL,
  CARDLIST_TABLE,
  CARDLIST_TH,
} from "@/components/atom/table-cardlist";
import { usePaginacion } from "@/components/atom/usePaginacion";
import { UiStateBoundary, type UiStatus } from "@/components/admin/UiStateBoundary";
import { fetchProcedureDetails } from "@/lib/api/analytics";
import { ApiError } from "@/lib/api/types";
import type { AnalyticsCategory, ProcedureDetailsPage } from "@/lib/api/types";
import { CATEGORY_META, statusLabel } from "./categories";
import type { DateRange } from "./range";

interface ProcedureDetailPanelProps {
  /** Categoría a filtrar; indefinida = todas (drill-down solo por estado, Reportes 2.0). */
  category?: AnalyticsCategory;
  /** Estado a filtrar; indefinido = toda la categoría. */
  status?: string;
  range: DateRange;
  tenantId?: string;
  onClose: () => void;
}

function describeError(error: unknown): string {
  if (error instanceof ApiError) {
    if (error.status === 400) return "El rango de fechas no es válido o falta la compañía.";
    if (error.status === 403) return "No tienes acceso al detalle de esa compañía.";
  }
  return "No se pudo cargar el detalle de trámites.";
}

function formatDate(value?: string | null): string {
  if (!value) return "—";
  return value.slice(0, 10);
}

/**
 * Panel lateral con la tabla paginada del detalle de trámites (HU #10248, AC1). Se abre
 * al seleccionar un segmento del gráfico y consume GET /api/v1/analytics/procedures con
 * los filtros de categoría/estado. Implementa los 4 estados de UI vía UiStateBoundary.
 */
export function ProcedureDetailPanel({ category, status, range, tenantId, onClose }: ProcedureDetailPanelProps) {
  const meta = category ? CATEGORY_META[category] : undefined;
  // Bug #13055 — paginación estándar con «Filas por página» (10/25/50/100), de servidor.
  const { page, pageSize, setPage, setPageSize } = usePaginacion();
  const [data, setData] = useState<ProcedureDetailsPage | null>(null);
  const [uiStatus, setUiStatus] = useState<UiStatus>("loading");
  const [errorMessage, setErrorMessage] = useState<string>();
  const [reloadKey, setReloadKey] = useState(0);

  // Cierra con Escape (accesibilidad de diálogo).
  useEffect(() => {
    function onKey(e: KeyboardEvent) {
      if (e.key === "Escape") onClose();
    }
    window.addEventListener("keydown", onKey);
    return () => window.removeEventListener("keydown", onKey);
  }, [onClose]);

  useEffect(() => {
    const controller = new AbortController();
    async function load() {
      setUiStatus("loading");
      try {
        const res = await fetchProcedureDetails(
          { from: range.from, to: range.to, category, status, page, pageSize, tenantId },
          controller.signal,
        );
        if (controller.signal.aborted) return;
        setData(res);
        setUiStatus(res.items.length === 0 ? "empty" : "ready");
      } catch (error) {
        if (controller.signal.aborted || (error as Error).name === "AbortError") return;
        setErrorMessage(describeError(error));
        setUiStatus("error");
      }
    }
    void load();
    return () => controller.abort();
  }, [category, status, range.from, range.to, tenantId, page, pageSize, reloadKey]);

  const retry = useCallback(() => setReloadKey((k) => k + 1), []);

  const categoryLabel = meta?.label ?? "Todos los trámites";
  const title = status ? `${categoryLabel} · ${statusLabel(status)}` : categoryLabel;

  return (
    <div className="fixed inset-0 z-40 flex justify-end" role="dialog" aria-modal="true" aria-labelledby="detalle-panel-title">
      {/* Backdrop */}
      <button
        type="button"
        className="absolute inset-0 bg-black/30"
        aria-label="Cerrar el panel"
        onClick={onClose}
      />

      <aside className="relative z-10 h-full w-full max-w-xl bg-white dark:bg-[#0B0F14] shadow-2xl flex flex-col">
        <header className="flex items-center justify-between px-5 py-4 border-b shrink-0">
          <div>
            <h2 id="detalle-panel-title" className="text-sm font-bold">
              Detalle de trámites
            </h2>
            <p className="text-xs opacity-70 mt-0.5">
              {title}
              {data ? ` · ${data.totalCount} trámites` : ""}
            </p>
          </div>
          <button
            type="button"
            onClick={onClose}
            aria-label="Cerrar detalle"
            className="h-8 w-8 grid place-items-center rounded-lg border hover:bg-[#557EFF1A]"
          >
            <X className="h-4 w-4" />
          </button>
        </header>

        <div className="flex-1 min-h-0 overflow-y-auto p-5">
          <UiStateBoundary
            status={uiStatus}
            errorMessage={errorMessage}
            onRetry={retry}
            emptyMessage="Sin trámites en el periodo seleccionado."
            skeletonRows={6}
          >
            {/* Bug #13055 — tabla homologada con el modelo de trámites. */}
            <div className={CARDLIST_SCROLL}>
              <table className={`min-w-[520px] ${CARDLIST_TABLE}`} aria-label="Detalle de trámites">
                <thead>
                  <tr className={CARDLIST_HEAD_ROW}>
                    <th scope="col" className={CARDLIST_TH}>Referencia</th>
                    <th scope="col" className={CARDLIST_TH}>Tipo</th>
                    <th scope="col" className={CARDLIST_TH}>Estado</th>
                    <th scope="col" className={CARDLIST_TH}>Radicador</th>
                    <th scope="col" className={CARDLIST_TH}>Enviado</th>
                  </tr>
                </thead>
                <tbody>
                  {data?.items.map((row) => (
                    <tr key={row.id} className={CARDLIST_ROW}>
                      <td className={`${CARDLIST_CELL} font-medium`}>{row.referenceNumber}</td>
                      <td className={`${CARDLIST_CELL} opacity-80`}>{row.procedureTypeName}</td>
                      <td className={CARDLIST_CELL}>{statusLabel(row.status)}</td>
                      <td className={`${CARDLIST_CELL} opacity-80`}>{row.createdByDisplayName}</td>
                      <td className={`${CARDLIST_CELL} opacity-80`}>{formatDate(row.submittedAt)}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          </UiStateBoundary>
        </div>

        {uiStatus === "ready" && data && (
          <footer className="px-5 py-3 border-t shrink-0">
            <Pagination
              page={page}
              pageSize={pageSize}
              totalCount={data.totalCount}
              onPageChange={setPage}
              onPageSizeChange={setPageSize}
              ariaLabel="Paginación del detalle de trámites"
              noun="trámites"
            />
          </footer>
        )}
      </aside>
    </div>
  );
}
