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
// (`OtReportsConsole.tsx`): `ModuleTitle`, `ReportesTabBar`, el `DateRangeFilter` compartido,
// `CompanySelector` para SuperAdmin y `useAnalyticsQuery` + `UiStateBoundary` para los cuatro
// estados de carga. No es una preferencia estética: quien ya sabe leer los reportes de su empresa
// no debería tener que reaprender dónde está el rango de fechas al entrar a ICT.
import { useCallback, useEffect, useMemo, useState } from "react";
import { CalendarClock, FileSpreadsheet, Loader2 } from "lucide-react";
import { UiStateBoundary } from "@/components/admin/UiStateBoundary";
import {
  CARDLIST_CELL,
  CARDLIST_HEAD_ROW,
  CARDLIST_ROW,
  CARDLIST_SCROLL,
  CARDLIST_TABLE,
  CARDLIST_TH,
} from "@/components/atom/table-cardlist";
import { fetchCompaniesIndex } from "@/lib/api/admin-companies";
import { variationPct } from "@/lib/api/analytics-v2";
import type { ReportType } from "@/lib/api/analytics-scheduling";
import {
  exportIctAtascadosReport,
  exportIctJobsReport,
  exportIctNovedadesReport,
  exportIctWebhooksReport,
  fetchIctAtascadosReport,
  fetchIctJobsReport,
  fetchIctNovedadesReport,
  fetchIctWebhooksReport,
  type IctAtascadosReport,
  type IctJobsReport,
  type IctNovedadesReport,
  type IctWebhooksReport,
} from "@/lib/api/ict-reports";
import { ApiError, type CompanyListItem } from "@/lib/api/types";
import { decodeJwtPayload, isSuperAdmin, TOKEN_STORAGE_KEY } from "@/lib/auth/jwt";
import { ModuleTitle } from "./ModuleTitle";
import { CompanyNotice } from "./_reportes/CompanyNotice";
import { CompanySelector } from "./_reportes/CompanySelector";
import { DateRangeFilter } from "./_reportes/DateRangeFilter";
import { formatDurationMs, formatInt, formatNumber } from "./_reportes/format";
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
 * un 403 después de esperar la carga. Por lo mismo es la única que NO depende de la compañía
 * elegida (`platformWide`): cubre todas.
 */
const TAB_DEFS: ReadonlyArray<{
  id: TabId;
  label: string;
  superOnly?: boolean;
  platformWide?: boolean;
}> = [
  { id: "novedades", label: "Novedades" },
  { id: "atascados", label: "Atascados" },
  { id: "jobs", label: "Jobs", superOnly: true, platformWide: true },
  { id: "webhooks", label: "Webhooks" },
  { id: "consultas", label: "Consultas" },
];

/**
 * Pestañas gobernadas por el rango de fechas. "Atascados" mira el estado de este momento y
 * "Consultas" trae sus propios filtros, así que en esas dos el selector se retira en vez de
 * quedarse presidiendo una vista donde no cambia ni un número.
 */
const RANGE_TABS: ReadonlyArray<TabId> = ["novedades", "jobs", "webhooks"];

/**
 * Color de marca por causa de novedad, con la misma paleta del resto de Reportes
 * (`_reportes/categories.ts`). Se asigna por NOMBRE y no por posición: si mañana cambia el orden
 * del resumen, cada causa conserva su color en vez de que las tarjetas se intercambien el suyo.
 * El catch-all va en el azul oscuro neutro — no nombra un problema concreto, así que no se lleva
 * un color de marca.
 */
const CAUSA_COLOR: Record<string, string> = {
  SOAT: "#557EFF",
  RTM: "#00DBD5",
  RNMC: "#9B8AFB",
  "Documento faltante": "#F9AC00",
  "Otra/sin clasificar": "#162744",
};

/** La pestaña activa vive en la dirección, igual que en empresa (`reportesTab`) y OT (`tab`). */
const TAB_QUERY_PARAM = "ictReportesTab";
/** La compañía también, para que un enlace copiado llegue mirando lo mismo. */
const COMPANY_QUERY_PARAM = "compania";

/** Los 4 tipos de informe programado que ofrece este módulo (Reportes 2.0, HU-D, cuarta ola). */
function ictReportTypes(isSuper: boolean): ReportType[] {
  const base: ReportType[] = ["ict_novedades", "ict_atascados", "ict_webhooks"];
  return isSuper ? [...base, "ict_jobs"] : base;
}

