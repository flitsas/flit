"use client";

// Submódulo de observabilidad ICT (Integración con Terceros) — HU10893.
// Dos pestañas: Logs (redactados/enmascarados por el backend) y Alertas ICT (métricas + eventos).
// Las Consultas personalizadas y la Programación de informes viven en su propio módulo, "Reportes
// ICT" (HU #11619) — separar los logs técnicos de los reportes de autoservicio.
import { Fragment, useCallback, useEffect, useMemo, useState } from "react";
import { Check, ChevronRight, X } from "lucide-react";
import { UiStateBoundary, type UiStatus } from "@/components/admin/UiStateBoundary";
import { CarLoader, CarLoaderModal } from "@/components/atom/CarLoader";
import { PageNav } from "@/components/atom/PageNav";
import { StatusBadge, type StatusTone } from "@/components/atom/StatusBadge";
import {
  TABLA_HEADER_BG,
  TABLA_HEADER_FG,
  TABLA_ROW_HOVER_CLS,
} from "@/components/atom/table-styles";
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
import { ModuleTitle } from "./ModuleTitle";
import { ReportesTabBar } from "./_reportes/ReportesTabBar";

type Tab = "logs" | "alertas";

const LOG_TYPES: ReadonlyArray<{ value: IctLogType; label: string }> = [
  { value: "auth", label: "Autenticación" },
  { value: "transaction", label: "Transacción" },
  { value: "webhook", label: "Webhook" },
  { value: "external", label: "Fuente externa" },
];

const PAGE_SIZE = 25;
const BORDER = "#DFE5ED";

const inputCls =
  "rounded-lg border border-[#D9DEE8] bg-white px-2.5 py-2 text-xs outline-none focus:border-[#557EFF] focus:ring-2 focus:ring-[#557EFF]/20 dark:border-white/15 dark:bg-[#0B0F14]";

const ghostCls =
  "inline-flex items-center gap-1.5 rounded-lg border border-[#D9DEE8] px-3 py-2 text-xs font-medium opacity-80 hover:border-[#557EFF] hover:opacity-100 dark:border-white/15";

