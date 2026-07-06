"use client";

import { useState } from "react";
import { FileSpreadsheet, FileText, Loader2 } from "lucide-react";
import { exportAnalyticsExcel, exportExecutivePdf } from "@/lib/api/analytics";
import { ApiError } from "@/lib/api/types";
import type { AnalyticsCategory } from "@/lib/api/types";
import type { DateRange } from "./range";

interface ExportButtonsProps {
  range: DateRange;
  tenantId?: string;
  /** Filtro de categoría activo (segmento seleccionado), si lo hay (AC1). */
  category?: AnalyticsCategory;
  status?: string;
  disabled?: boolean;
}

type Busy = "excel" | "pdf" | null;

function describeError(error: unknown): string {
  if (error instanceof ApiError) {
    if (error.status === 400) return "El rango de fechas no es válido para exportar.";
    if (error.status === 403) return "No tienes acceso a la exportación de esa compañía.";
    if (error.status === 401) return "Tu sesión expiró. Vuelve a iniciar sesión.";
  }
  return "No se pudo generar la exportación. Inténtalo de nuevo.";
}

/**
 * Botones de exportación del dashboard (HU #10249). Excel con filtros activos + indicador
 * de progreso (AC1) y PDF ejecutivo del periodo (AC2). Los fallos 4xx/5xx se muestran en
 * una región accesible sin bloquear el dashboard (AC3).
 */
export function ExportButtons({ range, tenantId, category, status, disabled }: ExportButtonsProps) {
  const [busy, setBusy] = useState<Busy>(null);
  const [error, setError] = useState<string | null>(null);

  async function run(kind: Exclude<Busy, null>, action: () => Promise<void>) {
    setBusy(kind);
    setError(null);
    try {
      await action();
    } catch (err) {
      setError(describeError(err));
    } finally {
      setBusy(null);
    }
  }

  const handleExcel = () =>
    run("excel", () => exportAnalyticsExcel({ from: range.from, to: range.to, category, status, tenantId }));
  const handlePdf = () =>
    run("pdf", () => exportExecutivePdf({ from: range.from, to: range.to, tenantId }));

  return (
    <div className="flex flex-col gap-1">
      <div className="flex items-center gap-2">
        <button
          type="button"
          onClick={handleExcel}
          disabled={disabled || busy !== null}
          aria-busy={busy === "excel"}
          className="flex items-center gap-1.5 text-xs font-semibold px-3 py-2 rounded-lg border disabled:opacity-50"
          style={{ color: "#162744" }}
        >
          {busy === "excel" ? (
            <Loader2 className="h-3.5 w-3.5 animate-spin" aria-hidden="true" />
          ) : (
            <FileSpreadsheet className="h-3.5 w-3.5" aria-hidden="true" />
          )}
          {busy === "excel" ? "Exportando…" : "Exportar Excel"}
        </button>

        <button
          type="button"
          onClick={handlePdf}
          disabled={disabled || busy !== null}
          aria-busy={busy === "pdf"}
          className="flex items-center gap-1.5 text-xs font-semibold px-3 py-2 rounded-lg text-white disabled:opacity-50"
          style={{ background: "#557EFF" }}
        >
          {busy === "pdf" ? (
            <Loader2 className="h-3.5 w-3.5 animate-spin" aria-hidden="true" />
          ) : (
            <FileText className="h-3.5 w-3.5" aria-hidden="true" />
          )}
          {busy === "pdf" ? "Generando…" : "Resumen Ejecutivo PDF"}
        </button>
      </div>

      {error && (
        <p role="alert" aria-live="assertive" className="text-[11px]" style={{ color: "#FF4E00" }}>
          {error}
        </p>
      )}
    </div>
  );
}
