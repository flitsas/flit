"use client";

import { useCallback, useEffect, useMemo, useState } from "react";
import { TarjetaMetrica, METRICAS_GRID_CLS } from "@flit/ui/TarjetaMetrica";
import { usePaginacion } from "@flit/ui/usePaginacion";
import { DataTable, type DataTableColumn } from "@flit/ui/DataTable";
import { StatusBadge } from "@flit/ui/StatusBadge";
import { BarraProgreso } from "@flit/ui/BarraProgreso";
import { UiStateBoundary, type UiStatus } from "@/components/admin/UiStateBoundary";
import { CarruselInicio } from "@/components/inicio/CarruselInicio";
import { PanelInicio } from "@/components/inicio/PanelInicio";
import { BarrasApiladas } from "@/components/inicio/BarrasApiladas";
import { GraficoArea } from "@/components/inicio/GraficoArea";
import type { SerieGrafica } from "@/components/inicio/graficos-kit";
import { FILTRO_CONTROL_CLS, FILTROS_REJILLA_CLS } from "@/components/inicio/filtros-inicio";
import { RangoFechas } from "@/components/atom/RangoFechas";
import { CompanySelector } from "./_reportes/CompanySelector";
import { isValidOptionalRange, sinRango, type DateRange } from "./_reportes/range";
import { useActiveBanners } from "@/hooks/useActiveBanners";
import { useFamilyAccess } from "@/hooks/useFamilyAccess";
import { fetchAnalyticsOverview, fetchMonthlyTrend } from "@/lib/api/analytics";
import { fetchAllCompanies } from "@/lib/api/admin-companies";
import {
  fetchSuperAdminCompanias,
  fetchSuperAdminOrganismos,
  type DistribucionFamilia,
  type SuperAdminCompania,
  type SuperAdminCompaniasResponse,
  type SuperAdminOrganismosResponse,
} from "@/lib/api/analytics-superadmin";
import { ALL_TENANTS, tramitesClient } from "@/lib/api/tramites-client";
import { getToken } from "@/lib/api/client";
import { decodeJwtPayload } from "@/lib/auth/jwt";
import { estadoAcentoVar } from "@/lib/tramites/estados";
import { ApiError } from "@/lib/api/types";
import type { AnalyticsOverviewResponse, CompanyListItem, MonthlyTrendPoint } from "@/lib/api/types";

/**
 * Inicio del SuperAdmin (rediseño de los 3 dashboards, 2026-11-17). Contenido y orden del prototipo (`SuperAdminView`),
 * con las piezas del kit. Sin SLA ni meta: los tiempos van en DÍAS y no se colorean.
 *
 * Fuentes:
 * - «Trámites totales», «Distribución de operaciones» (sin organismo) y el saludo: `/analytics/overview`.
 * - «Tiempo promedio ecosistema», las tarjetas por organismo y la distribución de UN organismo:
 *   `/analytics/superadmin/organismos` (lo de `/analytics/ot-metrics` no sirve aquí: no tiene vista global y exige rango).
 * - «Validaciones y firmas»: el total de validaciones biométricas del rango (vista global con `ALL_TENANTS`).
 * - «Resumen de compañías activas»: `/analytics/superadmin/companias-resumen`.
 *
 * Reparto de estados en todo el Inicio: Completados = aprobado; Rechazados = rechazado; En proceso = cualquier otro
 * estado no final. Anulado y revocado cuentan en el total, en ninguna de las tres.
 */

export const SUPERADMIN_ANTETITULO = "Visión del ecosistema";
const TODAS = "Todas las compañías";
const DIAS_REPRESADO = 7;
/** Filas por página de «Resumen de compañías activas». */

const ESTADOS_SIN_PROCESO = new Set(["aprobado", "rechazado", "anulado", "revocado"]);

/** Familias del overview y su código de «Ver por familia» (HU #13665). */
const FAMILIAS = [
  { categoria: "matriculas", etiqueta: "Matrículas", familia: "MATRICULAS" },
  { categoria: "traspasos", etiqueta: "Traspasos", familia: "TRASPASO" },
  { categoria: "otros", etiqueta: "Otros trámites", familia: "OTROS" },
] as const;

