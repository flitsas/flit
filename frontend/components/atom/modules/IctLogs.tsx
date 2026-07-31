"use client";

// Submódulo de observabilidad ICT (Integración con Terceros) — HU10893.
// Dos pestañas: Logs (redactados/enmascarados por el backend) y Alertas ICT (métricas + eventos).
import { Fragment, useCallback, useEffect, useMemo, useState } from "react";
import { Check } from "lucide-react";
import { UiStateBoundary, type UiStatus } from "@/components/admin/UiStateBoundary";
import {
  fetchIctAlerts,
  fetchIctLogs,
  type IctAlertMetrics,
  type IctLogEntry,
  type IctLogFilters,
  type IctLogType,
} from "@/lib/api/ict-client";
import {
  acknowledgeAlertEvent,
  fetchAlertEvents,
  fetchAlertRules,
  type AlertEvent,
} from "@/lib/api/analytics-scheduling";
import { decodeJwtPayload, isSuperAdmin, TOKEN_STORAGE_KEY } from "@/lib/auth/jwt";

type Tab = "logs" | "alertas";

const LOG_TYPES: IctLogType[] = ["auth", "transaction", "webhook", "external"];
const PAGE_SIZE = 25;

export function IctLogs() {
  const [tab, setTab] = useState<Tab>("logs");

  return (
    <div className="flex flex-col gap-4 p-4">
      <header className="flex items-center justify-between">
        <h1 className="text-xl font-semibold text-[#162744] dark:text-white">Integración con Terceros — Observabilidad</h1>
        <nav className="flex gap-2" role="tablist">
          <TabButton active={tab === "logs"} onClick={() => setTab("logs")}>Logs</TabButton>
          <TabButton active={tab === "alertas"} onClick={() => setTab("alertas")}>Alertas ICT</TabButton>
        </nav>
      </header>
      {tab === "logs" ? <LogsTab /> : <AlertsTab />}
    </div>
  );
}

function TabButton({ active, onClick, children }: { active: boolean; onClick: () => void; children: React.ReactNode }) {
  return (
    <button
      type="button"
      role="tab"
      aria-selected={active}
      onClick={onClick}
      className={`rounded-md px-3 py-1.5 text-sm font-medium transition-colors ${
        active ? "bg-[#557EFF] text-white" : "bg-transparent text-[#557EFF] hover:bg-[#557EFF]/10"
      }`}
    >
      {children}
    </button>
  );
}