export function IctLogs() {
  const [tab, setTab] = useState<Tab>("logs");

  return (
    <div className="app-bg flex min-h-screen flex-col gap-4 px-6 pt-6 pb-24 text-[#162744] dark:text-white">
      <ModuleTitle
        title="Log ICT"
        subtitle="Peticiones HTTP de la integración con terceros, ya enmascaradas. El recorrido por trámite está en Trazabilidad ICT."
      />
      <ReportesTabBar
        tabs={[
          { id: "logs", label: "Logs" },
          { id: "alertas", label: "Alertas ICT" },
        ]}
        activeId={tab}
        onChange={(id) => setTab(id as Tab)}
        ariaLabel="Secciones del Log ICT"
      />
      {tab === "logs" ? <LogsTab /> : <AlertsTab />}
    </div>
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
    // Carga al montar y al cambiar filtros: skeleton intencional.
    // eslint-disable-next-line react-hooks/set-state-in-effect
    setLoading(true);
    fetchIctLogs({ ...applied, page, pageSize: PAGE_SIZE }, controller.signal)
      .then((res) => {
        setData(res.items);
        setTotal(res.total);
        setError(null);
      })
      .catch((e: unknown) => {
        if (e instanceof DOMException && e.name === "AbortError") return;
        if (!controller.signal.aborted) {
          setError("No se pudieron cargar los logs. Revisa los filtros e inténtalo de nuevo.");
        }
      })
      .finally(() => {
        if (!controller.signal.aborted) setLoading(false);
      });
    return () => controller.abort();
  }, [applied, page]);

  const totalPages = Math.max(1, Math.ceil(total / PAGE_SIZE));
  const hayFiltros = Boolean(applied.logType || applied.search || applied.from || applied.to);

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

  const status: UiStatus = error
    ? "error"
    : data.length === 0
      ? "empty"
      : "ready";

  return (
    <section className="flex flex-col gap-4">
      <form
        onSubmit={applyFilters}
        className="flex shrink-0 flex-wrap items-end gap-2 rounded-2xl border border-[#DFE5ED] bg-white p-3 dark:border-white/10 dark:bg-[#0B0F14]"
        role="search"
        aria-label="Filtros del Log ICT"
      >
        <Campo label="Tipo" htmlFor="ict-log-tipo">
          <select
            id="ict-log-tipo"
            value={logTypeInput}
            onChange={(e) => setLogTypeInput(e.target.value as IctLogType | "")}
            className={inputCls}
          >
            <option value="">Todos</option>
            {LOG_TYPES.map((t) => (
              <option key={t.value} value={t.value}>
                {t.label}
              </option>
            ))}
          </select>
        </Campo>
        <Campo label="Buscar" htmlFor="ict-log-buscar">
          <input
            id="ict-log-buscar"
            type="text"
            value={searchInput}
            onChange={(e) => setSearchInput(e.target.value)}
            placeholder="N.º de trámite o ruta"
            className={`${inputCls} w-[210px]`}
          />
        </Campo>
        <Campo label="Desde" htmlFor="ict-log-desde">
          <input
            id="ict-log-desde"
            type="datetime-local"
            value={fromInput}
            onChange={(e) => setFromInput(e.target.value)}
            className={inputCls}
          />
        </Campo>
        <Campo label="Hasta" htmlFor="ict-log-hasta">
          <input
            id="ict-log-hasta"
            type="datetime-local"
            value={toInput}
            onChange={(e) => setToInput(e.target.value)}
            className={inputCls}
          />
        </Campo>
        <span className="flex-1" />
        {hayFiltros ? (
          <button type="button" onClick={clearFilters} className={ghostCls}>
            <X className="h-3.5 w-3.5" aria-hidden="true" /> Limpiar
          </button>
        ) : null}
        <button
          type="submit"
          disabled={loading}
          className="flex items-center gap-2 rounded-lg bg-[#557EFF] px-4 py-2 text-xs font-semibold text-white disabled:opacity-50 focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2"
        >
          Buscar
        </button>
      </form>

      {loading ? <CarLoaderModal label="Cargando logs ICT…" /> : null}

      <UiStateBoundary
        status={loading ? "ready" : status}
        errorMessage={error ?? "No se pudieron cargar los logs."}
        onRetry={() => {
          setApplied((prev) => ({ ...prev }));
        }}
        emptyMessage="Ningún log coincide con los filtros. Amplía el rango o quita algún filtro."
      >
        {!loading && data.length > 0 ? (
          <>
            <div className="overflow-x-auto">
              <table
                aria-label="Logs de integración ICT"
                className="w-full min-w-[960px] border-separate text-xs"
                style={{ borderSpacing: "0 8px" }}
              >
                <thead>
                  <tr
                    className="text-left text-[10px] font-semibold uppercase tracking-wider"
                    style={{ color: TABLA_HEADER_FG }}
                  >
                    <th className="rounded-l-xl px-3 py-2.5" style={{ background: TABLA_HEADER_BG, width: 34 }}>
                      <span className="sr-only">Detalle</span>
                    </th>
                    <th className="px-4 py-2.5" style={{ background: TABLA_HEADER_BG }}>
                      Fecha
                    </th>
                    <th className="px-4 py-2.5" style={{ background: TABLA_HEADER_BG }}>
                      Tipo
                    </th>
                    <th className="px-4 py-2.5" style={{ background: TABLA_HEADER_BG }}>
                      Dirección
                    </th>
                    <th className="px-4 py-2.5" style={{ background: TABLA_HEADER_BG }}>
                      Método
                    </th>
                    <th className="px-4 py-2.5" style={{ background: TABLA_HEADER_BG }}>
                      Ruta
                    </th>
                    <th className="px-4 py-2.5" style={{ background: TABLA_HEADER_BG }}>
                      Estado
                    </th>
                    <th className="px-4 py-2.5" style={{ background: TABLA_HEADER_BG }}>
                      Duración
                    </th>
                    <th className="rounded-r-xl px-4 py-2.5" style={{ background: TABLA_HEADER_BG }}>
                      Correlación
                    </th>
                  </tr>
                </thead>
                <tbody>
                  {data.map((row) => {
                    const open = expandedId === row.id;
                    return (
                      <Fragment key={row.id}>
                        <tr
                          className={`cursor-pointer bg-white text-xs dark:bg-[#162744] ${TABLA_ROW_HOVER_CLS}`}
                          onClick={() => setExpandedId(open ? null : row.id)}
                        >
                          <td
                            className="rounded-l-xl border-y border-l px-3 py-3 align-middle"
                            style={{ borderColor: BORDER }}
                          >
                            <ChevronRight
                              className={`h-4 w-4 text-[#557EFF] transition ${open ? "rotate-90" : ""}`}
                              aria-hidden="true"
                            />
                            <span className="sr-only">{open ? "Ocultar detalle" : "Ver detalle"}</span>
                          </td>
                          <td className="border-y px-4 py-3 align-middle whitespace-nowrap" style={{ borderColor: BORDER }}>
                            {new Date(row.createdAt).toLocaleString("es-CO")}
                          </td>
                          <td className="border-y px-4 py-3 align-middle" style={{ borderColor: BORDER }}>
                            <StatusBadge label={etiquetaTipo(row.logType)} tone="info" />
                          </td>
                          <td className="border-y px-4 py-3 align-middle" style={{ borderColor: BORDER }}>
                            {row.direction}
                          </td>
                          <td className="border-y px-4 py-3 align-middle font-medium" style={{ borderColor: BORDER }}>
                            {row.method}
                          </td>
                          <td
                            className="border-y px-4 py-3 align-middle font-mono text-xs"
                            style={{ borderColor: BORDER }}
                          >
                            {row.path}
                          </td>
                          <td className="border-y px-4 py-3 align-middle" style={{ borderColor: BORDER }}>
                            <StatusBadge label={String(row.statusCode)} tone={tonoHttp(row.statusCode)} />
                          </td>
                          <td className="border-y px-4 py-3 align-middle tabular-nums" style={{ borderColor: BORDER }}>
                            {row.durationMs} ms
                          </td>
                          <td
                            className="rounded-r-xl border-y border-r px-4 py-3 align-middle font-mono text-xs"
                            style={{ borderColor: BORDER }}
                          >
                            {row.correlationId?.slice(0, 8) ?? "—"}
                          </td>
                        </tr>
                        {open ? (
                          <tr>
                            <td colSpan={9} className="pb-2">
                              <div className="rounded-2xl border border-[#DFE5ED] bg-white p-4 dark:border-white/10 dark:bg-[#0B0F14]">
                                <LogDetail row={row} />
                              </div>
                            </td>
                          </tr>
                        ) : null}
                      </Fragment>
                    );
                  })}
                </tbody>
              </table>
            </div>

            <PageNav
              page={page}
              totalPages={totalPages}
              onPageChange={(p) => {
                setExpandedId(null);
                setPage(p);
              }}
              resumen={`Mostrando ${(page - 1) * PAGE_SIZE + 1}–${Math.min(page * PAGE_SIZE, total)} de ${total.toLocaleString("es-CO")} registros`}
              ariaLabel="Paginación del Log ICT"
            />
          </>
        ) : null}
      </UiStateBoundary>
    </section>
  );
}