const SERIES_OPERACIONES: readonly SerieGrafica[] = [
  { clave: "completados", etiqueta: "Completados", color: estadoAcentoVar("aprobado") },
  { clave: "enProceso", etiqueta: "En proceso", color: "var(--color-flit-brand)" },
  { clave: "rechazados", etiqueta: "Rechazados", color: estadoAcentoVar("rechazado") },
];

const MESES = ["Ene", "Feb", "Mar", "Abr", "May", "Jun", "Jul", "Ago", "Sep", "Oct", "Nov", "Dic"];

const numero = (n: number) => n.toLocaleString("es-CO");

/** «1 día», «2,5 días». */
export function formatoDias(dias: number): string {
  const texto = dias.toLocaleString("es-CO", { maximumFractionDigits: 1 });
  return `${texto} ${dias === 1 ? "día" : "días"}`;
}

const porcentaje = (pct: number) => `${pct.toLocaleString("es-CO", { maximumFractionDigits: 1 })} %`;

function describirError(error: unknown, que: string): string {
  if (error instanceof ApiError) {
    if (error.status === 401) return "Tu sesión expiró. Vuelve a iniciar sesión.";
    if (error.status === 403) return `No tienes acceso a ${que}.`;
  }
  return `No se pudo cargar ${que}.`;
}

function esAbortado(error: unknown, signal: AbortSignal): boolean {
  return signal.aborted || (error as Error)?.name === "AbortError";
}

/** Últimos 6 meses (para la tendencia de la compañía elegida). */
function ultimosSeisMeses(): { from: string; to: string } {
  const hoy = new Date();
  const iso = (d: Date) =>
    `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, "0")}-${String(d.getDate()).padStart(2, "0")}`;
  return {
    from: iso(new Date(hoy.getFullYear(), hoy.getMonth() - 5, 1)),
    to: iso(new Date(hoy.getFullYear(), hoy.getMonth() + 1, 0)),
  };
}

/** Distribución por familia a partir del `byStatus` del overview. */
export function distribucionDesdeOverview(overview: AnalyticsOverviewResponse | null): DistribucionFamilia[] {
  return (overview?.categories ?? []).map((c) => {
    let completados = 0;
    let rechazados = 0;
    let enProceso = 0;
    for (const s of c.byStatus) {
      if (s.status === "aprobado") completados += s.count;
      else if (s.status === "rechazado") rechazados += s.count;
      else if (!ESTADOS_SIN_PROCESO.has(s.status)) enProceso += s.count;
    }
    return { categoria: c.category, completados, enProceso, rechazados };
  });
}

/** Lectura de una carga con su propio estado: cada franja falla o carga sin arrastrar a las demás. */
interface Carga<T> {
  status: UiStatus;
  data: T | null;
  error?: string;
}

const CARGANDO = { status: "loading" as const, data: null };

