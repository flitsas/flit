"use client";

// Reportes de Integración con Terceros (ICT) — HU #11619.
//
// Antes de esta HU, los 4 tipos de informe ICT (novedades/atascados/jobs/webhooks) solo existían
// como adjunto de un correo programado: no había forma de verlos en pantalla sin programar nada y
// esperar. Aquí se muestran en vivo, con la misma agregación que ya arma el Excel (HU #11617), más
// las Consultas personalizadas (HU #11608) y su Programación — todo lo que antes vivía apretujado
// dentro de "Log ICT" y ahora tiene su propio espacio de navegación, separado de los logs técnicos.
//
// La consola se arma con las MISMAS piezas que las de empresa (`Reportes.tsx`) y organismo
// (`OtReportsConsole.tsx`): `ModuleTitle`, `ReportesTabBar`, el `DateRangeFilter` compartido y
// `useAnalyticsQuery` + `UiStateBoundary` para los cuatro estados de carga. No es una preferencia
// estética: quien ya sabe leer los reportes de su empresa no debería tener que reaprender dónde
// está el rango de fechas ni qué significa una pestaña activa al entrar a ICT. Por eso aquí no hay
// tabs de píldora, ni botón "Actualizar" —el rango recarga solo, como en el resto—, ni un `<h1>`
// propio compitiendo con el encabezado de módulo.
import { useCallback, useMemo, useState } from "react";
import { CalendarClock } from "lucide-react";
import { UiStateBoundary } from "@/components/admin/UiStateBoundary";
import {
  CARDLIST_CELL,
  CARDLIST_HEAD_ROW,
  CARDLIST_ROW,
  CARDLIST_SCROLL,
  CARDLIST_TABLE,
  CARDLIST_TH,
} from "@/components/atom/table-cardlist";
import type { ReportType } from "@/lib/api/analytics-scheduling";
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
import { decodeJwtPayload, isSuperAdmin, TOKEN_STORAGE_KEY } from "@/lib/auth/jwt";
import { ModuleTitle } from "./ModuleTitle";
import { DateRangeFilter } from "./_reportes/DateRangeFilter";
import { formatInt, formatNumber } from "./_reportes/format";
import { KpiCard } from "./_reportes/KpiCard";
import { defaultRange, isValidRange, type DateRange } from "./_reportes/range";
import { ReportesTabBar } from "./_reportes/ReportesTabBar";
import { useAnalyticsQuery } from "./_reportes/useAnalyticsQuery";
import { SchedulingPanel } from "./_reportes/scheduling/SchedulingPanel";
import type { SchedulePresetConsulta } from "./_reportes/scheduling/ScheduleForm";
import { IctQueriesTab } from "./_ict/IctQueriesTab";

type TabId = "novedades" | "atascados" | "jobs" | "webhooks" | "consultas";

/**
 * Pestañas de la consola. "Jobs" es `superOnly`: `ict.job_runs` es una tabla de plataforma, sin
 * `tenant_id`, así que el backend la reserva a SuperAdmin — enseñarla a los demás solo llevaría a
 * un 403 después de esperar la carga.
 */
const TAB_DEFS: ReadonlyArray<{ id: TabId; label: string; superOnly?: boolean }> = [
  { id: "novedades", label: "Novedades" },
  { id: "atascados", label: "Atascados" },
  { id: "jobs", label: "Jobs", superOnly: true },
  { id: "webhooks", label: "Webhooks" },
  { id: "consultas", label: "Consultas" },
];

/**
 * Pestañas gobernadas por el rango de fechas. "Atascados" mira el estado de este momento y
 * "Consultas" trae sus propios filtros, así que en esas dos el selector se retira en vez de
 * quedarse presidiendo una vista donde no cambia ni un número — la misma corrección que ya se hizo
 * en la consola del organismo.
 */
const RANGE_TABS: ReadonlyArray<TabId> = ["novedades", "jobs", "webhooks"];

/** La pestaña activa vive en la dirección, igual que en empresa (`reportesTab`) y OT (`tab`). */
const TAB_QUERY_PARAM = "ictReportesTab";

/** Los 4 tipos de informe programado que ofrece este módulo (Reportes 2.0, HU-D, cuarta ola). */
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

function initialTab(): string {
  if (typeof window === "undefined") return "";
  return new URLSearchParams(window.location.search).get(TAB_QUERY_PARAM) ?? "";
}

