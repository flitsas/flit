"use client";

// Submódulo de observabilidad ICT (Integración con Terceros) — HU10893.
// Dos pestañas: Logs (redactados/enmascarados por el backend) y Alertas ICT (métricas).
import { Fragment, useEffect, useState } from "react";
import {
  fetchIctAlerts,
  fetchIctLogs,
  type IctAlertMetrics,
  type IctLogEntry,
  type IctLogFilters,
  type IctLogType,
} from "@/lib/api/ict-client";

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
  const [correlationInput, setCorrelationInput] = useState("");
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
        if (!controller.signal.aborted) setError(e instanceof Error ? e.message : "Error al cargar los logs");
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
      correlationId: correlationInput.trim() || undefined,
      from: fromInput ? new Date(fromInput).toISOString() : undefined,
      to: toInput ? new Date(toInput).toISOString() : undefined,
    });
  }

  function clearFilters() {
    setLogTypeInput("");
    setCorrelationInput("");
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
          Correlación
          <input
            type="text"
            value={correlationInput}
            onChange={(e) => setCorrelationInput(e.target.value)}
            placeholder="correlation id"
            className={`${inputClass} font-mono`}
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
  const [metrics, setMetrics] = useState<IctAlertMetrics | null>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    const controller = new AbortController();
    fetchIctAlerts(controller.signal)
      .then(setMetrics)
      .catch((e: unknown) => {
        if (!controller.signal.aborted) setError(e instanceof Error ? e.message : "Error al cargar las alertas");
      });
    return () => controller.abort();
  }, []);

  if (error) return <p className="text-sm text-[#FF4E00]">{error}</p>;
  if (!metrics) return <p className="text-sm text-slate-500">Cargando…</p>;

  return (
    <section className="grid grid-cols-1 gap-3 sm:grid-cols-2 lg:grid-cols-4">
      <MetricCard label="Atascados en validación" value={metrics.stuckInValidation} warn={metrics.stuckInValidation > 0} />
      <MetricCard label="Tasa de novedades" value={`${metrics.noveltyRatePct}%`} warn={metrics.noveltyRatePct > 20} />
      <MetricCard label="Fallos de webhook (24h)" value={metrics.webhookDeliveryFailures} warn={metrics.webhookDeliveryFailures > 0} />
      <MetricCard label="Jobs fuera de SLA" value={metrics.jobsOutOfSla} warn={metrics.jobsOutOfSla > 0} />
    </section>
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