/** `true` si el usuario del token es SuperAdmin (única lectura del JWT que hace el módulo). */
function useIsSuperAdmin(): boolean {
  return useMemo(() => {
    if (typeof window === "undefined") return false;
    return isSuperAdmin(decodeJwtPayload(window.localStorage.getItem(TOKEN_STORAGE_KEY)));
  }, []);
}

function initialParam(name: string): string {
  if (typeof window === "undefined") return "";
  return new URLSearchParams(window.location.search).get(name) ?? "";
}

/** Refleja un valor en la dirección sin recargar ni apilar historial. */
function syncUrl(name: string, value: string) {
  try {
    const url = new URL(window.location.href);
    if (value) url.searchParams.set(name, value);
    else url.searchParams.delete(name);
    // `replaceState` y no `pushState`: recorrer las cinco pestañas no debe costar cinco «atrás»
    // para salir del módulo. Se conserva el resto de la dirección — «Consultas» guarda la suya.
    window.history.replaceState(window.history.state, "", url);
  } catch {
    /* entorno sin history (tests/SSR): el estado local basta */
  }
}

export function IctReports() {
  const isSuper = useIsSuperAdmin();
  const allowedReportTypes = useMemo(() => ictReportTypes(isSuper), [isSuper]);

  const visibleTabs = useMemo(() => TAB_DEFS.filter((t) => isSuper || !t.superOnly), [isSuper]);
  const [requestedTab, setRequestedTab] = useState<string>(() => initialParam(TAB_QUERY_PARAM));
  // Una pestaña que ya no existe —un enlace viejo, o "Jobs" abierto por quien dejó de ser
  // SuperAdmin— no puede dejar la consola en blanco: cae en la primera visible.
  const activeDef = visibleTabs.find((t) => t.id === requestedTab) ?? visibleTabs[0]!;
  const activeTab = activeDef.id;

  const selectTab = useCallback((id: string) => {
    setRequestedTab(id);
    syncUrl(TAB_QUERY_PARAM, id);
  }, []);

  /**
   * Compañía sobre la que corren los reportes, las consultas y la programación.
   *
   * A un usuario normal el backend se la resuelve del token, así que aquí va vacía. A SuperAdmin
   * NO se le puede resolver: su propio `tenant_id` casi nunca es donde están los datos de ICT, y
   * antes el módulo lo usaba en silencio — el resultado era un SuperAdmin viendo tres pestañas
   * vacías sin ninguna pista de por qué. Ahora elige, igual que en los reportes de empresa.
   */
  const [company, setCompany] = useState<string>(() => initialParam(COMPANY_QUERY_PARAM));
  const selectCompany = useCallback((tenantId: string) => {
    setCompany(tenantId);
    syncUrl(COMPANY_QUERY_PARAM, tenantId);
  }, []);
  const tenantId = company || undefined;
  const needsCompany = isSuper && !company && !activeDef.platformWide;

  // Catálogo de compañías del selector — solo SuperAdmin. Un fallo aquí no bloquea el módulo: el
  // selector queda vacío, igual que en la consola de empresa.
  const [companies, setCompanies] = useState<CompanyListItem[]>([]);
  useEffect(() => {
    if (!isSuper) return;
    const controller = new AbortController();
    fetchCompaniesIndex({ pageSize: 100, estadoActivo: true }, controller.signal)
      .then((res) => {
        if (!controller.signal.aborted) setCompanies(res.data);
      })
      .catch(() => {
        /* silencioso */
      });
    return () => controller.abort();
  }, [isSuper]);

  // El rango es del módulo, no de la pestaña: cambiar de Novedades a Webhooks conserva el periodo
  // que el usuario acaba de elegir, como los filtros globales de la consola de empresa.
  const [range, setRange] = useState<DateRange>(() => defaultRange());
  const rangeValid = isValidRange(range);
  const usesRange = RANGE_TABS.includes(activeTab);

  const [schedulingOpen, setSchedulingOpen] = useState(false);
  // "Programar este informe" sobre una consulta guardada de ICT: mismo mecanismo que
  // OtReportsConsole/Reportes.tsx, con savedQueryScope="ict" fijo.
  const [schedulePreset, setSchedulePreset] = useState<SchedulePresetConsulta | null>(null);

  const exportCurrentTab = useCallback(() => {
    switch (activeTab) {
      case "novedades":
        return exportIctNovedadesReport(range, tenantId);
      case "atascados":
        return exportIctAtascadosReport(tenantId);
      case "jobs":
        return exportIctJobsReport(range);
      case "webhooks":
        return exportIctWebhooksReport(range, tenantId);
      default:
        return Promise.resolve();
    }
  }, [activeTab, range, tenantId]);

  const blocked = needsCompany || (usesRange && !rangeValid);

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
        {isSuper && !activeDef.platformWide && (
          <CompanySelector
            companies={companies}
            value={company}
            onChange={selectCompany}
            defaultLabel="Selecciona una compañía"
            id="ict-reportes-compania"
          />
        )}
        <button
          type="button"
          onClick={() => setSchedulingOpen(true)}
          className="inline-flex items-center gap-2 rounded-xl border px-3 py-2 text-sm font-medium hover:bg-[#F4F7FC] dark:hover:bg-white/5"
          data-testid="ict-reportes-abrir-programacion"
        >
          <CalendarClock className="h-4 w-4" aria-hidden="true" />
          Programación y alertas
        </button>
        {/* «Consultas» ya trae su propio "Exportar a Excel" dentro de la consola. */}
        {activeTab !== "consultas" && (
          <div className="ml-auto">
            <ExcelExportButton onExport={exportCurrentTab} disabled={blocked} />
          </div>
        )}
      </div>

      {needsCompany ? (
        <CompanyNotice message="Como SuperAdmin debes elegir una compañía en el filtro superior para ver sus reportes de ICT. La pestaña «Jobs» es la excepción: cubre toda la plataforma." />
      ) : usesRange && !rangeValid ? (
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

/**
 * Descarga del informe de la pestaña activa. Mismo aspecto y mismo manejo de fallos que el
 * `ExportButtons` de la consola de empresa; aquí solo hay Excel porque estos 4 informes no tienen
 * una versión PDF con sentido (son detalle fila a fila).
 */
function ExcelExportButton({
  onExport,
  disabled,
}: {
  onExport: () => Promise<void>;
  disabled?: boolean;
}) {
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function run() {
    setBusy(true);
    setError(null);
    try {
      await onExport();
    } catch (err) {
      setError(describeExportError(err));
    } finally {
      setBusy(false);
    }
  }

  return (
    <div className="flex flex-col gap-1">
      <button
        type="button"
        onClick={() => void run()}
        disabled={disabled || busy}
        aria-busy={busy}
        // Acción principal de la fila, así que va con el fondo de marca (el mismo de "Reintentar" y
        // del PDF ejecutivo de empresa) en vez del borde neutro: aquí no compite con un segundo
        // botón de exportación del que hubiera que distinguirla.
        className="flex items-center gap-1.5 text-xs font-semibold px-3 py-2 rounded-lg text-white disabled:opacity-50"
        style={{ background: "#557EFF" }}
        data-testid="ict-reportes-exportar-excel"
      >
        {busy ? (
          <Loader2 className="h-3.5 w-3.5 animate-spin" aria-hidden="true" />
        ) : (
          <FileSpreadsheet className="h-3.5 w-3.5" aria-hidden="true" />
        )}
        {busy ? "Exportando…" : "Exportar Excel"}
      </button>
      {error && (
        <p role="alert" aria-live="assertive" className="text-[11px]" style={{ color: "#FF4E00" }}>
          {error}
        </p>
      )}
    </div>
  );
}

function describeExportError(error: unknown): string {
  if (error instanceof ApiError) {
    if (error.status === 400) return "El rango de fechas no es válido para exportar.";
    if (error.status === 401) return "Tu sesión expiró. Vuelve a iniciar sesión.";
    if (error.status === 403) return "No tienes acceso a la exportación de esta compañía.";
  }
  return "No se pudo generar la exportación. Inténtalo de nuevo.";
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
      El detalle se limitó a las primeras filas: hay más {what} de los que se muestran. Exporta el
      informe o prográmalo para recibirlo completo.
    </p>
  );
}

const dateTimeFmt = new Intl.DateTimeFormat("es-CO", { dateStyle: "short", timeStyle: "short" });

function fmtDateTime(iso: string): string {
  const d = new Date(iso);
  return Number.isNaN(d.getTime()) ? "—" : dateTimeFmt.format(d);
}

/**
 * El backend manda los porcentajes ya formateados pero SIN el símbolo ("16,67"), porque nacieron
 * para una celda de Excel bajo una cabecera que ya decía "%". En pantalla, fuera de esa cabecera,
 * un "60" suelto no se lee como porcentaje.
 */
function withPct(texto: string): string {
  return `${texto}%`;
}

/** Duración en segundos → texto legible. Las corridas de ICT duran milisegundos: mostrarlas con un
 * decimal de segundo ("0 s") escondía el dato entero. */
function fmtSeconds(segundos: number): string {
  return formatDurationMs(segundos * 1000);
}

/**
 * Total de la tarjeta. El backend corta el detalle en <c>MaxRows</c> filas, así que en un periodo
 * grande el total es un piso, no una cifra exacta: se marca con "+" para no presentar como definitivo
 * un número que en realidad dice "al menos esto".
 */
function fmtTotal(total: number, truncated: boolean): string {
  return truncated ? `${formatInt(total)}+` : formatInt(total);
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
            {/* La cifra que duele va en el naranja de "rechazado" del resto de Reportes: una
                novedad es trabajo que vuelve, no un indicador neutro. */}
            <KpiCard
              label="Novedades en el periodo"
              value={fmtTotal(report.total, report.truncated)}
              tooltip="Novedades registradas por el organismo sobre los pre-trámites enviados por ICT en el rango elegido. La variación compara con el periodo anterior de la misma longitud."
              variation={variationPct(report.total, report.totalPeriodoAnterior)}
              invertVariationColor
              color="#FF4E00"
            />
            {report.resumenPorCausa.slice(0, 3).map((c) => (
              <KpiCard
                key={c.causa}
                label={c.causa}
                value={`${formatInt(c.cantidad)} · ${withPct(c.porcentajeTexto)}`}
                tooltip="Novedades de esta causa y su peso sobre el total del periodo."
                color={CAUSA_COLOR[c.causa] ?? "#162744"}
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
            {/* Sin variación: esta pestaña es una foto del momento, no de un periodo, así que no
                hay un "antes" con el que compararla. */}
            {/* Ámbar de "esperando": un atascado no es un fallo, es algo detenido. */}
            <KpiCard
              label="Atascados ahora"
              value={fmtTotal(report.total, report.truncated)}
              tooltip="Pre-trámites detenidos en validación en este momento. Esta pestaña no usa rango de fechas: siempre muestra el estado actual."
              color="#F9AC00"
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
          <div className="grid grid-cols-2 gap-3 lg:grid-cols-4">
            <KpiCard
              label="Corridas en el periodo"
              value={fmtTotal(report.total, report.truncated)}
              tooltip="Corridas de los jobs del pipeline de ICT en el rango elegido, en toda la plataforma. La variación compara con el periodo anterior de la misma longitud."
              variation={variationPct(report.total, report.totalPeriodoAnterior)}
              color="#557EFF"
            />
            {/* Cero incumplimientos es buena noticia y se pinta como tal; en cuanto hay uno, pasa
                al naranja. Un rojo permanente en una tarjeta que dice "0" enseña a ignorarla. */}
            <KpiCard
              label="Corridas fuera de SLA"
              value={formatInt(report.corridasFueraDeSla.length)}
              tooltip="Corridas que superaron el tiempo esperado para su job."
              color={report.corridasFueraDeSla.length > 0 ? "#FF4E00" : "#8CC63F"}
            />
          </div>

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
                  fmtSeconds(r.duracionPromedioSeg),
                  fmtSeconds(r.duracionMaximaSeg),
                  withPct(r.porcentajeFueraDeSlaTexto),
                ],
              }))}
            />
            {report.truncated && <TruncatedNotice what="corridas" />}
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
                  cells: [c.job, c.resultado, fmtSeconds(c.duracionSeg), fmtDateTime(c.inicio)],
                }))}
              />
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
              value={fmtTotal(report.total, report.truncated)}
              tooltip="Notificaciones que ICT intentó entregar al sistema del cliente en el rango elegido. La variación compara con el periodo anterior de la misma longitud."
              variation={variationPct(report.total, report.totalPeriodoAnterior)}
              color="#00DBD5"
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
