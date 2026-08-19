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
import { Pagination } from "@/components/atom/Pagination";
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
  ICT_EXCEL_MAX_ROWS,
  ICT_REPORT_PAGE_SIZE,
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
import { formatDurationMs, formatInt, formatNumber, formatPct } from "./_reportes/format";
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

/**
 * Color por estado de entrega de webhook, con los mismos verdes/naranjas que el resto de Reportes
 * usa para "bien"/"falló". Los tres estados los define el backend (`EstadoWebhook`).
 */
const ESTADO_WEBHOOK_COLOR: Record<string, string> = {
  Entregado: "#8CC63F",
  Fallido: "#FF4E00",
  Pendiente: "#F9AC00",
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

/**
 * Página del detalle, atada a los filtros vigentes.
 *
 * NO se persiste en la dirección, a diferencia de la pestaña (`ictReportesTab`) y la compañía
 * (`compania`): esas dos deciden QUÉ se está mirando y por eso un enlace copiado tiene que
 * conservarlas; la página es solo por dónde vas dentro de esa misma vista, y además significa una
 * cosa distinta en cada pestaña — persistirla obligaría a cuatro parámetros o a uno ambiguo.
 *
 * La página se deriva de `filtersKey` en vez de reponerse desde un efecto: al cambiar el rango o la
 * compañía, la clave deja de coincidir y la página vuelve a 1 en el mismo render, sin un paso
 * intermedio pidiendo la página 7 de un periodo que ya no es el elegido.
 */
function usePagedFilters(filtersKey: string): [number, (page: number) => void] {
  const [state, setState] = useState({ key: filtersKey, page: 1 });
  const page = state.key === filtersKey ? state.page : 1;
  const setPage = useCallback((next: number) => setState({ key: filtersKey, page: next }), [filtersKey]);
  return [page, setPage];
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

  /**
   * Universo de la pestaña, tal como lo reporta cada consulta. Solo existe para saber si hay algo
   * que exportar: "Exportar Excel" con 0 resultados descargaba un archivo con la cabecera y nada
   * más (se veía en Atascados vacío). `undefined` = todavía no se sabe, y ahí el botón no estorba.
   */
  const [tabTotals, setTabTotals] = useState<Partial<Record<TabId, number>>>({});
  const reportTotal = useCallback((tab: TabId, total: number) => {
    setTabTotals((prev) => (prev[tab] === total ? prev : { ...prev, [tab]: total }));
  }, []);

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
  const nothingToExport = tabTotals[activeTab] === 0;

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
            <ExcelExportButton onExport={exportCurrentTab} disabled={blocked || nothingToExport} />
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
          {activeTab === "novedades" && (
            <NovedadesTab range={range} tenantId={tenantId} onTotal={reportTotal} />
          )}
          {activeTab === "atascados" && <AtascadosTab tenantId={tenantId} onTotal={reportTotal} />}
          {activeTab === "jobs" && <JobsTab range={range} onTotal={reportTotal} />}
          {activeTab === "webhooks" && (
            <WebhooksTab range={range} tenantId={tenantId} onTotal={reportTotal} />
          )}
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

/**
 * Aviso de corte DEL EXCEL. Ya no habla de la pantalla: el detalle en vivo se pagina, así que
 * siempre se puede llegar a la última fila. Lo que sigue topándose es el archivo exportado, y eso
 * es lo que hay que advertir antes de que alguien lo dé por completo.
 */
function ExcelTruncatedNotice({ what }: { what: string }) {
  return (
    <p className="mt-3 text-[11px] opacity-70" data-testid="ict-aviso-excel-truncado">
      El Excel de este informe se corta en las primeras {formatInt(ICT_EXCEL_MAX_ROWS)} filas y hay
      más {what} en el periodo. En pantalla puedes recorrerlos todos con la paginación.
    </p>
  );
}

/** Barra de paginación del detalle, con el mismo paginador del resto del producto. */
function DetailPagination({
  page,
  total,
  onPageChange,
}: {
  page: number;
  total: number;
  onPageChange: (page: number) => void;
}) {
  return (
    <Pagination
      page={page}
      pageSize={ICT_REPORT_PAGE_SIZE}
      totalCount={total}
      onPageChange={onPageChange}
    />
  );
}

/**
 * Avisa al módulo del universo de la pestaña para que sepa si hay algo que exportar. Va en un
 * efecto y no en el render porque `onTotal` toca estado del padre; el propio `onTotal` corta la
 * re-notificación cuando el valor no cambió.
 */
function useReportTotal(tab: TabId, total: number | undefined, onTotal: (tab: TabId, total: number) => void) {
  useEffect(() => {
    if (total !== undefined) onTotal(tab, total);
  }, [tab, total, onTotal]);
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

/**
 * Valor de una tarjeta de estado de webhook: "4.810 · 92,3%". El reparto viene del periodo
 * completo y los tres estados suman el total, así que el porcentaje se saca directo; solo se
 * protege la división por cero de un periodo sin webhooks.
 */
function estadoValor(cantidad: number, total: number): string {
  return `${formatInt(cantidad)} · ${total === 0 ? "0%" : formatPct((cantidad / total) * 100)}`;
}

/** Duración en segundos → texto legible. Las corridas de ICT duran milisegundos: mostrarlas con un
 * decimal de segundo ("0 s") escondía el dato entero. */
function fmtSeconds(segundos: number): string {
  return formatDurationMs(segundos * 1000);
}

function NovedadesTab({
  range,
  tenantId,
  onTotal,
}: {
  range: DateRange;
  tenantId?: string;
  onTotal: (tab: TabId, total: number) => void;
}) {
  const [page, setPage] = usePagedFilters(`${range.from}|${range.to}|${tenantId ?? ""}`);
  const q = useAnalyticsQuery<IctNovedadesReport>(
    (signal) => fetchIctNovedadesReport(range, tenantId, { page, pageSize: ICT_REPORT_PAGE_SIZE }, signal),
    [range.from, range.to, tenantId, page],
    { isEmpty: (r) => r.total === 0 },
  );
  const report = q.data;
  useReportTotal("novedades", report?.total, onTotal);

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
          {/* Seis tarjetas (el total + las cinco causas) en filas de tres: antes se cortaban a las
              tres primeras y en dev eso enseñaba tres ceros mientras el 100% de las novedades
              estaba en "Otra/sin clasificar", sin nada en pantalla que dijera dónde estaba el
              resto. Con las cinco visibles, las causas suman exactamente el total. */}
          <div className="grid grid-cols-2 gap-3 lg:grid-cols-3">
            {/* La cifra que duele va en el naranja de "rechazado" del resto de Reportes: una
                novedad es trabajo que vuelve, no un indicador neutro. */}
            <KpiCard
              label="Novedades en el periodo"
              value={formatInt(report.total)}
              tooltip="Novedades registradas por el organismo sobre los pre-trámites enviados por ICT en el rango elegido. La variación compara con el periodo anterior de la misma longitud."
              variation={variationPct(report.total, report.totalPeriodoAnterior)}
              invertVariationColor
              color="#FF4E00"
            />
            {report.resumenPorCausa.map((c) => (
              <KpiCard
                key={c.causa}
                label={c.causa}
                value={`${formatInt(c.cantidad)} · ${withPct(c.porcentajeTexto)}`}
                tooltip="Novedades de esta causa y su peso sobre el total del periodo. Las causas cubren el 100% de las novedades: lo que no se puede clasificar cuenta en «Otra/sin clasificar»."
                color={CAUSA_COLOR[c.causa] ?? "#162744"}
              />
            ))}
          </div>

          <ReportSection
            title={`Detalle de novedades (${formatInt(report.total)})`}
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
            <DetailPagination page={report.page} total={report.total} onPageChange={setPage} />
            {report.truncated && <ExcelTruncatedNotice what="novedades" />}
          </ReportSection>
        </div>
      )}
    </UiStateBoundary>
  );
}

