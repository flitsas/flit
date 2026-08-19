"use client";

// Reportes de Integración con Terceros (ICT) — HU #11619.
//
// Antes de esta HU, los 4 tipos de informe ICT (novedades/atascados/jobs/webhooks) solo existían
// como adjunto de un correo programado: no había forma de verlos en pantalla sin programar nada y
// esperar. Aquí se muestran en vivo, con la misma agregación que ya arma el Excel (HU #11617), más
// las Consultas personalizadas (HU #11608) y su Programación — todo lo que antes vivía apretujado
// dentro de "Log ICT" y ahora tiene su propio espacio de navegación ("Reportes ICT"), separado de
// los logs técnicos (que se quedan en Log ICT).
import { useCallback, useEffect, useMemo, useState } from "react";
import { CalendarClock } from "lucide-react";
import {
  CARDLIST_CELL,
  CARDLIST_HEAD_ROW,
  CARDLIST_ROW,
  CARDLIST_SCROLL,
  CARDLIST_TABLE,
  CARDLIST_TH,
} from "@/components/atom/table-cardlist";
import { Empty, ErrorNotice, PrimaryButton, Section } from "@/components/consultas/ui";
import {
  fetchIctAtascadosReport,
  fetchIctJobsReport,
  fetchIctNovedadesReport,
  fetchIctWebhooksReport,
  type IctAtascadosReport,
  type IctJobsReport,
  type IctNovedadesReport,
  type IctWebhooksReport,
} from "@/lib/api/ict-reports";
import type { ReportType } from "@/lib/api/analytics-scheduling";
import { decodeJwtPayload, isSuperAdmin, TOKEN_STORAGE_KEY } from "@/lib/auth/jwt";
import { SchedulingPanel } from "@/components/atom/modules/_reportes/scheduling/SchedulingPanel";
import type { SchedulePresetConsulta } from "@/components/atom/modules/_reportes/scheduling/ScheduleForm";
import { IctQueriesTab } from "./_ict/IctQueriesTab";

type Tab = "novedades" | "atascados" | "jobs" | "webhooks" | "consultas";

/**
 * Los 4 tipos de informe programado que ofrece este módulo (Reportes 2.0, HU-D, cuarta ola).
 * "ict_jobs" queda fuera para quien no es SuperAdmin: `ict.job_runs` es platform-wide, sin
 * `tenant_id`, así que el backend (`ReportSchedulesEndpoints`) lo rechaza para cualquier otro rol
 * — mostrarlo igual solo llevaría a un 403 después de llenar el formulario.
 */
function ictReportTypes(isSuper: boolean): ReportType[] {
  const base: ReportType[] = ["ict_novedades", "ict_atascados", "ict_webhooks"];
  return isSuper ? [...base, "ict_jobs"] : base;
}

/**
 * Compañía sobre la que corren las consultas, la programación y los reportes en vivo de este
 * módulo. El resto de usuarios va con la suya (el backend la resuelve del token); a SuperAdmin,
 * sin un selector propio de compañía en este módulo, se le resuelve con su propio `tenant_id`.
 */
function useIctTenantId(): { tenantId: string | undefined; isSuper: boolean } {
  return useMemo(() => {
    if (typeof window === "undefined") return { tenantId: undefined, isSuper: false };
    const payload = decodeJwtPayload(window.localStorage.getItem(TOKEN_STORAGE_KEY));
    const isSuper = isSuperAdmin(payload);
    return { tenantId: isSuper ? payload?.tenant_id : undefined, isSuper };
  }, []);
}

function toIsoDate(d: Date): string {
  return d.toISOString().slice(0, 10);
}

/** Rango por defecto: últimos 30 días, mismo criterio que el resto de Reportes. */
function defaultRange(reference: Date = new Date()): { from: string; to: string } {
  const start = new Date(reference);
  start.setDate(start.getDate() - 29);
  return { from: toIsoDate(start), to: toIsoDate(reference) };
}