function LogsTab() {
  const [applied, setApplied] = useState<IctLogFilters>({});
  const [logTypeInput, setLogTypeInput] = useState<IctLogType | "">("");
  const [searchInput, setSearchInput] = useState("");
  const [fromInput, setFromInput] = useState("");
  const [toInput, setToInput] = useState("");
  const [page, setPage] = useState(1);
  const [expandedId, setExpandedId] = useState<string | null>(null);
  const [data, setData] = useState<IctLogEntry[]>([]);
  const [total, setTotal] = useState(0);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    const controller = new AbortController();
    // Todas las actualizaciones de estado ocurren en callbacks asíncronos (no síncronas en el efecto).
    fetchIctLogs({ ...applied, page, pageSize: PAGE_SIZE }, controller.signal)
      .then((res) => {
        setData(res.items);
        setTotal(res.total);
        setError(null);
      })
      .catch((e: unknown) => {
        // Mensaje genérico: nunca exponer el código HTTP ni la ruta técnica al usuario.
        if (e instanceof DOMException && e.name === "AbortError") return;
        if (!controller.signal.aborted) {
          setError("No se pudieron cargar los logs. Revisa los filtros e intenta de nuevo.");
        }
      })
      .finally(() => {
        if (!controller.signal.aborted) setLoading(false);
      });
    return () => controller.abort();
  }, [applied, page]);

  const totalPages = Math.max(1, Math.ceil(total / PAGE_SIZE));

  function applyFilters(e: React.FormEvent) {
    e.preventDefault();
    setPage(1);
    setExpandedId(null);
    setApplied({
      logType: logTypeInput || undefined,
      search: searchInput.trim() || undefined,
      from: fromInput ? new Date(fromInput).toISOString() : undefined,
      to: toInput ? new Date(toInput).toISOString() : undefined,
    });
  }

  function clearFilters() {
    setLogTypeInput("");
    setSearchInput("");
    setFromInput("");
    setToInput("");
    setPage(1);
    setExpandedId(null);
    setApplied({});
  }

  const inputClass =
    "rounded border border-slate-300 bg-white px-2 py-1 text-sm dark:bg-slate-800 dark:text-white";

  return (
    <section className="flex flex-col gap-3">
      <form onSubmit={applyFilters} className="flex flex-wrap items-end gap-3">
        <label className="flex flex-col gap-1 text-xs text-slate-500">
          Tipo
          <select
            value={logTypeInput}
            onChange={(e) => setLogTypeInput(e.target.value as IctLogType | "")}
            className={inputClass}
          >
            <option value="">Todos</option>
            {LOG_TYPES.map((t) => (
              <option key={t} value={t}>{t}</option>
            ))}
          </select>
        </label>
        <label className="flex flex-col gap-1 text-xs text-slate-500">
          Buscar
          <input
            type="text"
            value={searchInput}
            onChange={(e) => setSearchInput(e.target.value)}
            placeholder="nº de trámite o ruta (p.ej. 82)"
            className={inputClass}
          />
        </label>
        <label className="flex flex-col gap-1 text-xs text-slate-500">
          Desde
          <input type="datetime-local" value={fromInput} onChange={(e) => setFromInput(e.target.value)} className={inputClass} />
        </label>
        <label className="flex flex-col gap-1 text-xs text-slate-500">
          Hasta
          <input type="datetime-local" value={toInput} onChange={(e) => setToInput(e.target.value)} className={inputClass} />
        </label>
        <button type="submit" className="rounded-md bg-[#557EFF] px-3 py-1.5 text-sm font-medium text-white">
          Aplicar
        </button>
        <button
          type="button"
          onClick={clearFilters}
          className="rounded-md border border-slate-300 px-3 py-1.5 text-sm text-[#557EFF]"
        >
          Limpiar
        </button>
        <span className="ml-auto text-sm text-slate-500">{total} registros</span>
      </form>

      {loading && <p className="text-sm text-slate-500">Cargando…</p>}
      {error && <p className="text-sm text-[#FF4E00]">{error}</p>}
      {!loading && !error && data.length === 0 && <p className="text-sm text-slate-500">Sin logs.</p>}

      {!loading && !error && data.length > 0 && (
        <div className="overflow-x-auto rounded-lg border border-slate-200 dark:border-slate-700">
          <table className="w-full text-left text-sm">
            <thead className="bg-slate-50 text-slate-500 dark:bg-slate-800">
              <tr>
                <th className="px-3 py-2" />
                <th className="px-3 py-2">Fecha</th>
                <th className="px-3 py-2">Tipo</th>
                <th className="px-3 py-2">Dir</th>
                <th className="px-3 py-2">Método</th>
                <th className="px-3 py-2">Ruta</th>
                <th className="px-3 py-2">Estado</th>
                <th className="px-3 py-2">ms</th>
                <th className="px-3 py-2">Correlación</th>
              </tr>
            </thead>
            <tbody>
              {data.map((row) => {
                const open = expandedId === row.id;
                return (
                  <Fragment key={row.id}>
                    <tr
                      onClick={() => setExpandedId(open ? null : row.id)}
                      className="cursor-pointer border-t border-slate-100 hover:bg-slate-50 dark:border-slate-700 dark:hover:bg-slate-800/50"
                    >
                      <td className="px-3 py-2 text-slate-400">{open ? "▾" : "▸"}</td>
                      <td className="px-3 py-2 whitespace-nowrap">{new Date(row.createdAt).toLocaleString()}</td>
                      <td className="px-3 py-2">{row.logType}</td>
                      <td className="px-3 py-2">{row.direction}</td>
                      <td className="px-3 py-2">{row.method}</td>
                      <td className="px-3 py-2 font-mono text-xs">{row.path}</td>
                      <td className="px-3 py-2">{row.statusCode}</td>
                      <td className="px-3 py-2">{row.durationMs}</td>
                      <td className="px-3 py-2 font-mono text-xs">{row.correlationId?.slice(0, 8) ?? "—"}</td>
                    </tr>
                    {open && (
                      <tr className="border-t border-slate-100 dark:border-slate-700">
                        <td colSpan={9} className="bg-slate-50/60 px-3 py-3 dark:bg-slate-800/40">
                          <LogDetail row={row} />
                        </td>
                      </tr>
                    )}
                  </Fragment>
                );
              })}
            </tbody>
          </table>
        </div>
      )}

      <div className="flex items-center gap-2">
        <button
          type="button"
          disabled={page <= 1}
          onClick={() => setPage((p) => Math.max(1, p - 1))}
          className="rounded border border-slate-300 px-2 py-1 text-sm disabled:opacity-40"
        >
          Anterior
        </button>
        <span className="text-sm text-slate-500">{page} / {totalPages}</span>
        <button
          type="button"
          disabled={page >= totalPages}
          onClick={() => setPage((p) => p + 1)}
          className="rounded border border-slate-300 px-2 py-1 text-sm disabled:opacity-40"
        >
          Siguiente
        </button>
      </div>
    </section>
  );
}

