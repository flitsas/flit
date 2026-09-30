"use client";

import { useCallback, useEffect, useState } from "react";
import { Check } from "lucide-react";
import { Pagination } from "@/components/atom/Pagination";
import { RowActions } from "@/components/atom/RowActions";
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

/**
 * Sub-vista "Historial de disparos" (Reportes 2.0, HU-D): alert-events paginados, más
 * recientes primero, filtrables por regla. Muestra valor vs umbral y si se notificó.
 */
export function AlertEventsHistory({ rules, tenantId, otTransitOfficeId }: AlertEventsHistoryProps) {
  const [events, setEvents] = useState<AlertEvent[]>([]);
  const [totalCount, setTotalCount] = useState(0);
  // Bug #13055 — paginación estándar con «Filas por página» (10/25/50/100), de servidor.
  const { page, pageSize, setPage, setPageSize } = usePaginacion();
  const [ruleId, setRuleId] = useState<string>("");
  const [status, setStatus] = useState<UiStatus>("loading");
  const [acking, setAcking] = useState<string | null>(null);

  const load = useCallback(async () => {
    setStatus("loading");
    try {
      const data = otTransitOfficeId
        ? await fetchOtAlertEvents({ ruleId: ruleId || undefined, page, pageSize, transitOfficeId: otTransitOfficeId })
        : await fetchAlertEvents({ ruleId: ruleId || undefined, page, pageSize, tenantId });
      setEvents(data.items);
      setTotalCount(data.totalCount);
      setStatus(data.items.length === 0 ? "empty" : "ready");
    } catch {
      setStatus("error");
    }
  }, [ruleId, page, pageSize, tenantId, otTransitOfficeId]);

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
        {/* Bug #13055 — tabla homologada con el modelo de trámites (sin tarjeta envolvente). */}
        <div className={CARDLIST_SCROLL}>
          <table className={CARDLIST_TABLE} aria-label="Historial de disparos de alertas">
            <thead>
              <tr className={CARDLIST_HEAD_ROW}>
                <th scope="col" className={CARDLIST_TH}>Fecha</th>
                <th scope="col" className={CARDLIST_TH}>Alerta</th>
                <th scope="col" className={`${CARDLIST_TH} text-right`}>Valor</th>
                <th scope="col" className={`${CARDLIST_TH} text-right`}>Umbral</th>
                <th scope="col" className={CARDLIST_TH}>Notificada</th>
                <th scope="col" className={CARDLIST_TH}>Detalle</th>
                <th scope="col" className={CARDLIST_TH}>Acción</th>
              </tr>
            </thead>
            <tbody>
              {events.map((e) => (
                <tr key={e.id} data-testid="alert-event-row" className={CARDLIST_ROW}>
                  <td className={`${CARDLIST_CELL} whitespace-nowrap`}>{formatDateTime(e.triggeredAt)}</td>
                  <td className={CARDLIST_CELL}>{e.ruleName}</td>
                  <td className={`${CARDLIST_CELL} text-right font-semibold`} style={{ color: "#557EFF" }}>
                    {e.metricValue}
                  </td>
                  <td className={`${CARDLIST_CELL} text-right`}>{e.threshold}</td>
                  <td className={CARDLIST_CELL}>{e.notified ? "Sí" : "No"}</td>
                  <td className={`${CARDLIST_CELL} max-w-[320px] truncate`} title={e.message ?? undefined}>
                    {e.message ?? "—"}
                  </td>
                  <td className={`${CARDLIST_CELL} whitespace-nowrap`}>
                    {e.acknowledgedAt ? (
                      <span
                        className="inline-flex items-center gap-1 rounded-full px-2 py-0.5 text-[11px] font-semibold"
                        style={{ background: "#557EFF1A", color: "#557EFF" }}
                      >
                        <Check className="h-3 w-3" aria-hidden="true" /> Reconocida
                      </span>
                    ) : (
                      <RowActions
                        className="justify-start"
                        actions={[
                          {
                            icon: Check,
                            label: `Reconocer disparo de ${e.ruleName}`,
                            tone: "primary",
                            onClick: () => void handleAck(e.id),
                            disabled: acking === e.id,
                            disabledTitle: "Reconociendo…",
                          },
                        ]}
                      />
                    )}
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
          onPageChange={setPage}
          onPageSizeChange={setPageSize}
          ariaLabel="Paginación del historial de disparos"
          noun="disparos"
        />
      </UiStateBoundary>
    </div>
  );
}