function etiquetaTipo(tipo: IctLogType): string {
  return LOG_TYPES.find((t) => t.value === tipo)?.label ?? tipo;
}

function tonoHttp(code: number): StatusTone {
  if (code >= 200 && code < 300) return "success";
  if (code >= 400 && code < 500) return "warning";
  if (code >= 500) return "danger";
  return "neutral";
}

function Campo({
  label,
  htmlFor,
  children,
}: {
  label: string;
  htmlFor: string;
  children: React.ReactNode;
}) {
  return (
    <label htmlFor={htmlFor} className="flex min-w-0 flex-col gap-1 text-xs font-medium text-[#59677D] dark:text-white/70">
      {label}
      {children}
    </label>
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
      <div className="flex flex-wrap gap-x-6 gap-y-1 text-xs text-[#59677D] dark:text-white/70">
        <span>
          ID: <span className="font-mono text-[#162744] dark:text-white">{row.id}</span>
        </span>
        <span>
          Correlación:{" "}
          <span className="font-mono text-[#162744] dark:text-white">{row.correlationId ?? "—"}</span>
        </span>
        <span>
          Usuario: <span className="font-mono text-[#162744] dark:text-white">{row.usuario ?? "—"}</span>
        </span>
        <span>
          Tenant: <span className="font-mono text-[#162744] dark:text-white">{row.tenantId ?? "—"}</span>
        </span>
      </div>
      {present.length === 0 ? (
        <p className="text-xs text-[#59677D] dark:text-white/70">Sin cuerpo capturado para esta entrada.</p>
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
        <span className="text-xs font-semibold text-[#162744] dark:text-white">{label}</span>
        <button type="button" onClick={copy} className={ghostCls}>
          {copied ? "Copiado" : "Copiar"}
        </button>
      </div>
      <pre className="max-h-64 overflow-auto rounded-xl border border-[#DFE5ED] bg-white p-3 font-mono text-xs leading-snug text-[#162744] dark:border-white/10 dark:bg-[#0B0F14] dark:text-white">
        {pretty}
      </pre>
    </div>
  );
}

function AlertsTab() {
  return (
    <section className="flex flex-col gap-4">
      <p className="text-xs text-[#59677D] dark:text-white/70">
        Indicadores en vivo del pipeline ICT. Las reglas, umbrales y destinatarios se configuran en
        Reportes ICT.
      </p>
      <IctAlertMetricsRow />
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
        if (e instanceof DOMException && e.name === "AbortError") return;
        if (!controller.signal.aborted) {
          setError("No se pudieron cargar las métricas de alerta.");
        }
      });
    return () => controller.abort();
  }, []);

  if (error) return <p className="text-sm text-[#FF4E00]">{error}</p>;
  if (!metrics) {
    return (
      <div className="py-10">
        <CarLoader label="Cargando métricas de alerta…" />
      </div>
    );
  }

  return (
    <div className="grid grid-cols-1 gap-3 sm:grid-cols-2 lg:grid-cols-4">
      <MetricCard label="Atascados en validación" value={metrics.stuckInValidation} warn={metrics.stuckInValidation > 0} />
      <MetricCard label="Tasa de novedades" value={`${metrics.noveltyRatePct}%`} warn={metrics.noveltyRatePct > 20} />
      <MetricCard label="Fallos de webhook (24 h)" value={metrics.webhookDeliveryFailures} warn={metrics.webhookDeliveryFailures > 0} />
      <MetricCard label="Jobs fuera de SLA" value={metrics.jobsOutOfSla} warn={metrics.jobsOutOfSla > 0} />
    </div>
  );
}