/** Detalle expandible de una entrada de log: cabeceras/request/response (ya redactados por el backend). */
function LogDetail({ row }: { row: IctLogEntry }) {
  const sections: Array<[string, string | null]> = [
    ["Cabeceras", row.headers],
    ["Request", row.request],
    ["Response", row.response],
  ];
  const present = sections.filter(([, value]) => value != null && value !== "");

  return (
    <div className="flex flex-col gap-3">
      <div className="flex flex-wrap gap-x-6 gap-y-1 text-xs text-slate-500">
        <span>ID: <span className="font-mono">{row.id}</span></span>
        <span>Correlación: <span className="font-mono">{row.correlationId ?? "—"}</span></span>
        <span>Usuario: <span className="font-mono">{row.usuario ?? "—"}</span></span>
        <span>Tenant: <span className="font-mono">{row.tenantId ?? "—"}</span></span>
      </div>
      {present.length === 0 ? (
        <p className="text-xs text-slate-400">Sin cuerpo capturado para esta entrada.</p>
      ) : (
        present.map(([label, value]) => <JsonBlock key={label} label={label} value={value as string} />)
      )}
    </div>
  );
}

function JsonBlock({ label, value }: { label: string; value: string }) {
  const [copied, setCopied] = useState(false);
  let pretty = value;
  try {
    pretty = JSON.stringify(JSON.parse(value), null, 2);
  } catch {
    // No es JSON: se muestra tal cual.
  }

  function copy() {
    void navigator.clipboard?.writeText(pretty).then(() => {
      setCopied(true);
      setTimeout(() => setCopied(false), 1500);
    });
  }

  return (
    <div className="flex flex-col gap-1">
      <div className="flex items-center gap-2">
        <span className="text-xs font-semibold text-[#162744] dark:text-slate-200">{label}</span>
        <button
          type="button"
          onClick={copy}
          className="rounded border border-slate-300 px-2 py-0.5 text-[11px] text-[#557EFF] hover:bg-[#557EFF]/10"
        >
          {copied ? "Copiado" : "Copiar"}
        </button>
      </div>
      <pre className="max-h-64 overflow-auto rounded border border-slate-200 bg-white p-2 font-mono text-[11px] leading-snug text-slate-700 dark:border-slate-700 dark:bg-slate-900 dark:text-slate-200">
        {pretty}
      </pre>
    </div>
  );
}

function AlertsTab() {
  return (
    <section className="flex flex-col gap-5">
      <div className="flex flex-col gap-2">
        <p className="text-xs text-slate-500">
          Indicadores en vivo del pipeline ICT. Configure reglas, umbrales y destinatarios en el módulo Reportes.
        </p>
        <IctAlertMetricsRow />
      </div>
      <IctAlertEventsList />
    </section>
  );
}

function IctAlertMetricsRow() {
  const [metrics, setMetrics] = useState<IctAlertMetrics | null>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    const controller = new AbortController();
    fetchIctAlerts(controller.signal)
      .then(setMetrics)
      .catch((e: unknown) => {
        if (!controller.signal.aborted) setError(e instanceof Error ? e.message : "Error al cargar las métricas");
      });
    return () => controller.abort();
  }, []);

  if (error) return <p className="text-sm text-[#FF4E00]">{error}</p>;
  if (!metrics) return <p className="text-sm text-slate-500">Cargando métricas…</p>;

  return (
    <div className="grid grid-cols-1 gap-3 sm:grid-cols-2 lg:grid-cols-4">
      <MetricCard label="Atascados en validación" value={metrics.stuckInValidation} warn={metrics.stuckInValidation > 0} />
      <MetricCard label="Tasa de novedades" value={`${metrics.noveltyRatePct}%`} warn={metrics.noveltyRatePct > 20} />
      <MetricCard label="Fallos de webhook (24h)" value={metrics.webhookDeliveryFailures} warn={metrics.webhookDeliveryFailures > 0} />
      <MetricCard label="Jobs fuera de SLA" value={metrics.jobsOutOfSla} warn={metrics.jobsOutOfSla > 0} />
    </div>
  );
}

function MetricCard({ label, value, warn }: { label: string; value: number | string; warn: boolean }) {
  return (
    <div className={`rounded-lg border p-4 ${warn ? "border-[#FF4E00]/40 bg-[#FF4E00]/5" : "border-slate-200 dark:border-slate-700"}`}>
      <p className="text-sm text-slate-500">{label}</p>
      <p className={`mt-1 text-2xl font-semibold ${warn ? "text-[#FF4E00]" : "text-[#162744] dark:text-white"}`}>{value}</p>
    </div>
  );
}

/** Prefijo de las métricas de alerta ICT en el subsistema de Reportes (analytics.alert_rules). */
const ICT_METRIC_PREFIX = "ict_";
const ICT_EVENTS_PAGE_SIZE = 50;