function AtascadosTab({
  tenantId,
  onTotal,
}: {
  tenantId?: string;
  onTotal: (tab: TabId, total: number) => void;
}) {
  const [page, setPage] = usePagedFilters(tenantId ?? "");
  const q = useAnalyticsQuery<IctAtascadosReport>(
    (signal) => fetchIctAtascadosReport(tenantId, { page, pageSize: ICT_REPORT_PAGE_SIZE }, signal),
    [tenantId, page],
    { isEmpty: (r) => r.total === 0 },
  );
  const report = q.data;
  useReportTotal("atascados", report?.total, onTotal);

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
              value={formatInt(report.total)}
              tooltip="Pre-trámites detenidos en validación en este momento. Esta pestaña no usa rango de fechas: siempre muestra el estado actual."
              color="#F9AC00"
            />
          </div>

          <ReportSection
            title={`Detalle de atascados (${formatInt(report.total)})`}
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
            <DetailPagination page={report.page} total={report.total} onPageChange={setPage} />
            {report.truncated && <ExcelTruncatedNotice what="atascados" />}
          </ReportSection>
        </div>
      )}
    </UiStateBoundary>
  );
}

function JobsTab({
  range,
  onTotal,
}: {
  range: DateRange;
  onTotal: (tab: TabId, total: number) => void;
}) {
  const [page, setPage] = usePagedFilters(`${range.from}|${range.to}`);
  const q = useAnalyticsQuery<IctJobsReport>(
    (signal) => fetchIctJobsReport(range, { page, pageSize: ICT_REPORT_PAGE_SIZE }, signal),
    [range.from, range.to, page],
    { isEmpty: (r) => r.resumenPorJob.length === 0 },
  );
  const report = q.data;
  useReportTotal("jobs", report?.total, onTotal);

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
              value={formatInt(report.total)}
              tooltip="Corridas de los jobs del pipeline de ICT en el rango elegido, en toda la plataforma. La variación compara con el periodo anterior de la misma longitud."
              variation={variationPct(report.total, report.totalPeriodoAnterior)}
              color="#557EFF"
            />
            {/* Cero incumplimientos es buena noticia y se pinta como tal; en cuanto hay uno, pasa
                al naranja. Un rojo permanente en una tarjeta que dice "0" enseña a ignorarla. */}
            {/* `totalFueraDeSla` y no `corridasFueraDeSla.length`: la lista es una página. */}
            <KpiCard
              label="Corridas fuera de SLA"
              value={formatInt(report.totalFueraDeSla)}
              tooltip="Corridas que superaron el tiempo esperado para su job, en todo el periodo."
              color={report.totalFueraDeSla > 0 ? "#FF4E00" : "#8CC63F"}
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
            {/* Sin aviso de truncamiento aquí: el resumen por job trae una fila por job, no se
                pagina ni cabe cortarlo. El aviso va abajo, con la lista que sí puede cortarse. */}
          </ReportSection>

          {report.totalFueraDeSla > 0 && (
            <ReportSection
              title={`Corridas fuera de SLA (${formatInt(report.totalFueraDeSla)})`}
              hint="Cada corrida que superó el tiempo esperado para su job."
            >
              <DetailTable
                headers={["Job", "Resultado", "Duración", "Inicio"]}
                rows={report.corridasFueraDeSla.map((c, i) => ({
                  key: `${c.job}-${c.inicio}-${i}`,
                  cells: [c.job, c.resultado, fmtSeconds(c.duracionSeg), fmtDateTime(c.inicio)],
                }))}
              />
              <DetailPagination
                page={report.page}
                total={report.totalFueraDeSla}
                onPageChange={setPage}
              />
              {report.truncated && <ExcelTruncatedNotice what="corridas fuera de SLA" />}
            </ReportSection>
          )}
        </div>
      )}
    </UiStateBoundary>
  );
}