export function SuperAdminDashboard({ onNewTramite: _onNewTramite }: { onNewTramite: () => void }) {
  void _onNewTramite;
  const [displayName, setDisplayName] = useState("");
  const [companies, setCompanies] = useState<CompanyListItem[]>([]);
  const [tenantId, setTenantId] = useState("");
  /** BUG #12588 — arranca SIN rango: el total es el universo real. */
  const [range, setRange] = useState<DateRange>(() => sinRango());
  const [organismoId, setOrganismoId] = useState<string | null>(null);
  // HU #13920 — paginación estándar: 10/20/30/50 con «Filas por página», recordado en la sesión.
  const pgCompanias = usePaginacion("inicio.SuperAdminDashboard.companias");
  const [reloadKey, setReloadKey] = useState(0);

  const [overview, setOverview] = useState<Carga<AnalyticsOverviewResponse>>(CARGANDO);
  const [organismos, setOrganismos] = useState<Carga<SuperAdminOrganismosResponse>>(CARGANDO);
  const [companias, setCompanias] = useState<Carga<SuperAdminCompaniasResponse>>(CARGANDO);
  const [validaciones, setValidaciones] = useState<Carga<number>>(CARGANDO);
  const [tendencia, setTendencia] = useState<Carga<MonthlyTrendPoint[]>>(CARGANDO);

  const banners = useActiveBanners();
  const { showsInAggregates } = useFamilyAccess();
  const retry = useCallback(() => setReloadKey((k) => k + 1), []);

  useEffect(() => {
    const payload = decodeJwtPayload(getToken());
    // eslint-disable-next-line react-hooks/set-state-in-effect
    setDisplayName((payload?.display_name as string | undefined) ?? (payload?.email as string | undefined) ?? "");
  }, []);

  // Catálogo COMPLETO de compañías para el selector (paginado internamente; el buscador filtra en cliente).
  useEffect(() => {
    const controller = new AbortController();
    fetchAllCompanies({ estadoActivo: true }, controller.signal)
      .then((data) => {
        if (!controller.signal.aborted) setCompanies(data);
      })
      .catch(() => { /* silencioso: el selector queda vacío y el Inicio sigue operativo */ });
    return () => controller.abort();
  }, []);

  const rangoValido = isValidOptionalRange(range);
  const rangoError = "La fecha inicial no puede ser posterior a la fecha final.";

  // Overview: KPI de totales, % de aprobación, saludo y distribución sin organismo.
  useEffect(() => {
    const controller = new AbortController();
    if (!rangoValido) {
      // eslint-disable-next-line react-hooks/set-state-in-effect
      setOverview({ status: "error", data: null, error: rangoError });
      return () => controller.abort();
    }
    setOverview(CARGANDO);
    fetchAnalyticsOverview({ from: range.from, to: range.to, tenantId: tenantId || undefined }, controller.signal)
      .then((data) => setOverview({ status: "ready", data }))
      .catch((err) => {
        if (!esAbortado(err, controller.signal))
          setOverview({ status: "error", data: null, error: describirError(err, "las cifras de trámites") });
      });
    return () => controller.abort();
  }, [range, tenantId, reloadKey, rangoValido]);

  // Organismos: tarjetas, distribución de un organismo y tiempo del ecosistema.
  useEffect(() => {
    const controller = new AbortController();
    if (!rangoValido) {
      // eslint-disable-next-line react-hooks/set-state-in-effect
      setOrganismos({ status: "error", data: null, error: rangoError });
      return () => controller.abort();
    }
    setOrganismos(CARGANDO);
    fetchSuperAdminOrganismos({ from: range.from, to: range.to, tenantId: tenantId || undefined }, controller.signal)
      .then((data) => setOrganismos({ status: "ready", data }))
      .catch((err) => {
        if (!esAbortado(err, controller.signal))
          setOrganismos({ status: "error", data: null, error: describirError(err, "el rendimiento por organismo") });
      });
    return () => controller.abort();
  }, [range, tenantId, reloadKey, rangoValido]);

  // Compañías: no depende de la compañía elegida (la tabla las lista todas; la elegida se busca en ella).
  useEffect(() => {
    const controller = new AbortController();
    if (!rangoValido) {
      // eslint-disable-next-line react-hooks/set-state-in-effect
      setCompanias({ status: "error", data: null, error: rangoError });
      return () => controller.abort();
    }
    setCompanias(CARGANDO);
    fetchSuperAdminCompanias({ from: range.from, to: range.to, stuckDays: DIAS_REPRESADO }, controller.signal)
      .then((data) => setCompanias({ status: "ready", data }))
      .catch((err) => {
        if (!esAbortado(err, controller.signal))
          setCompanias({ status: "error", data: null, error: describirError(err, "el resumen de compañías") });
      });
    return () => controller.abort();
  }, [range, reloadKey, rangoValido]);

  // Validaciones y firmas: total de validaciones biométricas del rango. En «Todas las compañías» se pide la vista
  // global explícita (ALL_TENANTS), no el tenant del JWT del SuperAdmin (HU #12706 AC4).
  useEffect(() => {
    const controller = new AbortController();
    if (!rangoValido) {
      // eslint-disable-next-line react-hooks/set-state-in-effect
      setValidaciones({ status: "error", data: null, error: rangoError });
      return () => controller.abort();
    }
    setValidaciones(CARGANDO);
    const createdFrom = range.from ? `${range.from}T00:00:00` : undefined;
    const createdTo = range.to ? `${range.to}T23:59:59` : undefined;
    tramitesClient
      .listTenantBiometricValidations({ createdFrom, createdTo, pageSize: 1 }, tenantId || ALL_TENANTS)
      .then((res) => {
        if (!controller.signal.aborted) setValidaciones({ status: "ready", data: res.stats.total });
      })
      .catch((err) => {
        if (!esAbortado(err, controller.signal))
          setValidaciones({ status: "error", data: null, error: describirError(err, "las validaciones") });
      });
    return () => controller.abort();
  }, [range, tenantId, reloadKey, rangoValido]);

  // Tendencia de 6 meses de la compañía elegida (mini tarjeta «Trámites activos»). Sin compañía no se pide.
  useEffect(() => {
    const controller = new AbortController();
    if (!tenantId) return () => controller.abort();
    // eslint-disable-next-line react-hooks/set-state-in-effect
    setTendencia(CARGANDO);
    fetchMonthlyTrend({ ...ultimosSeisMeses(), tenantId }, controller.signal)
      .then((res) => setTendencia({ status: "ready", data: res.items }))
      .catch((err) => {
        if (!esAbortado(err, controller.signal))
          setTendencia({ status: "error", data: null, error: describirError(err, "la tendencia") });
      });
    return () => controller.abort();
  }, [tenantId, reloadKey]);

  // ── Derivados ──────────────────────────────────────────────────────────────────────────────────

  const familiasVisibles = useMemo(() => FAMILIAS.filter((f) => showsInAggregates(f.familia)), [showsInAggregates]);
  const categoriasVisibles = useMemo(() => new Set<string>(familiasVisibles.map((f) => f.categoria)), [familiasVisibles]);

  const companiaElegida = companies.find((c) => c.id === tenantId);
  const etiquetaCompania = tenantId ? companiaElegida?.razonSocial ?? "Compañía elegida" : TODAS;

  const listaOrganismos = useMemo(() => organismos.data?.items ?? [], [organismos.data]);
  const organismoActivo = listaOrganismos.find((o) => o.transitOfficeId === organismoId) ?? null;
  const maxVolumen = Math.max(1, ...listaOrganismos.map((o) => o.volumen));

  const filasOperaciones = useMemo(() => {
    const fuente = organismoActivo ? organismoActivo.distribucion : distribucionDesdeOverview(overview.data);
    return familiasVisibles.map((f) => {
      const d = fuente.find((x) => x.categoria === f.categoria);
      return {
        etiqueta: f.etiqueta,
        completados: d?.completados ?? 0,
        enProceso: d?.enProceso ?? 0,
        rechazados: d?.rechazados ?? 0,
      };
    });
  }, [organismoActivo, overview.data, familiasVisibles]);
  const operacionesConDatos = filasOperaciones.some((f) => f.completados + f.enProceso + f.rechazados > 0);
  const fuenteOperaciones = organismoActivo ? organismos : overview;
  const operacionesStatus: UiStatus =
    fuenteOperaciones.status === "ready" ? (operacionesConDatos ? "ready" : "empty") : fuenteOperaciones.status;

  const todasCompanias = useMemo(() => companias.data?.items ?? [], [companias.data]);
  const resumenElegida = todasCompanias.find((c) => c.tenantId === tenantId) ?? null;
  // La plataforma puede tener cientos de compañías activas: la tabla lista las que tienen trámites en el periodo
  // (el servidor ya las ordena por activos) y las demás se cuentan en una línea.
  const conTramites = useMemo(() => todasCompanias.filter((c) => c.total > 0), [todasCompanias]);
  const sinTramites = todasCompanias.length - conTramites.length;
  const paginasCompanias = Math.max(1, Math.ceil(conTramites.length / pgCompanias.pageSize));
  const pagina = Math.min(pgCompanias.page, paginasCompanias);
  const filasCompanias = conTramites.slice((pagina - 1) * pgCompanias.pageSize, pagina * pgCompanias.pageSize);

  // «Trámites totales», su % de aprobación y el saludo salen de companias-resumen y NO del overview global: el overview
  // sin compañía cuenta también los tenants con perfil de organismo de tránsito (p. ej. Empresa Demo FLIT), que la tabla
  // excluye igual que las Consultas de SuperAdmin. Es la misma incoherencia que se ve hoy en Reportes (Resumen general
  // ≠ Consultas); aquí el KPI cuadra con la suma de la tabla. Con una compañía elegida se usa su fila.
  const totales = useMemo(() => {
    const filas = tenantId ? (resumenElegida ? [resumenElegida] : []) : todasCompanias;
    if (tenantId && !resumenElegida) return null;
    let total = 0;
    let aprobados = 0;
    let rechazados = 0;
    for (const c of filas) {
      total += c.total;
      aprobados += c.aprobados;
      rechazados += c.rechazados;
    }
    return { total, aprobacion: aprobados + rechazados === 0 ? null : (aprobados * 100) / (aprobados + rechazados) };
  }, [tenantId, resumenElegida, todasCompanias]);
  const totalesListos = companias.status === "ready" && totales !== null;
  const totalTramites = totales?.total ?? 0;
  const aprobacionGlobal = totales?.aprobacion ?? null;

  const puntosTendencia = useMemo(() => {
    const porMes = new Map<string, { etiqueta: string; valor: number }>();
    for (const p of tendencia.data ?? []) {
      if (!categoriasVisibles.has(p.category)) continue;
      const clave = `${p.year}-${String(p.month).padStart(2, "0")}`;
      const actual = porMes.get(clave) ?? { etiqueta: MESES[p.month - 1], valor: 0 };
      actual.valor += p.total;
      porMes.set(clave, actual);
    }
    return [...porMes.entries()].sort(([a], [b]) => a.localeCompare(b)).map(([, v]) => v);
  }, [tendencia.data, categoriasVisibles]);

  const cuerpoBienvenida =
    totalesListos ? (
      <>
        {etiquetaCompania} · <strong>{numero(totalTramites)} {totalTramites === 1 ? "trámite" : "trámites"}</strong> en el
        periodo seleccionado.
      </>
    ) : (
      `${etiquetaCompania} · calculando los trámites del periodo seleccionado.`
    );
  const bienvenida = [
    { antetitulo: SUPERADMIN_ANTETITULO, titulo: displayName ? `Hola, ${displayName}` : "Hola", cuerpo: cuerpoBienvenida },
  ];

  const elegirCompania = useCallback((id: string) => {
    setTenantId(id);
    setOrganismoId(null);
  }, []);
  const cambiarRango = useCallback((siguiente: DateRange) => {
    setRange(siguiente);
    pgCompanias.setPage(1);
  }, [pgCompanias]);

  const columnasCompanias: DataTableColumn<SuperAdminCompania>[] = [
    {
      key: "compania",
      header: "Compañía",
      render: (c) => (
        // Botón y no fila con tabIndex: Enter y Espacio funcionan de forma nativa y el lector lo anuncia como acción.
        <button
          type="button"
          onClick={(e) => {
            e.stopPropagation();
            elegirCompania(c.tenantId);
          }}
          aria-label={`Ver resumen de ${c.nombre}`}
          className="min-w-0 text-left font-semibold text-[color:var(--di-text-primary)] underline-offset-2 hover:underline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-[var(--kit-azul-texto)]"
        >
          {c.nombre}
        </button>
      ),
    },
    { key: "activos", header: "Trámites activos", align: "right", render: (c) => <span className="tabular-nums">{numero(c.activos)}</span> },
    {
      key: "tasa",
      header: "Tasa aprobación",
      align: "right",
      render: (c) =>
        c.tasaAprobacionPct === null ? (
          <span className="text-xs text-[color:var(--di-text-tertiary)]">Sin decididos</span>
        ) : (
          <StatusBadge tone="success" label={porcentaje(c.tasaAprobacionPct)} />
        ),
    },
    {
      key: "represados",
      header: "Alertas / Represados",
      align: "right",
      render: (c) => (
        <StatusBadge
          tone={c.represados > 0 ? "warning" : "neutral"}
          label={numero(c.represados)}
          title={`En proceso sin moverse en más de ${DIAS_REPRESADO} días`}
        />
      ),
    },
  ];

  const tiempoEcosistema = organismos.data?.tiempoPromedioDias ?? null;

  return (
    <div className="app-bg min-h-screen px-6 pt-6 pb-10 max-sm:px-4 flex flex-col gap-4 text-foreground" data-testid="superadmin-dashboard">
      {/* Franja 1 — carrusel y, a la derecha, filtros + 3 KPIs. */}
      <div className="grid grid-cols-1 lg:grid-cols-2 gap-3 items-stretch">
        <CarruselInicio bienvenida={bienvenida} banners={banners} aria-label="Novedades de la plataforma" />

        <div className="flex min-w-0 flex-col gap-3" data-testid="superadmin-hero-column">
          <div className={FILTROS_REJILLA_CLS} data-testid="superadmin-filter-row">
            <RangoFechas value={range} onChange={cambiarRango} permiteSinRango idPrefix="superadmin-rango" />
            <CompanySelector
              companies={companies}
              value={tenantId}
              onChange={elegirCompania}
              defaultLabel={TODAS}
              id="superadmin-compania"
              className={`col-start-2 row-start-1 min-w-0 ${FILTRO_CONTROL_CLS}`}
            />
          </div>

          <div className="grid grid-cols-1 sm:grid-cols-3 items-stretch gap-3" data-testid="superadmin-kpis">
            <TarjetaMetrica
              icono="tramites"
              etiqueta="Trámites totales"
              cifra={totalesListos ? numero(totalTramites) : companias.status === "loading" ? "…" : "Sin dato"}
              title={
                companias.status === "error"
                  ? companias.error
                  : companias.status === "ready" && !totales
                    ? "Esta compañía no es una compañía cliente activa"
                    : undefined
              }
              pie={
                !totalesListos
                  ? undefined
                  : aprobacionGlobal === null
                    ? "Sin trámites decididos"
                    : `${porcentaje(aprobacionGlobal)} aprobación`
              }
              data-testid="kpi-tramites-totales"
            />
            <TarjetaMetrica
              icono="tiempo"
              etiqueta="Tiempo promedio ecosistema"
              cifra={
                organismos.status === "ready"
                  ? tiempoEcosistema === null
                    ? "Sin decisiones"
                    : formatoDias(tiempoEcosistema)
                  : organismos.status === "error"
                    ? "Sin dato"
                    : "…"
              }
              title={organismos.status === "error" ? organismos.error : undefined}
              pie="De radicación a resolución"
              data-testid="kpi-tiempo-ecosistema"
            />
            <TarjetaMetrica
              icono="firmas"
              etiqueta="Validaciones y firmas"
              cifra={
                validaciones.status === "ready" && validaciones.data !== null
                  ? numero(validaciones.data)
                  : validaciones.status === "error"
                    ? "Sin dato"
                    : "…"
              }
              title={validaciones.status === "error" ? validaciones.error : undefined}
              pie="Biometría y firma"
              data-testid="kpi-validaciones"
            />
          </div>
        </div>
      </div>

      {/* Franja 2 — rendimiento por organismo; pulsar uno filtra «Distribución de operaciones». */}
      <PanelInicio
        titulo="Rendimiento por Organismo de Tránsito"
        icono="banco"
        subtitulo={`${etiquetaCompania} · volumen y tiempo promedio de respuesta en días. Pulsa un organismo para filtrar la distribución.`}
        data-testid="panel-organismos"
      >
        <UiStateBoundary
          status={organismos.status === "ready" ? (listaOrganismos.length > 0 ? "ready" : "empty") : organismos.status}
          errorMessage={organismos.error}
          onRetry={retry}
          emptyMessage="Ningún organismo de tránsito recibió trámites en el periodo seleccionado."
          skeletonRows={2}
        >
          <div className={METRICAS_GRID_CLS} data-testid="organismos-grid">
            {listaOrganismos.map((o) => (
              <TarjetaMetrica
                key={o.transitOfficeId}
                etiqueta={o.nombre}
                cifra={numero(o.volumen)}
                activa={organismoId === o.transitOfficeId}
                onAlternar={(siguiente) => setOrganismoId(siguiente ? o.transitOfficeId : null)}
                aria-label={`${o.nombre}: ${numero(o.volumen)} trámites, ${
                  o.tiempoPromedioDias === null ? "sin decisiones" : `${formatoDias(o.tiempoPromedioDias)} de promedio`
                }. Filtrar la distribución por este organismo`}
                // El pie no es un enlace: sin subrayado ni color de acción.
                className="[&>span:last-child]:no-underline [&>span:last-child]:text-[color:var(--di-text-secondary)]"
                pie={
                  <span className="flex flex-col gap-1.5">
                    <span aria-hidden="true" className="block h-2 w-full overflow-hidden rounded-full bg-[var(--kit-fondo)]">
                      <span
                        className="block h-full rounded-full bg-[var(--color-flit-brand)]"
                        style={{ width: `${Math.round((o.volumen / maxVolumen) * 100)}%` }}
                      />
                    </span>
                    <span>
                      {o.tiempoPromedioDias === null
                        ? "Sin decisiones en el periodo"
                        : `${formatoDias(o.tiempoPromedioDias)} de promedio`}
                    </span>
                  </span>
                }
                data-testid={`organismo-${o.transitOfficeId}`}
              />
            ))}
          </div>
        </UiStateBoundary>
      </PanelInicio>

      {/* Franja 3 — distribución de operaciones y resumen de compañías. */}
      <div className="grid grid-cols-1 lg:grid-cols-2 gap-3">
        <PanelInicio
          titulo="Distribución de operaciones"
          icono="reportes"
          subtitulo={organismoActivo ? organismoActivo.nombre : "Todos los organismos"}
          accion={
            organismoActivo ? (
              <button
                type="button"
                onClick={() => setOrganismoId(null)}
                className="text-sm font-medium text-[color:var(--di-action)] underline underline-offset-2"
              >
                Ver todos los organismos
              </button>
            ) : undefined
          }
          data-testid="panel-operaciones"
        >
          <UiStateBoundary
            status={operacionesStatus}
            errorMessage={fuenteOperaciones.error}
            onRetry={retry}
            emptyMessage="No hay operaciones en el periodo seleccionado."
            skeletonRows={3}
          >
            <BarrasApiladas filas={filasOperaciones} series={SERIES_OPERACIONES} />
          </UiStateBoundary>
        </PanelInicio>

        {tenantId ? (
          <PanelInicio
            titulo="Resumen de compañía"
            icono="empresas"
            subtitulo={etiquetaCompania}
            accion={
              <button
                type="button"
                onClick={() => elegirCompania("")}
                className="text-sm font-medium text-[color:var(--di-action)] underline underline-offset-2"
              >
                ← Ver todas
              </button>
            }
            data-testid="panel-resumen-compania"
          >
            <UiStateBoundary
              status={companias.status === "ready" ? (resumenElegida ? "ready" : "empty") : companias.status}
              errorMessage={companias.error}
              onRetry={retry}
              emptyMessage="Esta compañía no tiene resumen de trámites: no es una compañía cliente activa."
              skeletonRows={2}
            >
              {resumenElegida && (
                <div className="grid grid-cols-1 sm:grid-cols-3 gap-3">
                  <div className="flex min-w-0 flex-col gap-2 rounded-[9px] border border-[color:var(--di-border)] bg-[var(--di-surface-elevated)] p-4">
                    <span className="text-sm font-medium text-[color:var(--di-text-primary)]">Trámites activos</span>
                    <span className="text-[length:var(--di-text-cifra)] font-bold leading-[1.1] tabular-nums text-[color:var(--kit-azul-texto)]">
                      {numero(resumenElegida.activos)}
                    </span>
                    {tendencia.status === "ready" && puntosTendencia.length > 1 ? (
                      <GraficoArea puntos={puntosTendencia} nombreSerie="Trámites creados por mes" alto={80} />
                    ) : (
                      <span className="text-xs text-[color:var(--di-text-tertiary)]">
                        {tendencia.status === "loading" ? "Cargando la tendencia…" : "Sin tendencia de los últimos 6 meses"}
                      </span>
                    )}
                  </div>
                  <div className="flex min-w-0 flex-col gap-2 rounded-[9px] border border-[color:var(--di-border)] bg-[var(--di-surface-elevated)] p-4">
                    <span className="text-sm font-medium text-[color:var(--di-text-primary)]">Tasa aprobación</span>
                    <span className="text-[length:var(--di-text-cifra)] font-bold leading-[1.1] tabular-nums text-[color:var(--kit-azul-texto)]">
                      {resumenElegida.tasaAprobacionPct === null ? "Sin decididos" : porcentaje(resumenElegida.tasaAprobacionPct)}
                    </span>
                    <BarraProgreso
                      etiqueta="Tasa de aprobación"
                      valor={resumenElegida.tasaAprobacionPct ?? 0}
                      maximo={100}
                      textoValor={resumenElegida.tasaAprobacionPct === null ? "Sin decididos" : porcentaje(resumenElegida.tasaAprobacionPct)}
                    />
                    <span className="text-xs text-[color:var(--di-text-tertiary)]">
                      {numero(resumenElegida.aprobados)} aprobados · {numero(resumenElegida.rechazados)} rechazados
                    </span>
                  </div>
                  <div className="flex min-w-0 flex-col gap-2 rounded-[9px] border border-[color:var(--di-border)] bg-[var(--di-surface-elevated)] p-4">
                    <span className="text-sm font-medium text-[color:var(--di-text-primary)]">Alertas / Represados</span>
                    <span className="text-[length:var(--di-text-cifra)] font-bold leading-[1.1] tabular-nums text-[color:var(--kit-azul-texto)]">
                      {numero(resumenElegida.represados)}
                    </span>
                    <span className="text-xs text-[color:var(--di-text-tertiary)]">
                      En proceso sin moverse en más de {DIAS_REPRESADO} días
                    </span>
                  </div>
                </div>
              )}
            </UiStateBoundary>
          </PanelInicio>
        ) : (
          <PanelInicio
            titulo="Resumen de compañías activas"
            icono="empresas"
            subtitulo="Pulsa una compañía para ver su detalle"
            data-testid="panel-companias"
          >
            <DataTable
              tarjeta={false}
              columns={columnasCompanias}
              rows={filasCompanias}
              getRowKey={(c) => c.tenantId}
              onRowClick={(c) => elegirCompania(c.tenantId)}
              status={companias.status}
              errorMessage={companias.error}
              onRetry={retry}
              emptyMessage="Ninguna compañía tiene trámites en el periodo seleccionado."
              ariaLabel="Resumen de compañías activas"
              pagination={{
                page: pagina,
                pageSize: pgCompanias.pageSize,
                totalCount: conTramites.length,
                onPageChange: pgCompanias.setPage,
                onPageSizeChange: pgCompanias.setPageSize,
              }}
            />
            {companias.status === "ready" && sinTramites > 0 && (
              <p className="mt-3 text-xs text-[color:var(--di-text-tertiary)]" data-testid="companias-sin-tramites">
                {numero(sinTramites)} {sinTramites === 1 ? "compañía activa no tiene" : "compañías activas no tienen"} trámites
                en el periodo seleccionado.
              </p>
            )}
          </PanelInicio>
        )}
      </div>
    </div>
  );
}