function MetricCard({ label, value, warn }: { label: string; value: number | string; warn: boolean }) {
  return (
    <div
      className="rounded-2xl border bg-white p-5 dark:bg-[#162744]"
      style={{
        borderColor: warn ? "#FF4E00" : BORDER,
        background: warn ? "rgba(255, 78, 0, 0.06)" : undefined,
      }}
    >
      <p className="text-xs font-medium text-[#59677D] dark:text-white/70">{label}</p>
      <p className={`mt-1 text-2xl font-semibold ${warn ? "text-[#FF4E00]" : "text-[#162744] dark:text-white"}`}>
        {value}
      </p>
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
    <div className="flex flex-col gap-3">
      <h2 className="text-sm font-semibold text-[#162744] dark:text-white">Eventos de alerta ICT</h2>
      {acking ? <CarLoaderModal label="Reconociendo alerta…" /> : null}
      {status === "loading" ? (
        <div className="py-10">
          <CarLoader label="Cargando eventos de alerta…" />
        </div>
      ) : (
        <UiStateBoundary
          status={status}
          emptyMessage="Sin disparos de alerta ICT registrados."
          errorMessage="El historial de alertas ICT no está disponible aquí; gestiónelo en Reportes ICT."
          onRetry={() => void load()}
        >
          <div className="overflow-x-auto">
          <table
            aria-label="Eventos de alerta ICT"
            className="w-full border-separate text-xs"
            style={{ borderSpacing: "0 8px" }}
          >
            <thead>
              <tr
                className="text-left text-[10px] font-semibold uppercase tracking-wider"
                style={{ color: TABLA_HEADER_FG }}
              >
                <th className="rounded-l-xl px-4 py-2.5" style={{ background: TABLA_HEADER_BG }}>
                  Fecha
                </th>
                <th className="px-4 py-2.5" style={{ background: TABLA_HEADER_BG }}>
                  Alerta
                </th>
                <th className="px-4 py-2.5 text-right" style={{ background: TABLA_HEADER_BG }}>
                  Valor
                </th>
                <th className="px-4 py-2.5 text-right" style={{ background: TABLA_HEADER_BG }}>
                  Umbral
                </th>
                <th className="px-4 py-2.5" style={{ background: TABLA_HEADER_BG }}>
                  Detalle
                </th>
                <th className="rounded-r-xl px-4 py-2.5" style={{ background: TABLA_HEADER_BG }}>
                  Acción
                </th>
              </tr>
            </thead>
            <tbody>
              {events.map((e) => (
                <tr key={e.id} className={`bg-white text-xs dark:bg-[#162744] ${TABLA_ROW_HOVER_CLS}`}>
                  <td
                    className="rounded-l-xl border-y border-l px-4 py-3 align-middle whitespace-nowrap"
                    style={{ borderColor: BORDER }}
                  >
                    {new Date(e.triggeredAt).toLocaleString("es-CO")}
                  </td>
                  <td className="border-y px-4 py-3 align-middle" style={{ borderColor: BORDER }}>
                    {e.ruleName}
                  </td>
                  <td
                    className="border-y px-4 py-3 text-right align-middle font-semibold text-[#557EFF]"
                    style={{ borderColor: BORDER }}
                  >
                    {e.metricValue}
                  </td>
                  <td className="border-y px-4 py-3 text-right align-middle" style={{ borderColor: BORDER }}>
                    {e.threshold}
                  </td>
                  <td
                    className="max-w-[280px] truncate border-y px-4 py-3 align-middle"
                    style={{ borderColor: BORDER }}
                    title={e.message ?? undefined}
                  >
                    {e.message ?? "—"}
                  </td>
                  <td
                    className="rounded-r-xl border-y border-r px-4 py-3 align-middle whitespace-nowrap"
                    style={{ borderColor: BORDER }}
                  >
                    {e.acknowledgedAt ? (
                      <StatusBadge
                        label="Reconocida"
                        tone="success"
                        ariaLabel="Alerta reconocida"
                      />
                    ) : (
                      <button
                        type="button"
                        disabled={acking === e.id}
                        onClick={() => void handleAck(e.id)}
                        className="inline-flex items-center gap-1 rounded-xl border border-[#DFE5ED] bg-white px-3 py-1.5 text-xs font-semibold text-[#557EFF] hover:bg-[#EFF6FF] disabled:opacity-40 dark:border-white/15 dark:bg-[#0B0F14]"
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
      )}
    </div>
  );
}