export function IctReports() {
  const { tenantId, isSuper } = useIctTenantId();
  const allowedReportTypes = useMemo(() => ictReportTypes(isSuper), [isSuper]);

  const visibleTabs = useMemo(() => TAB_DEFS.filter((t) => isSuper || !t.superOnly), [isSuper]);
  const [requestedTab, setRequestedTab] = useState<string>(() => initialTab());
  // Una pestaña que ya no existe —un enlace viejo, o "Jobs" abierto por quien dejó de ser
  // SuperAdmin— no puede dejar la consola en blanco: cae en la primera visible.
  const activeTab: TabId = visibleTabs.some((t) => t.id === requestedTab)
    ? (requestedTab as TabId)
    : visibleTabs[0]!.id;

  const selectTab = useCallback((id: string) => {
    setRequestedTab(id);
    try {
      const url = new URL(window.location.href);
      url.searchParams.set(TAB_QUERY_PARAM, id);
      // `replaceState` y no `pushState`: recorrer las cinco pestañas no debe costar cinco «atrás»
      // para salir del módulo. Se conserva el resto de la dirección — «Consultas» guarda la suya.
      window.history.replaceState(window.history.state, "", url);
    } catch {
      /* entorno sin history (tests/SSR): el estado local basta */
    }
  }, []);

  // El rango es del módulo, no de la pestaña: cambiar de Novedades a Webhooks conserva el periodo
  // que el usuario acaba de elegir, como los filtros globales de la consola de empresa.
  const [range, setRange] = useState<DateRange>(() => defaultRange());
  const rangeValid = isValidRange(range);
  const usesRange = RANGE_TABS.includes(activeTab);

  const [schedulingOpen, setSchedulingOpen] = useState(false);
  // "Programar este informe" sobre una consulta guardada de ICT: mismo mecanismo que
  // OtReportsConsole/Reportes.tsx, con savedQueryScope="ict" fijo.
  const [schedulePreset, setSchedulePreset] = useState<SchedulePresetConsulta | null>(null);

  return (
    <div className="app-bg min-h-screen px-6 pt-6 pb-10 flex flex-col gap-4 text-[#162744] dark:text-white">
      <ModuleTitle
        title="Reportes de Integración con Terceros"
        subtitle="Novedades, atascados y entregas de ICT, con consultas propias y envío programado."
      />

      <ReportesTabBar
        tabs={visibleTabs.map(({ id, label }) => ({ id, label }))}
        activeId={activeTab}
        onChange={selectTab}
        ariaLabel="Pestañas de reportes de ICT"
      />

      {/* Filtros debajo de las pestañas: primero se elige qué mirar, luego sobre qué periodo. */}
      <div className="flex flex-wrap items-end gap-3 shrink-0">
        {usesRange && <DateRangeFilter value={range} onChange={setRange} />}
        <button
          type="button"
          onClick={() => setSchedulingOpen(true)}
          className="inline-flex items-center gap-2 rounded-xl border px-3 py-2 text-sm font-medium hover:bg-[#F4F7FC] dark:hover:bg-white/5"
          data-testid="ict-reportes-abrir-programacion"
        >
          <CalendarClock className="h-4 w-4" aria-hidden="true" />
          Programación y alertas
        </button>
      </div>

      {usesRange && !rangeValid ? (
        <div
          role="alert"
          className="flex flex-col items-center justify-center gap-2 rounded-2xl border p-8 text-center bg-white dark:bg-[#0B0F14]"
        >
          <p className="text-sm font-medium">La fecha inicial no puede ser posterior a la fecha final.</p>
          <p className="text-xs opacity-70">Corrige el rango de fechas para volver a consultar el reporte.</p>
        </div>
      ) : (
        <div className="pr-1">
          {activeTab === "novedades" && <NovedadesTab range={range} tenantId={tenantId} />}
          {activeTab === "atascados" && <AtascadosTab tenantId={tenantId} />}
          {activeTab === "jobs" && <JobsTab range={range} />}
          {activeTab === "webhooks" && <WebhooksTab range={range} tenantId={tenantId} />}
          {activeTab === "consultas" && (
            <IctQueriesTab
              tenantId={tenantId}
              onScheduleQuery={(query) => {
                setSchedulePreset({
                  savedQueryId: query.id,
                  savedQueryScope: "ict",
                  queryName: query.nombre,
                });
                setSchedulingOpen(true);
              }}
            />
          )}
        </div>
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

/** Sección de contenido: misma tarjeta blanca con título que usan las pestañas de empresa. */
function ReportSection({
  title,
  hint,
  children,
  testId,
}: {
  title: string;
  hint: string;
  children: React.ReactNode;
  testId?: string;
}) {
  return (
    <section className="rounded-2xl p-5 bg-white dark:bg-[#0B0F14] border" data-testid={testId}>
      <h2 className="text-sm font-bold mb-3" title={hint}>
        {title}
      </h2>
      {children}
    </section>
  );
}

/** Tabla de detalle: misma receta "lista de tarjetas" que el resto de Reportes. */
function DetailTable({
  headers,
  rows,
}: {
  headers: string[];
  rows: { key: string; cells: string[] }[];
}) {
  return (
    <div className={CARDLIST_SCROLL}>
      <table className={CARDLIST_TABLE}>
        <thead>
          <tr className={CARDLIST_HEAD_ROW}>
            {headers.map((h) => (
              <th key={h} className={CARDLIST_TH}>
                {h}
              </th>
            ))}
          </tr>
        </thead>
        <tbody>
          {rows.map((row) => (
            <tr key={row.key} className={CARDLIST_ROW}>
              {row.cells.map((cell, i) => (
                <td key={headers[i]} className={CARDLIST_CELL}>
                  {cell}
                </td>
              ))}
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}

/** Aviso de corte del detalle: el backend limita las filas del reporte en vivo. */
function TruncatedNotice({ what }: { what: string }) {
  return (
    <p className="mt-3 text-[11px] opacity-70">
      El detalle se limitó a las primeras filas: hay más {what} de los que se muestran. Programa el
      informe para recibirlo completo en Excel.
    </p>
  );
}

const dateTimeFmt = new Intl.DateTimeFormat("es-CO", { dateStyle: "short", timeStyle: "short" });

function fmtDateTime(iso: string): string {
  const d = new Date(iso);
  return Number.isNaN(d.getTime()) ? "—" : dateTimeFmt.format(d);
}

function NovedadesTab({ range, tenantId }: { range: DateRange; tenantId?: string }) {
  const q = useAnalyticsQuery<IctNovedadesReport>(
    (signal) => fetchIctNovedadesReport(range, tenantId, signal),
    [range.from, range.to, tenantId],
    { isEmpty: (r) => r.total === 0 },
  );
  const report = q.data;

  return (
    <UiStateBoundary
      status={q.status}
      errorMessage={q.errorMessage}
      onRetry={q.retry}
      emptyMessage="Sin novedades en el periodo seleccionado."
      skeletonRows={3}
    >
      {report && (
        <div className="flex flex-col gap-4" data-testid="ict-novedades-tab">
          <div className="grid grid-cols-2 gap-3 lg:grid-cols-4">
            <KpiCard
              label="Novedades en el periodo"
              value={formatInt(report.total)}
              tooltip="Novedades registradas por el organismo sobre los pre-trámites enviados por ICT en el rango elegido."
            />
            {report.resumenPorCausa.slice(0, 3).map((c) => (
              <KpiCard
                key={c.causa}
                label={c.causa}
                value={`${formatInt(c.cantidad)} · ${c.porcentajeTexto}`}
                tooltip="Novedades de esta causa y su peso sobre el total del periodo."
              />
            ))}
          </div>

          <ReportSection
            title={`Detalle de novedades (${formatInt(report.detalle.length)})`}
            hint="Cada novedad registrada en el periodo, con el trámite al que corresponde."
          >
            <DetailTable
              headers={["Placa", "VIN", "Radicado", "Comentarios", "Registrado"]}
              rows={report.detalle.map((d, i) => ({
                key: `${d.radicado ?? d.placa ?? d.vin ?? ""}-${i}`,
                cells: [
                  d.placa ?? "—",
                  d.vin ?? "—",
                  d.radicado ?? "—",
                  d.comentarios ?? "—",
                  fmtDateTime(d.registradoEn),
                ],
              }))}
            />
            {report.truncated && <TruncatedNotice what="novedades" />}
          </ReportSection>
        </div>
      )}
    </UiStateBoundary>
  );
}

function AtascadosTab({ tenantId }: { tenantId?: string }) {
  const q = useAnalyticsQuery<IctAtascadosReport>(
    (signal) => fetchIctAtascadosReport(tenantId, signal),
    [tenantId],
    { isEmpty: (r) => r.total === 0 },
  );
  const report = q.data;

  return (
    <UiStateBoundary
      status={q.status}
      errorMessage={q.errorMessage}
      onRetry={q.retry}
      emptyMessage="No hay pre-trámites atascados en validación."
      skeletonRows={3}
    >
      {report && (
        <div className="flex flex-col gap-4" data-testid="ict-atascados-tab">
          <div className="grid grid-cols-2 gap-3 lg:grid-cols-4">
            <KpiCard
              label="Atascados ahora"
              value={formatInt(report.total)}
              tooltip="Pre-trámites detenidos en validación en este momento. Esta pestaña no usa rango de fechas: siempre muestra el estado actual."
            />
          </div>

          <ReportSection
            title={`Detalle de atascados (${formatInt(report.detalle.length)})`}
            hint="Qué está esperando cada pre-trámite detenido y desde hace cuánto."
          >
            <DetailTable
              headers={["Placa", "VIN", "Radicado", "Esperando", "Días detenido"]}
              rows={report.detalle.map((d, i) => ({
                key: `${d.radicado ?? d.placa ?? d.vin ?? ""}-${i}`,
                cells: [
                  d.placa ?? "—",
                  d.vin ?? "—",
                  d.radicado ?? "—",
                  d.esperando,
                  formatNumber(d.diasTranscurridos),
                ],
              }))}
            />
            {report.truncated && <TruncatedNotice what="atascados" />}
          </ReportSection>
        </div>
      )}
    </UiStateBoundary>
  );
}

function JobsTab({ range }: { range: DateRange }) {
  const q = useAnalyticsQuery<IctJobsReport>(
    (signal) => fetchIctJobsReport(range, signal),
    [range.from, range.to],
    { isEmpty: (r) => r.resumenPorJob.length === 0 },
  );
  const report = q.data;

  return (
    <UiStateBoundary
      status={q.status}
      errorMessage={q.errorMessage}
      onRetry={q.retry}
      emptyMessage="Sin corridas de jobs en el periodo seleccionado."
      skeletonRows={3}
    >
      {report && (
        <div className="flex flex-col gap-4" data-testid="ict-jobs-tab">
          <ReportSection
            title="Rendimiento por job"
            hint="Reporte de plataforma: cubre todas las compañías, no solo la suya."
          >
            <DetailTable
              headers={["Job", "Corridas", "Duración prom.", "Duración máx.", "% fuera de SLA"]}
              rows={report.resumenPorJob.map((r) => ({
                key: r.job,
                cells: [
                  r.job,
                  formatInt(r.corridas),
                  `${formatNumber(r.duracionPromedioSeg)} s`,
                  `${formatNumber(r.duracionMaximaSeg)} s`,
                  r.porcentajeFueraDeSlaTexto,
                ],
              }))}
            />
          </ReportSection>

          {report.corridasFueraDeSla.length > 0 && (
            <ReportSection
              title={`Corridas fuera de SLA (${formatInt(report.corridasFueraDeSla.length)})`}
              hint="Cada corrida que superó el tiempo esperado para su job."
            >
              <DetailTable
                headers={["Job", "Resultado", "Duración", "Inicio"]}
                rows={report.corridasFueraDeSla.map((c, i) => ({
                  key: `${c.job}-${c.inicio}-${i}`,
                  cells: [
                    c.job,
                    c.resultado,
                    `${formatNumber(c.duracionSeg)} s`,
                    fmtDateTime(c.inicio),
                  ],
                }))}
              />
              {report.truncated && <TruncatedNotice what="corridas" />}
            </ReportSection>
          )}
        </div>
      )}
    </UiStateBoundary>
  );
}

function WebhooksTab({ range, tenantId }: { range: DateRange; tenantId?: string }) {
  const q = useAnalyticsQuery<IctWebhooksReport>(
    (signal) => fetchIctWebhooksReport(range, tenantId, signal),
    [range.from, range.to, tenantId],
    { isEmpty: (r) => r.total === 0 },
  );
  const report = q.data;

  return (
    <UiStateBoundary
      status={q.status}
      errorMessage={q.errorMessage}
      onRetry={q.retry}
      emptyMessage="Sin webhooks en el periodo seleccionado."
      skeletonRows={3}
    >
      {report && (
        <div className="flex flex-col gap-4" data-testid="ict-webhooks-tab">
          <div className="grid grid-cols-2 gap-3 lg:grid-cols-4">
            <KpiCard
              label="Webhooks en el periodo"
              value={formatInt(report.total)}
              tooltip="Notificaciones que ICT intentó entregar al sistema del cliente en el rango elegido."
            />
          </div>

          <ReportSection
            title={`Detalle de entregas (${formatInt(report.detalle.length)})`}
            hint="En qué acabó cada entrega y cuántos intentos costó."
          >
            <DetailTable
              headers={["Radicado", "Estado", "Intentos", "URL destino", "Registrado"]}
              rows={report.detalle.map((w, i) => ({
                key: `${w.radicado}-${i}`,
                cells: [
                  w.radicado,
                  w.estado,
                  formatInt(w.intentos),
                  w.urlDestino ?? "—",
                  fmtDateTime(w.registradoEn),
                ],
              }))}
            />
            {report.truncated && <TruncatedNotice what="webhooks" />}
          </ReportSection>
        </div>
      )}
    </UiStateBoundary>
  );
}