export function IctReports() {
  const [tab, setTab] = useState<Tab>("novedades");
  const [schedulingOpen, setSchedulingOpen] = useState(false);
  // "Programar este informe" en una consulta guardada de ICT (Reportes 2.0, HU-D, cuarta ola):
  // mismo mecanismo que OtReportsConsole/Reportes.tsx, con savedQueryScope="ict" fijo.
  const [schedulePreset, setSchedulePreset] = useState<SchedulePresetConsulta | null>(null);
  const { tenantId, isSuper } = useIctTenantId();
  const allowedReportTypes = useMemo(() => ictReportTypes(isSuper), [isSuper]);

  return (
    <div className="flex flex-col gap-4 p-4">
      <header className="flex flex-wrap items-center justify-between gap-3">
        <h1 className="text-xl font-semibold text-[#162744] dark:text-white">Integración con Terceros — Reportes</h1>
        <div className="flex flex-wrap items-center gap-2">
          <nav className="flex flex-wrap gap-2" role="tablist">
            <TabButton active={tab === "novedades"} onClick={() => setTab("novedades")}>Novedades</TabButton>
            <TabButton active={tab === "atascados"} onClick={() => setTab("atascados")}>Atascados</TabButton>
            {isSuper && <TabButton active={tab === "jobs"} onClick={() => setTab("jobs")}>Jobs</TabButton>}
            <TabButton active={tab === "webhooks"} onClick={() => setTab("webhooks")}>Webhooks</TabButton>
            <TabButton active={tab === "consultas"} onClick={() => setTab("consultas")}>Consultas</TabButton>
          </nav>
          <button
            type="button"
            onClick={() => setSchedulingOpen(true)}
            className="inline-flex items-center gap-2 rounded-xl border border-slate-300 px-3 py-1.5 text-sm font-medium text-[#162744] hover:bg-[#F4F7FC] dark:border-slate-700 dark:text-white dark:hover:bg-white/5"
            data-testid="ict-reportes-abrir-programacion"
          >
            <CalendarClock className="h-4 w-4" aria-hidden="true" />
            Programación
          </button>
        </div>
      </header>

      {tab === "novedades" && <NovedadesTab tenantId={tenantId} />}
      {tab === "atascados" && <AtascadosTab tenantId={tenantId} />}
      {tab === "jobs" && isSuper && <JobsTab />}
      {tab === "webhooks" && <WebhooksTab tenantId={tenantId} />}
      {tab === "consultas" && (
        <IctQueriesTab
          tenantId={tenantId}
          onScheduleQuery={(query) => {
            setSchedulePreset({ savedQueryId: query.id, savedQueryScope: "ict", queryName: query.nombre });
            setSchedulingOpen(true);
          }}
        />
      )}

      <SchedulingPanel
        open={schedulingOpen}
        onClose={() => {
          setSchedulingOpen(false);
          setSchedulePreset(null);
        }}
        tenantId={tenantId}
        presetConsulta={schedulePreset}
        onConsumePreset={() => setSchedulePreset(null)}
        allowedReportTypes={allowedReportTypes}
      />
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

/** Tabla genérica de detalle: misma receta visual "lista de tarjetas" que el resto de Reportes. */
function Table({ headers, rows }: { headers: string[]; rows: { key: string; cells: string[] }[] }) {
  return (
    <div className={CARDLIST_SCROLL}>
      <table className={`min-w-[34rem] ${CARDLIST_TABLE}`}>
        <thead>
          <tr className={CARDLIST_HEAD_ROW}>
            {headers.map((h) => (
              <th key={h} className={CARDLIST_TH}>{h}</th>
            ))}
          </tr>
        </thead>
        <tbody>
          {rows.map((row) => (
            <tr key={row.key} className={CARDLIST_ROW}>
              {row.cells.map((cell, i) => (
                <td key={headers[i]} className={`${CARDLIST_CELL} ${i === 0 ? "" : "tabular-nums"}`}>{cell}</td>
              ))}
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}

function SummaryCard({ label, value }: { label: string; value: number | string }) {
  return (
    <div className="rounded-xl border border-[#DFE5ED] px-3 py-2 dark:border-white/10">
      <p className="text-xl font-semibold tabular-nums text-[#162744] dark:text-white">{value}</p>
      <p className="text-[11px] text-[#6B7280] dark:text-white/50">{label}</p>
    </div>
  );
}

function fmt(iso: string): string {
  const d = new Date(iso);
  return Number.isNaN(d.getTime()) ? iso : d.toLocaleString();
}

function DateRangeFilter({
  from,
  to,
  onChange,
  onApply,
  busy,
}: {
  from: string;
  to: string;
  onChange: (range: { from: string; to: string }) => void;
  onApply: () => void;
  busy: boolean;
}) {
  return (
    <div className="flex flex-wrap items-end gap-3">
      <label className="flex flex-col gap-1 text-xs text-slate-500">
        Desde
        <input
          type="date"
          value={from}
          onChange={(e) => onChange({ from: e.target.value, to })}
          className="rounded border border-slate-300 bg-white px-2 py-1 text-sm dark:bg-slate-800 dark:text-white"
        />
      </label>
      <label className="flex flex-col gap-1 text-xs text-slate-500">
        Hasta
        <input
          type="date"
          value={to}
          onChange={(e) => onChange({ from, to: e.target.value })}
          className="rounded border border-slate-300 bg-white px-2 py-1 text-sm dark:bg-slate-800 dark:text-white"
        />
      </label>
      <PrimaryButton onClick={onApply} disabled={busy}>
        {busy ? "Cargando…" : "Actualizar"}
      </PrimaryButton>
    </div>
  );
}

function NovedadesTab({ tenantId }: { tenantId?: string }) {
  const [range, setRange] = useState(defaultRange);
  const [report, setReport] = useState<IctNovedadesReport | null>(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const load = useCallback(async () => {
    setBusy(true);
    setError(null);
    try {
      setReport(await fetchIctNovedadesReport(range, tenantId));
    } catch (e: unknown) {
      setError(e instanceof Error ? e.message : "No se pudo cargar el reporte de novedades.");
    } finally {
      setBusy(false);
    }
  }, [range, tenantId]);

  useEffect(() => {
    // eslint-disable-next-line react-hooks/set-state-in-effect -- carga async: los setState ocurren tras el await
    void load();
    // eslint-disable-next-line react-hooks/exhaustive-deps -- solo al montar; "Actualizar" relanza con el rango vigente
  }, []);

  return (
    <section className="flex flex-col gap-4" data-testid="ict-novedades-tab">
      <Section title="Novedades por causa" testId="ict-novedades-filtros">
        <DateRangeFilter from={range.from} to={range.to} onChange={setRange} onApply={() => void load()} busy={busy} />
      </Section>

      {error && <ErrorNotice message={error} />}

      {report && (
        <>
          <div className="grid grid-cols-2 gap-3 lg:grid-cols-4">
            <SummaryCard label="Novedades en el periodo" value={report.total} />
            {report.resumenPorCausa.slice(0, 3).map((r) => (
              <SummaryCard key={r.causa} label={r.causa} value={`${r.cantidad} (${r.porcentajeTexto})`} />
            ))}
          </div>

          {report.detalle.length === 0 ? (
            <Empty>Sin novedades en el rango seleccionado.</Empty>
          ) : (
            <Table
              headers={["Placa", "VIN", "Radicado", "Comentarios", "Registrado"]}
              rows={report.detalle.map((d, i) => ({
                key: `${d.radicado ?? d.placa ?? d.vin ?? i}`,
                cells: [d.placa ?? "—", d.vin ?? "—", d.radicado ?? "—", d.comentarios ?? "—", fmt(d.registradoEn)],
              }))}
            />
          )}
          {report.truncated && (
            <p className="text-[11px] text-amber-700 dark:text-amber-400">
              El detalle se limitó a las primeras filas del periodo; hay más novedades de las mostradas.
            </p>
          )}
        </>
      )}
    </section>
  );
}

function AtascadosTab({ tenantId }: { tenantId?: string }) {
  const [report, setReport] = useState<IctAtascadosReport | null>(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const load = useCallback(async () => {
    setBusy(true);
    setError(null);
    try {
      setReport(await fetchIctAtascadosReport(tenantId));
    } catch (e: unknown) {
      setError(e instanceof Error ? e.message : "No se pudo cargar el reporte de atascados.");
    } finally {
      setBusy(false);
    }
  }, [tenantId]);

  useEffect(() => {
    // eslint-disable-next-line react-hooks/set-state-in-effect -- carga async: los setState ocurren tras el await
    void load();
  }, [load]);

  return (
    <section className="flex flex-col gap-4" data-testid="ict-atascados-tab">
      <Section
        title="Atascados en validación ahora mismo"
        testId="ict-atascados-filtros"
        hint="Esta pestaña no lleva rango de fechas: siempre enseña el estado de este momento."
      >
        <PrimaryButton onClick={() => void load()} disabled={busy}>
          {busy ? "Cargando…" : "Actualizar"}
        </PrimaryButton>
      </Section>

      {error && <ErrorNotice message={error} />}

      {report && (
        <>
          <div className="grid grid-cols-2 gap-3 lg:grid-cols-4">
            <SummaryCard label="Atascados" value={report.total} />
          </div>

          {report.detalle.length === 0 ? (
            <Empty>No hay pre-trámites atascados en validación.</Empty>
          ) : (
            <Table
              headers={["Placa", "VIN", "Radicado", "Esperando", "Días transcurridos"]}
              rows={report.detalle.map((d, i) => ({
                key: `${d.radicado ?? d.placa ?? d.vin ?? i}`,
                cells: [d.placa ?? "—", d.vin ?? "—", d.radicado ?? "—", d.esperando, d.diasTranscurridos.toFixed(1)],
              }))}
            />
          )}
          {report.truncated && (
            <p className="text-[11px] text-amber-700 dark:text-amber-400">
              El detalle se limitó a las primeras filas; hay más atascados de los mostrados.
            </p>
          )}
        </>
      )}
    </section>
  );
}

function JobsTab() {
  const [range, setRange] = useState(defaultRange);
  const [report, setReport] = useState<IctJobsReport | null>(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const load = useCallback(async () => {
    setBusy(true);
    setError(null);
    try {
      setReport(await fetchIctJobsReport(range));
    } catch (e: unknown) {
      setError(e instanceof Error ? e.message : "No se pudo cargar el reporte de jobs.");
    } finally {
      setBusy(false);
    }
  }, [range]);

  useEffect(() => {
    // eslint-disable-next-line react-hooks/set-state-in-effect -- carga async: los setState ocurren tras el await
    void load();
    // eslint-disable-next-line react-hooks/exhaustive-deps -- solo al montar; "Actualizar" relanza con el rango vigente
  }, []);

  return (
    <section className="flex flex-col gap-4" data-testid="ict-jobs-tab">
      <Section
        title="Rendimiento de los jobs del pipeline"
        testId="ict-jobs-filtros"
        hint="Reporte de plataforma: cubre todas las compañías, no solo la suya."
      >
        <DateRangeFilter from={range.from} to={range.to} onChange={setRange} onApply={() => void load()} busy={busy} />
      </Section>

      {error && <ErrorNotice message={error} />}

      {report && (
        <>
          {report.resumenPorJob.length === 0 ? (
            <Empty>Sin corridas de jobs en el rango seleccionado.</Empty>
          ) : (
            <Table
              headers={["Job", "Corridas", "Duración prom. (s)", "Duración máx. (s)", "% fuera de SLA"]}
              rows={report.resumenPorJob.map((r) => ({
                key: r.job,
                cells: [
                  r.job,
                  String(r.corridas),
                  r.duracionPromedioSeg.toFixed(1),
                  r.duracionMaximaSeg.toFixed(1),
                  r.porcentajeFueraDeSlaTexto,
                ],
              }))}
            />
          )}

          {report.corridasFueraDeSla.length > 0 && (
            <>
              <p className="text-[11px] font-semibold uppercase tracking-wide text-[#6B7280] dark:text-white/50">
                Corridas fuera de SLA
              </p>
              <Table
                headers={["Job", "Resultado", "Duración (s)", "Inicio"]}
                rows={report.corridasFueraDeSla.map((c, i) => ({
                  key: `${c.job}-${c.inicio}-${i}`,
                  cells: [c.job, c.resultado, c.duracionSeg.toFixed(1), fmt(c.inicio)],
                }))}
              />
            </>
          )}
          {report.truncated && (
            <p className="text-[11px] text-amber-700 dark:text-amber-400">
              El detalle se limitó a las primeras corridas; hay más de las mostradas.
            </p>
          )}
        </>
      )}
    </section>
  );
}

function WebhooksTab({ tenantId }: { tenantId?: string }) {
  const [range, setRange] = useState(defaultRange);
  const [report, setReport] = useState<IctWebhooksReport | null>(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const load = useCallback(async () => {
    setBusy(true);
    setError(null);
    try {
      setReport(await fetchIctWebhooksReport(range, tenantId));
    } catch (e: unknown) {
      setError(e instanceof Error ? e.message : "No se pudo cargar el reporte de webhooks.");
    } finally {
      setBusy(false);
    }
  }, [range, tenantId]);

  useEffect(() => {
    // eslint-disable-next-line react-hooks/set-state-in-effect -- carga async: los setState ocurren tras el await
    void load();
    // eslint-disable-next-line react-hooks/exhaustive-deps -- solo al montar; "Actualizar" relanza con el rango vigente
  }, []);

  return (
    <section className="flex flex-col gap-4" data-testid="ict-webhooks-tab">
      <Section title="Trazabilidad de entrega de webhooks" testId="ict-webhooks-filtros">
        <DateRangeFilter from={range.from} to={range.to} onChange={setRange} onApply={() => void load()} busy={busy} />
      </Section>

      {error && <ErrorNotice message={error} />}

      {report && (
        <>
          <div className="grid grid-cols-2 gap-3 lg:grid-cols-4">
            <SummaryCard label="Webhooks en el periodo" value={report.total} />
          </div>

          {report.detalle.length === 0 ? (
            <Empty>Sin webhooks en el rango seleccionado.</Empty>
          ) : (
            <Table
              headers={["Radicado", "Estado", "Intentos", "URL destino", "Registrado"]}
              rows={report.detalle.map((w, i) => ({
                key: `${w.radicado}-${i}`,
                cells: [w.radicado, w.estado, String(w.intentos), w.urlDestino ?? "—", fmt(w.registradoEn)],
              }))}
            />
          )}
          {report.truncated && (
            <p className="text-[11px] text-amber-700 dark:text-amber-400">
              El detalle se limitó a las primeras filas del periodo; hay más webhooks de los mostrados.
            </p>
          )}
        </>
      )}
    </section>
  );
}