/**
 * Historial de disparos de alerta ICT + reconocimiento (acknowledge). Reutiliza el subsistema de
 * Reportes (analytics.alert_events) filtrando a las reglas cuya métrica es ict_*. Degrada de forma
 * suave si el usuario no tiene acceso a analytics (el CRUD completo vive en el módulo Reportes).
 */
function IctAlertEventsList() {
  const [events, setEvents] = useState<AlertEvent[]>([]);
  const [status, setStatus] = useState<UiStatus>("loading");
  const [acking, setAcking] = useState<string | null>(null);

  // El endpoint de alert-events exige tenantId al SuperAdmin (sin vista global); para el resto lo deriva
  // del token. Se toma el tenant del propio JWT.
  const tenantId = useMemo<string | undefined>(() => {
    if (typeof window === "undefined") return undefined;
    const payload = decodeJwtPayload(window.localStorage.getItem(TOKEN_STORAGE_KEY));
    return isSuperAdmin(payload) ? payload?.tenant_id : undefined;
  }, []);

  const load = useCallback(async () => {
    setStatus("loading");
    try {
      const [rulesRes, eventsRes] = await Promise.all([
        fetchAlertRules(tenantId),
        fetchAlertEvents({ page: 1, pageSize: ICT_EVENTS_PAGE_SIZE, tenantId }),
      ]);
      const ictRuleIds = new Set(
        rulesRes.items.filter((r) => r.metric.startsWith(ICT_METRIC_PREFIX)).map((r) => r.id),
      );
      const ictEvents = eventsRes.items.filter((e) => ictRuleIds.has(e.alertRuleId));
      setEvents(ictEvents);
      setStatus(ictEvents.length === 0 ? "empty" : "ready");
    } catch {
      setStatus("error");
    }
  }, [tenantId]);

  useEffect(() => {
    // eslint-disable-next-line react-hooks/set-state-in-effect -- carga async: los setState ocurren tras el await
    void load();
  }, [load]);

  async function handleAck(id: string) {
    setAcking(id);
    try {
      const updated = await acknowledgeAlertEvent(id, tenantId);
      setEvents((prev) => prev.map((ev) => (ev.id === id ? updated : ev)));
    } catch {
      // Silencioso: el estado no cambia; el usuario puede reintentar.
    } finally {
      setAcking(null);
    }
  }

  return (
    <div className="flex flex-col gap-2">
      <h2 className="text-sm font-semibold text-[#162744] dark:text-slate-100">Eventos de alerta ICT</h2>
      <UiStateBoundary
        status={status}
        emptyMessage="Sin disparos de alerta ICT registrados."
        errorMessage="El historial de alertas ICT no está disponible aquí; gestiónelo en el módulo Reportes."
        onRetry={() => void load()}
        skeletonRows={3}
      >
        <div className="overflow-x-auto rounded-lg border border-slate-200 dark:border-slate-700">
          <table className="w-full text-left text-sm">
            <thead className="bg-slate-50 text-slate-500 dark:bg-slate-800">
              <tr>
                <th className="px-3 py-2">Fecha</th>
                <th className="px-3 py-2">Alerta</th>
                <th className="px-3 py-2 text-right">Valor</th>
                <th className="px-3 py-2 text-right">Umbral</th>
                <th className="px-3 py-2">Detalle</th>
                <th className="px-3 py-2">Acción</th>
              </tr>
            </thead>
            <tbody>
              {events.map((e) => (
                <tr key={e.id} className="border-t border-slate-100 text-[#162744] dark:border-slate-700 dark:text-slate-200">
                  <td className="px-3 py-2 whitespace-nowrap">{new Date(e.triggeredAt).toLocaleString()}</td>
                  <td className="px-3 py-2">{e.ruleName}</td>
                  <td className="px-3 py-2 text-right font-semibold text-[#557EFF]">{e.metricValue}</td>
                  <td className="px-3 py-2 text-right">{e.threshold}</td>
                  <td className="px-3 py-2 max-w-[280px] truncate" title={e.message ?? undefined}>{e.message ?? "—"}</td>
                  <td className="px-3 py-2 whitespace-nowrap">
                    {e.acknowledgedAt ? (
                      <span className="inline-flex items-center gap-1 rounded-full bg-[#557EFF]/10 px-2 py-0.5 text-[11px] font-semibold text-[#557EFF]">
                        <Check className="h-3 w-3" aria-hidden="true" /> Reconocida
                      </span>
                    ) : (
                      <button
                        type="button"
                        disabled={acking === e.id}
                        onClick={() => void handleAck(e.id)}
                        className="inline-flex items-center gap-1 rounded-lg border border-slate-300 px-2 py-1 text-xs text-[#557EFF] hover:bg-[#557EFF]/10 disabled:opacity-40"
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
      </UiStateBoundary>
    </div>
  );
}
