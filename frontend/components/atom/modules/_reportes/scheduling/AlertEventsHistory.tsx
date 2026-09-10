"use client";

import { useCallback, useEffect, useState } from "react";
import { Check, ChevronLeft, ChevronRight } from "lucide-react";
import { UiStateBoundary, type UiStatus } from "@/components/admin/UiStateBoundary";
import {
  acknowledgeAlertEvent,
  fetchAlertEvents,
  type AlertEvent,
  type AlertRule,
} from "@/lib/api/analytics-scheduling";
import { acknowledgeOtAlertEvent, fetchOtAlertEvents } from "@/lib/api/ot-scheduling";
import { formatDateTime } from "./labels";

interface AlertEventsHistoryProps {
  /** Filtro opcional por regla. */
  rules: AlertRule[];
  tenantId?: string;
  /** Alcance Organismo de Tránsito (Reportes 2.0, HU-D, tercera ola). Ver {@link AlertsSection}. */
  otTransitOfficeId?: string;
}

const PAGE_SIZE = 10;

/**
 * Sub-vista "Historial de disparos" (Reportes 2.0, HU-D): alert-events paginados, más
 * recientes primero, filtrables por regla. Muestra valor vs umbral y si se notificó.
 */
export function AlertEventsHistory({ rules, tenantId, otTransitOfficeId }: AlertEventsHistoryProps) {
  const [events, setEvents] = useState<AlertEvent[]>([]);
  const [totalCount, setTotalCount] = useState(0);
  const [page, setPage] = useState(1);
  const [ruleId, setRuleId] = useState<string>("");
  const [status, setStatus] = useState<UiStatus>("loading");
  const [acking, setAcking] = useState<string | null>(null);

  const load = useCallback(async () => {
    setStatus("loading");
    try {
      const data = otTransitOfficeId
        ? await fetchOtAlertEvents({ ruleId: ruleId || undefined, page, pageSize: PAGE_SIZE, transitOfficeId: otTransitOfficeId })
        : await fetchAlertEvents({ ruleId: ruleId || undefined, page, pageSize: PAGE_SIZE, tenantId });
      setEvents(data.items);
      setTotalCount(data.totalCount);
      setStatus(data.items.length === 0 ? "empty" : "ready");
    } catch {
      setStatus("error");
    }
  }, [ruleId, page, tenantId, otTransitOfficeId]);

  useEffect(() => {
    // eslint-disable-next-line react-hooks/set-state-in-effect -- carga async: los setState ocurren tras el await
    void load();
  }, [load]);

  async function handleAck(id: string) {
    setAcking(id);
    try {
      const updated = otTransitOfficeId
        ? await acknowledgeOtAlertEvent(id, otTransitOfficeId)
        : await acknowledgeAlertEvent(id, tenantId);
      setEvents((prev) => prev.map((ev) => (ev.id === id ? updated : ev)));
    } catch {
      // Silencioso: el estado no cambia; el usuario puede reintentar.
    } finally {
      setAcking(null);
    }
  }

  const totalPages = Math.max(1, Math.ceil(totalCount / PAGE_SIZE));

  return (
    <div data-testid="alert-events-history" className="space-y-3">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <h4 className="text-sm font-bold text-[#162744] dark:text-slate-100">Historial de disparos</h4>
        <select
          aria-label="Filtrar por alerta"
          value={ruleId}
          onChange={(e) => {
            setPage(1);
            setRuleId(e.target.value);
          }}
          className="rounded-xl border border-slate-200 dark:border-slate-700 bg-white dark:bg-slate-900 px-3 py-1.5 text-xs text-[#162744] dark:text-slate-100"
        >
          <option value="">Todas las alertas</option>
          {rules.map((r) => (
            <option key={r.id} value={r.id}>{r.name}</option>
          ))}
        </select>
      </div>

      <UiStateBoundary
        status={status}
        emptyMessage="Aún no hay disparos registrados."
        errorMessage="No se pudo cargar el historial de disparos."
        onRetry={() => void load()}
        skeletonRows={3}
      >
        <div className="overflow-x-auto rounded-2xl border border-slate-200 dark:border-slate-700">
          <table className="w-full text-left text-sm">
            <thead>
              <tr className="text-xs uppercase tracking-wide text-slate-500 dark:text-slate-400 border-b border-slate-200 dark:border-slate-700">
                <th className="px-3 py-2">Fecha</th>
                <th className="px-3 py-2">Alerta</th>
                <th className="px-3 py-2 text-right">Valor</th>
                <th className="px-3 py-2 text-right">Umbral</th>
                <th className="px-3 py-2">Notificada</th>
                <th className="px-3 py-2">Detalle</th>
                <th className="px-3 py-2">Acción</th>
              </tr>
            </thead>
            <tbody>
              {events.map((e) => (
                <tr
                  key={e.id}
                  data-testid="alert-event-row"
                  className="border-b border-slate-100 dark:border-slate-800 last:border-0 text-[#162744] dark:text-slate-200"
                >
                  <td className="px-3 py-2 whitespace-nowrap">{formatDateTime(e.triggeredAt)}</td>
                  <td className="px-3 py-2">{e.ruleName}</td>
                  <td className="px-3 py-2 text-right font-semibold" style={{ color: "#557EFF" }}>
                    {e.metricValue}
                  </td>
                  <td className="px-3 py-2 text-right">{e.threshold}</td>
                  <td className="px-3 py-2">{e.notified ? "Sí" : "No"}</td>
                  <td className="px-3 py-2 max-w-[320px] truncate" title={e.message ?? undefined}>
                    {e.message ?? "—"}
                  </td>
                  <td className="px-3 py-2 whitespace-nowrap">
                    {e.acknowledgedAt ? (
                      <span
                        className="inline-flex items-center gap-1 rounded-full px-2 py-0.5 text-[11px] font-semibold"
                        style={{ background: "#557EFF1A", color: "#557EFF" }}
                      >
                        <Check className="h-3 w-3" aria-hidden="true" /> Reconocida
                      </span>
                    ) : (
                      <button
                        type="button"
                        disabled={acking === e.id}
                        onClick={() => void handleAck(e.id)}
                        className="inline-flex items-center gap-1 rounded-lg border border-slate-200 dark:border-slate-700 px-2 py-1 text-xs hover:bg-slate-50 dark:hover:bg-slate-800 disabled:opacity-40"
                      >
                        <Check className="h-3.5 w-3.5" aria-hidden="true" />
                        {acking === e.id ? "Reconociendo…" : "Reconocer"}
                      </button>
                    )}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>

        <div className="flex items-center justify-between text-xs text-slate-500 dark:text-slate-400">
          <span>
            {totalCount} disparo{totalCount === 1 ? "" : "s"} · página {page} de {totalPages}
          </span>
          <div className="flex items-center gap-1">
            <button
              type="button"
              aria-label="Página anterior"
              disabled={page <= 1}
              onClick={() => setPage((p) => Math.max(1, p - 1))}
              className="rounded-lg border border-slate-200 dark:border-slate-700 p-1.5 disabled:opacity-40"
            >
              <ChevronLeft className="h-3.5 w-3.5" aria-hidden="true" />
            </button>
            <button
              type="button"
              aria-label="Página siguiente"
              disabled={page >= totalPages}
              onClick={() => setPage((p) => Math.min(totalPages, p + 1))}
              className="rounded-lg border border-slate-200 dark:border-slate-700 p-1.5 disabled:opacity-40"
            >
              <ChevronRight className="h-3.5 w-3.5" aria-hidden="true" />
            </button>
          </div>
        </div>
      </UiStateBoundary>
    </div>
  );
}