function WebhooksTab({
  range,
  tenantId,
  onTotal,
}: {
  range: DateRange;
  tenantId?: string;
  onTotal: (tab: TabId, total: number) => void;
}) {
  const [page, setPage] = usePagedFilters(`${range.from}|${range.to}|${tenantId ?? ""}`);
  const q = useAnalyticsQuery<IctWebhooksReport>(
    (signal) => fetchIctWebhooksReport(range, tenantId, { page, pageSize: ICT_REPORT_PAGE_SIZE }, signal),
    [range.from, range.to, tenantId, page],
    { isEmpty: (r) => r.total === 0 },
  );
  const report = q.data;
  useReportTotal("webhooks", report?.total, onTotal);

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
          {/* El conteo total no dice lo único que importa de un webhook: si llegó. Los tres
              estados son del PERIODO COMPLETO (no de la página) y suman exactamente el total, así
              que la fila se lee entera: cuántas se intentaron y en qué acabaron. */}
          <div className="grid grid-cols-2 gap-3 lg:grid-cols-4">
            <KpiCard
              label="Webhooks en el periodo"
              value={formatInt(report.total)}
              tooltip="Notificaciones que ICT intentó entregar al sistema del cliente en el rango elegido. La variación compara con el periodo anterior de la misma longitud."
              variation={variationPct(report.total, report.totalPeriodoAnterior)}
              color="#00DBD5"
            />
            <KpiCard
              label="Entregados"
              value={estadoValor(report.totalEntregados, report.total)}
              tooltip="Entregas que el sistema del cliente confirmó en el periodo. Entregados + fallidos + pendientes suman el total."
              color={ESTADO_WEBHOOK_COLOR.Entregado}
            />
            <KpiCard
              label="Fallidos"
              value={estadoValor(report.totalFallidos, report.total)}
              tooltip="Entregas que se intentaron y el sistema del cliente rechazó o no respondió, en el periodo."
              color={ESTADO_WEBHOOK_COLOR.Fallido}
            />
            <KpiCard
              label="Pendientes"
              value={estadoValor(report.totalPendientes, report.total)}
              tooltip="Notificaciones que todavía no se han intentado entregar, en el periodo."
              color={ESTADO_WEBHOOK_COLOR.Pendiente}
            />
          </div>

          <ReportSection
            title={`Detalle de entregas (${formatInt(report.total)})`}
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
            <DetailPagination page={report.page} total={report.total} onPageChange={setPage} />
            {report.truncated && <ExcelTruncatedNotice what="webhooks" />}
          </ReportSection>
        </div>
      )}
    </UiStateBoundary>
  );
}
