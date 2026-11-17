"use client";

import { useCallback, useEffect, useMemo, useState } from "react";
import {
  BarChart,
  Bar,
  XAxis,
  YAxis,
  Tooltip,
  ResponsiveContainer,
  CartesianGrid,
} from "recharts";
import { Icono } from "@flit/ui/Icono";
import { UiStateBoundary, type UiStatus } from "@/components/admin/UiStateBoundary";
import {
  fetchActiveModules,
  fetchAnalyticsOverview,
  fetchMonthlyTrend,
  fetchNetworkAnalyticsOverview,
  fetchNetworkMonthlyTrend,
} from "@/lib/api/analytics";
import { fetchAllCompanies } from "@/lib/api/admin-companies";
import { ALL_TENANTS, tramitesClient } from "@/lib/api/tramites-client";
import { getToken } from "@/lib/api/client";
import { canReadIdentityDashboard, decodeJwtPayload, isSuperAdmin } from "@/lib/auth/jwt";
import { useActiveBanners } from "@/hooks/useActiveBanners";
import { useNetworkScope } from "@/hooks/useNetworkScope";
import { useFamilyAccess } from "@/hooks/useFamilyAccess";
import { COPY } from "@/lib/copy/copy-catalog";
import { NetworkScopeSelector } from "@/components/operacion/NetworkScopeSelector";
import { NetworkScopeBadge } from "@/components/operacion/NetworkScopeBadge";
import { ETIQUETA_SOLO_COMPANIA_PROPIA } from "@/lib/tramites/network-scope";
import { estadoAcentoVar, estadoLabel } from "@/lib/tramites/estados";
import { TarjetaMetrica } from "@flit/ui/TarjetaMetrica";
import { RangoFechas } from "@/components/atom/RangoFechas";
import { GraficoDona } from "@/components/inicio/GraficoDona";
import { CompanySelector } from "./_reportes/CompanySelector";
import { isValidOptionalRange, sinRango, type DateRange } from "./_reportes/range";
import { ApiError } from "@/lib/api/types";
import type {
  ActiveModulesResponse,
  AnalyticsOverviewResponse,
  CompanyListItem,
  MonthlyTrendPoint,
} from "@/lib/api/types";
import type { BiometricValidationStats } from "@/lib/api/types/procedure-runtime";
import { InicioModulos } from "@/components/inicio/InicioModulos";
import { CarruselInicio } from "@/components/inicio/CarruselInicio";
import { FILTRO_CONTROL_CLS, FILTROS_REJILLA_CLS } from "@/components/inicio/filtros-inicio";

/** KPI de volumen del dashboard gestor (HU #12700 / E03). */
export const DASHBOARD_KPI_TOTAL_LABEL = COPY.E03;
/** Hero del dashboard gestor (A18 es N/A: no se unifica con el hero OT). */
export const DASHBOARD_HERO_PREFIX = "Hola,";

// ── Helpers de rango ──────────────────────────────────────────────────────────

function fmtDate(d: Date): string {
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, "0")}-${String(d.getDate()).padStart(2, "0")}`;
}

function lastNMonthsRange(n: number): { from: string; to: string } {
  const now = new Date();
  return {
    from: fmtDate(new Date(now.getFullYear(), now.getMonth() - (n - 1), 1)),
    to: fmtDate(new Date(now.getFullYear(), now.getMonth() + 1, 0)),
  };
}

// ── Helpers de datos ──────────────────────────────────────────────────────────

const MONTH_NAMES = ["Ene", "Feb", "Mar", "Abr", "May", "Jun", "Jul", "Ago", "Sep", "Oct", "Nov", "Dic"];

/** Series de «Seguimiento operativo» (barra + leyenda) y la familia que cada una representa (HU #13665). */
const SERIES = [
  { label: "Matrículas", color: "var(--color-flit-brand)", family: "MATRICULAS" },
  { label: "Traspasos", color: "var(--color-flit-tech)", family: "TRASPASO" },
  // HU #14481 — sin naranja: la tinta de la propuesta (`--di-accent`, que en oscuro pasa a azul claro).
  { label: "Otros", color: "var(--di-accent)", family: "OTROS" },
] as const;

function buildChartData(items: MonthlyTrendPoint[]) {
  const map = new Map<string, { m: string; Matrículas: number; Traspasos: number; Otros: number }>();
  for (const pt of items) {
    const key = `${pt.year}-${String(pt.month).padStart(2, "0")}`;
    if (!map.has(key)) {
      map.set(key, { m: MONTH_NAMES[pt.month - 1], Matrículas: 0, Traspasos: 0, Otros: 0 });
    }
    const row = map.get(key)!;
    if (pt.category === "matriculas") row["Matrículas"] = pt.total;
    else if (pt.category === "traspasos") row["Traspasos"] = pt.total;
    else row["Otros"] = pt.total;
  }
  // Mantener orden cronológico
  return Array.from(map.entries())
    .sort(([a], [b]) => a.localeCompare(b))
    .map(([, v]) => v);
}

function describeError(error: unknown, networkActive = false): string {
  if (error instanceof ApiError) {
    if (error.status === 403) {
      return networkActive
        ? "No tienes acceso a las métricas de la red."
        : "No tienes acceso a las métricas de esa compañía.";
    }
    if (error.status === 401) return "Tu sesión expiró. Vuelve a iniciar sesión.";
  }
  return "No se pudieron cargar las métricas del dashboard.";
}

/**
 * Cuerpo del slide de bienvenida — datos que el propio Dashboard ya trae para la tarjeta
 * "Validaciones Biométricas" (`biometricStats`/`expiringSoonCount`) y para la Distribución
 * General (`totalTramites`), sin ninguna llamada adicional. Mientras esas cargas no estén listas
 * se mantiene el texto genérico anterior, para no parpadear con "0 alertas" antes de tiempo.
 */
function buildWelcomeBody(
  biometricStatus: UiStatus,
  biometricStats: BiometricValidationStats | null,
  expiringSoonCount: number,
  totalTramites: number,
): string {
  const generic =
    "Tus procesos y validaciones se encuentran sincronizados. Continúa gestionando tu operación de manera segura y eficiente.";
  if (biometricStatus !== "ready" || !biometricStats) return generic;

  const rechazadas = biometricStats.rechazadas;
  const porVencer = expiringSoonCount;

  if (rechazadas > 0 && porVencer > 0) {
    return `Tienes ${rechazadas + porVencer} validaciones de identidad que requieren tu atención: ${rechazadas} rechazada${rechazadas === 1 ? "" : "s"} y ${porVencer} por vencer.`;
  }
  if (rechazadas > 0) {
    const validacion = rechazadas === 1 ? "validación" : "validaciones";
    const rechazada = rechazadas === 1 ? "rechazada" : "rechazadas";
    const requiere = rechazadas === 1 ? "requiere" : "requieren";
    return `Tienes ${rechazadas} ${validacion} de identidad ${rechazada} que ${requiere} tu atención.`;
  }
  if (porVencer > 0) {
    const validacion = porVencer === 1 ? "validación" : "validaciones";
    const requiere = porVencer === 1 ? "requiere" : "requieren";
    return `Tienes ${porVencer} ${validacion} de identidad por vencer que ${requiere} tu atención.`;
  }
  return `Tienes ${totalTramites} trámite${totalTramites === 1 ? "" : "s"} en este periodo. Todo sincronizado — sin identidades pendientes de atención.`;
}

// ── Componente principal ──────────────────────────────────────────────────────

// eslint-disable-next-line @typescript-eslint/no-unused-vars
export function Dashboard({ onNewTramite: _onNewTramite }: { onNewTramite: () => void }) {
  // Identidad del usuario
  const [displayName, setDisplayName] = useState("—");
  const [isSuper, setIsSuper] = useState(false);
  // HU #12711 — la API de identidad exige el permiso del módulo (o dashboard.read) y rechaza a los
  // organismos: sin él, la tarjeta de validaciones no se pide ni se pinta (antes quedaba en error).
  const [canSeeBiometrics, setCanSeeBiometrics] = useState(false);

  // Selector de compañía (solo SuperAdmin)
  const [companies, setCompanies] = useState<CompanyListItem[]>([]);
  const [tenantId, setTenantId] = useState("");

  // Rango de fechas de las métricas (KPIs, distribución general, validaciones biométricas) — visible a todos los roles.
  /**
   * BUG #12588 — arranca SIN rango: el total tiene que ser el número real de trámites del tenant.
   * Antes partía del mes en curso, y como el backend filtra por fecha de CREACIÓN, todo lo radicado
   * antes quedaba fuera de las tarjetas aunque siguiera en curso; QA lo leyó como un conteo mal
   * calculado. El filtro sigue disponible para acotar a mano.
   */
  const [range, setRange] = useState<DateRange>(() => sinRango());

  // HU #12364 — alcance de red de una cabeza de grupo: el MISMO control y la MISMA preferencia
  // (`tramites.scope`) que el listado de trámites (AC5). Para quien no es cabeza el hook no hace
  // ninguna llamada y `networkActive` es siempre falso: las llamadas de abajo quedan como hoy (AC4).
  const net = useNetworkScope();
  const networkActive = net.networkActive;
  const networkChildTenantId = net.scope.childTenantId;
  const networkReady = net.ready;

  // Datos de la API
  const [overview, setOverview] = useState<AnalyticsOverviewResponse | null>(null);
  const [trend, setTrend] = useState<MonthlyTrendPoint[]>([]);

  // Estados UI
  const [status, setStatus] = useState<UiStatus>("loading");
  const [errorMessage, setErrorMessage] = useState<string>();
  const [reloadKey, setReloadKey] = useState(0);

  // Validaciones biométricas (card "Validaciones Biométricas") — fuente y ciclo de vida
  // independientes del overview de analytics.
  const [biometricStats, setBiometricStats] = useState<BiometricValidationStats | null>(null);
  const [expiringSoonCount, setExpiringSoonCount] = useState(0);
  const [biometricStatus, setBiometricStatus] = useState<UiStatus>("loading");
  const [biometricErrorMessage, setBiometricErrorMessage] = useState<string>();

  // Módulos activos del tenant (HU #12253, Feature #12249) — Trámites/Comparendos/
  // Resoluciones, fuente y ciclo de vida independientes del overview. `null` mientras
  // carga o si la consulta falla; los flags derivados asumen `true` por defecto (AC1, AC5)
  // para no ocultar Trámites ni mostrar prematuramente las tarjetas "Próximamente".
  const [activeModules, setActiveModules] = useState<ActiveModulesResponse | null>(null);
  const [activeModulesStatus, setActiveModulesStatus] = useState<UiStatus>("loading");
  const [activeModulesErrorMessage, setActiveModulesErrorMessage] = useState<string>();

  // Banners Activos (HU #12242, AC1). Un fallo de red ya degrada en silencio dentro del hook (AC3);
  // los banners cuya imagen falla los retira `CarruselInicio`.
  const activeBanners = useActiveBanners();

  // Leer identidad del JWT en cliente (el token solo existe en cliente tras el montaje).
  useEffect(() => {
    const payload = decodeJwtPayload(getToken());
    const name = (payload?.display_name as string | undefined) ?? (payload?.email as string | undefined) ?? "—";
    // eslint-disable-next-line react-hooks/set-state-in-effect
    setDisplayName(name);
    setIsSuper(isSuperAdmin(payload));
    setCanSeeBiometrics(canReadIdentityDashboard(payload));
  }, []);

  // Cargar catálogo COMPLETO de compañías para el selector SuperAdmin (paginado internamente:
  // el buscador filtra en cliente y no puede encontrar lo que nunca llegó al navegador).
  useEffect(() => {
    if (!isSuper) return;
    const controller = new AbortController();
    fetchAllCompanies({ estadoActivo: true }, controller.signal)
      .then((data) => {
        if (!controller.signal.aborted) setCompanies(data);
      })
      .catch(() => { /* silencioso: el selector queda vacío, el dashboard sigue operativo */ });
    return () => controller.abort();
  }, [isSuper]);

  // Cargar datos reales (mes actual + últimos 6 meses para el gráfico)
  useEffect(() => {
    const controller = new AbortController();

    async function load() {
      setStatus("loading");

      if (!isValidOptionalRange(range)) {
        setErrorMessage("La fecha inicial no puede ser posterior a la fecha final.");
        setStatus("error");
        return;
      }

      const monthRange = range;
      // Tendencia mensual: siempre los últimos 6 meses, sin acoplarse al filtro de rango
      // (es una vista macro; si siguiera el filtro, un rango corto la dejaría sin sentido).
      const trendRange = lastNMonthsRange(6);
      const tid = tenantId || undefined;

      try {
        // HU #12364 AC1 — con la red activa las mismas dos consultas van a `network/stats/*`
        // (con `childTenantId` si se eligió un hijo); con alcance propio, llamadas idénticas a hoy.
        const [overviewRes, trendRes] = networkActive
          ? await Promise.all([
              fetchNetworkAnalyticsOverview(
                { from: monthRange.from, to: monthRange.to, childTenantId: networkChildTenantId },
                controller.signal,
              ),
              fetchNetworkMonthlyTrend(
                { from: trendRange.from, to: trendRange.to, childTenantId: networkChildTenantId },
                controller.signal,
              ),
            ])
          : await Promise.all([
              fetchAnalyticsOverview({ from: monthRange.from, to: monthRange.to, tenantId: tid }, controller.signal),
              fetchMonthlyTrend({ from: trendRange.from, to: trendRange.to, tenantId: tid }, controller.signal),
            ]);
        if (controller.signal.aborted) return;
        setOverview(overviewRes);
        setTrend(trendRes.items);
        setStatus("ready");
      } catch (err) {
        if (controller.signal.aborted || (err as Error).name === "AbortError") return;
        setErrorMessage(describeError(err, networkActive));
        setStatus("error");
      }
    }

    // La cabeza espera a conocer su alcance guardado para no pedir «lo propio» y luego «la red».
    if (!networkReady) return () => controller.abort();
    void load();
    return () => controller.abort();
  }, [range, tenantId, reloadKey, networkActive, networkChildTenantId, networkReady]);

  // Cargar KPIs de validaciones biométricas (card "Validaciones Biométricas"), independiente
  // del overview de analytics.
  useEffect(() => {
    const controller = new AbortController();

    async function loadBiometrics() {
      if (!canSeeBiometrics) return;
      // HU #12706 (AC4) — el listado plano ya tiene vista global para el SuperAdmin: en «Todas las
      // compañías» se pide explícitamente sin compañía (ALL_TENANTS), no con el tenant del propio JWT
      // del SuperAdmin, que daría datos silenciosamente equivocados.
      const biometricTenant = isSuper && !tenantId ? ALL_TENANTS : tenantId || undefined;
      setBiometricStatus("loading");

      if (!isValidOptionalRange(range)) {
        setBiometricErrorMessage("La fecha inicial no puede ser posterior a la fecha final.");
        setBiometricStatus("error");
        return;
      }

      // BUG #12588 — sin extremo no se manda el filtro: estas estadísticas siguen al mismo rango que
      // las tarjetas, así que sin rango cuentan todo. `createdFrom`/`createdTo` ya eran opcionales en
      // el cliente (la consulta hermana de «por vencer» nunca los manda).
      const createdFrom = range.from ? `${range.from}T00:00:00` : undefined;
      const createdTo = range.to ? `${range.to}T23:59:59` : undefined;

      try {
        const [statsRes, expiringRes] = await Promise.all([
          tramitesClient.listTenantBiometricValidations({ createdFrom, createdTo, pageSize: 10 }, biometricTenant),
          tramitesClient.listTenantBiometricValidations({ vigenciaEstado: "por_vencer", pageSize: 10 }, biometricTenant),
        ]);
        if (controller.signal.aborted) return;
        setBiometricStats(statsRes.stats);
        setExpiringSoonCount(expiringRes.total);
        setBiometricStatus("ready");
      } catch (err) {
        if (controller.signal.aborted || (err as Error).name === "AbortError") return;
        setBiometricErrorMessage(describeError(err));
        setBiometricStatus("error");
      }
    }

    void loadBiometrics();
    return () => controller.abort();
  }, [range, tenantId, isSuper, reloadKey, canSeeBiometrics]);

  // Cargar flags de módulos activos (Trámites/Comparendos/Resoluciones), independiente del
  // rango de fechas — no depende de `range` (AC5: un fallo aquí no debe tumbar ni bloquear
  // el resto de secciones del dashboard, cada una con su propio status aislado).
  useEffect(() => {
    const controller = new AbortController();

    async function loadActiveModules() {
      // El endpoint NO tiene vista global (son los flags de UN tenant): un SuperAdmin en
      // "Todas las compañías" recibiría 400 en cada intento, y sin este atajo la sección quedaba
      // en error permanente sin importar qué se cambiara en configuración — el toggle nunca
      // llegaba a verse reflejado porque la llamada ni siquiera se hacía contra un tenant
      // concreto. Mismo patrón que `loadBiometrics` (líneas arriba) para el mismo caso.
      if (isSuper && !tenantId) {
        setActiveModulesStatus("empty");
        return;
      }
      setActiveModulesStatus("loading");
      try {
        const res = await fetchActiveModules(tenantId || undefined, controller.signal);
        if (controller.signal.aborted) return;
        setActiveModules(res);
        setActiveModulesStatus("ready");
      } catch (err) {
        if (controller.signal.aborted || (err as Error).name === "AbortError") return;
        setActiveModulesErrorMessage(describeError(err));
        setActiveModulesStatus("error");
      }
    }

    void loadActiveModules();
    return () => controller.abort();
  }, [tenantId, isSuper, reloadKey]);

  const retry = useCallback(() => setReloadKey((k) => k + 1), []);

  // Derivar métricas del overview
  const categories = useMemo(() => overview?.categories ?? [], [overview]);
  const totalTramites = categories.reduce((sum, c) => sum + c.total, 0);

  // Auto-avance del carrusel. El cuerpo del slide de bienvenida depende de datos que cargan
  // aparte (biometría, overview) — ver `buildWelcomeBody`. Memoizado aparte (no solo inline en
  // `slides`) porque el React Compiler exige que toda dependencia de un `useMemo` sea a su vez
  // estable/memoizada.
  const welcomeBody = useMemo(
    () => buildWelcomeBody(biometricStatus, biometricStats, expiringSoonCount, totalTramites),
    [biometricStatus, biometricStats, expiringSoonCount, totalTramites],
  );
  const bienvenida = useMemo(
    () => [{ titulo: `${DASHBOARD_HERO_PREFIX} ${displayName}`, cuerpo: welcomeBody }],
    [displayName, welcomeBody],
  );
  const matriculas = categories.find((c) => c.category === "matriculas")?.total ?? 0;
  const traspasos = categories.find((c) => c.category === "traspasos")?.total ?? 0;
  const otros = categories.find((c) => c.category === "otros")?.total ?? 0;
  // HU #13665 (FF-4) — sin Ver de una familia, su tarjeta y su serie no se pintan (la API ya no la cuenta).
  const { showsInAggregates } = useFamilyAccess();

  // Distribución consolidada por estado de TODAS las categorías (no solo traspasos).
  const globalFunnel = useMemo(() => {
    const map = new Map<string, number>();
    for (const cat of categories) {
      for (const s of cat.byStatus) {
        map.set(s.status, (map.get(s.status) ?? 0) + s.count);
      }
    }
    return Array.from(map, ([status, count]) => ({ status, count }))
      .filter((s) => s.count > 0)
      .sort((a, b) => b.count - a.count);
  }, [categories]);

  const totalFunnel = globalFunnel.reduce((sum, f) => sum + f.count, 0);

  const chartData = useMemo(() => buildChartData(trend), [trend]);

  // Estado independiente por sección: el overview (rango filtrado) gobierna la
  // distribución general; la tendencia (6 meses fijos) gobierna la gráfica, sin acoplarse
  // al vacío del overview — una compañía sin trámites en el rango elegido puede seguir
  // teniendo tendencia histórica que mostrar. Las validaciones biométricas tienen su
  // propio ciclo de carga/estado (biometricStatus), independiente de ambos.
  const overviewHasData = categories.some((c) => c.total > 0);
  const overviewStatus: UiStatus = status === "ready" ? (overviewHasData ? "ready" : "empty") : status;
  const chartHasData = chartData.length > 0;
  const chartStatus: UiStatus = status === "ready" ? (chartHasData ? "ready" : "empty") : status;

  // Flags de módulos del tenant (AC1, AC2, AC3, AC4 — redefinidos tras validar en vivo con el
  // usuario: la tarjeta "Próximamente" avisa de un módulo que la compañía SÍ activó pero que
  // todavía no tiene contenido real construido, no al revés. Si el flag está apagado, la
  // compañía no lo contrató: no tiene sentido mencionárselo.
  //
  // Trámites (AC1) mantiene su semántica original — mientras carga o si falla, se asume
  // habilitado (`true`) para no parpadear, ya con contenido real. Comparendos/Resoluciones
  // (AC2-AC4) mientras carga o si falla se asumen APAGADOS (`false`): más seguro no anunciar
  // un módulo de más que anunciar uno que la compañía no activó.
  const tramitesModuleEnabled = activeModules?.tramitesModuleEnabled ?? true;
  const comparendosModuleEnabled = activeModules?.comparendosModuleEnabled ?? false;
  const resolucionesModuleEnabled = activeModules?.resolucionesModuleEnabled ?? false;
  const comingSoonModules = [
    { key: "comparendos", label: "Comparendos", enabled: comparendosModuleEnabled },
    { key: "resoluciones", label: "Resoluciones", enabled: resolucionesModuleEnabled },
  ].filter((m) => m.enabled === true);

  // Estado compuesto de la fila "Próximamente" (AC5): mientras carga o si falla, el
  // UiStateBoundary aislado lo refleja SIN afectar el resto del dashboard (overview,
  // biometricStats, etc. tienen su propio status independiente).
  const comingSoonStatus: UiStatus =
    activeModulesStatus === "ready" ? (comingSoonModules.length > 0 ? "ready" : "empty") : activeModulesStatus;

  return (
    <div className="app-bg min-h-screen px-6 pt-6 pb-10 flex flex-col gap-4 text-foreground">
      {/* Fila superior: Banner + KPIs — la columna derecha fija el alto; el banner se estira (HU #12725 B.1). */}
      <div className="grid grid-cols-1 md:grid-cols-3 gap-3 shrink-0 items-stretch">
        <CarruselInicio
          className="md:col-span-2"
          bienvenida={bienvenida}
          banners={activeBanners}
        />

        {/* Columna derecha: filtros en fila + KPIs 2×2 (HU #12725 B.1 / D3). */}
        <div
          // HU #14481 (referencia de Samuel): la columna fija el alto de la fila y el banner se estira a ella
          // (`items-stretch`), no al revés; por eso aquí no hay alto mínimo.
          className="flex flex-col gap-3"
          data-testid="dashboard-hero-column"
        >
          <div
            className={`${FILTROS_REJILLA_CLS} ${
              isSuper ? "" : "[&>[data-testid=rango-fechas]>label:first-child]:col-span-2"
            }`}
            data-testid="dashboard-filter-row"
          >
            <RangoFechas
              value={range}
              onChange={setRange}
              permiteSinRango
              idPrefix="dashboard-rango"
            />
            {isSuper && (
              <CompanySelector
                companies={companies}
                value={tenantId}
                onChange={setTenantId}
                disabled={status === "loading"}
                defaultLabel="Todas las compañías"
                id="dashboard-compania"
                className={`col-start-2 row-start-1 min-w-0 ${FILTRO_CONTROL_CLS}`}
              />
            )}
          </div>
          {/* HU #12364 — solo para una cabeza de red (AC4: nadie más lo ve). */}
          {net.isGroupParent && (
            <NetworkScopeSelector
              scope={net.scope}
              onChange={net.setScope}
              hijos={net.children}
              childrenStatus={net.childrenStatus}
              disabled={net.saving}
              testId="dashboard-network-scope-select"
            />
          )}
          {/* KPIs de Trámites — solo visibles si el módulo está habilitado para el tenant
              (AC1: `true` por defecto mientras carga, evita ocultar la sección con parpadeo). */}
          {tramitesModuleEnabled !== false && (
            // HU #14481 (referencia de Samuel) — solo las tres familias, en una fila de 3 columnas; el
            // «Total trámites» y «Completados» salen de la cabecera (el total sigue en el saludo).
            // Compactas y arriba, bajo los filtros (referencia de Samuel): sin `flex-1` ni estirarse hasta el alto
            // del banner; la columna puede quedar más baja que él.
            <div className="grid grid-cols-3 items-start gap-3" data-testid="dashboard-kpi-grid">
              {[
                // UI Kit 2026: claves del kit con glifo (disco --kit-hover y glifo azul), no los discos sólidos
                // @tramite-matricula/@tramite-traspaso: placa para matrícula e intercambio para traspaso.
                { label: "Matrículas", value: matriculas, ilustracion: "preasignacion" as const, family: "MATRICULAS" as const },
                { label: "Traspasos", value: traspasos, ilustracion: "llamadas" as const, family: "TRASPASO" as const },
                { label: "Otros Trámites", value: otros, ilustracion: "procesos" as const, family: "OTROS" as const },
              ].filter((k) => showsInAggregates(k.family)).map((k) => {
                const isError = status === "error";
                // HU #14481 (propuesta): `TarjetaMetrica` de @flit/ui —etiqueta, cifra en tinta e
                // ilustración— en vez del número de color y el ícono en un círculo.
                return (
                  <TarjetaMetrica
                    key={k.label}
                    // La columna de 1/3 deja ~120 px: en la rejilla de TarjetaMetrica (1fr + auto) el
                    // rótulo se comía el ancho y la ilustración quedaba en 0 px. Aquí la tarjeta va a
                    // una columna —el rótulo usa todo el ancho y no se parte— y la ilustración se fija
                    // abajo a la derecha, junto a la cifra, con un tamaño que no se encoge.
                    className="relative grid-cols-1 content-start gap-y-0.5 p-3.5 [&>:nth-child(3)]:absolute [&>:nth-child(3)]:bottom-3 [&>:nth-child(3)]:right-3 [&>:nth-child(3)]:h-10 [&>:nth-child(3)]:w-10 [&>:nth-child(3)]:shrink-0 [&>:nth-child(3)]:max-w-none [&>span:nth-child(2)]:pr-12 [&>span:nth-child(2)]:text-2xl"
                    icono={k.ilustracion}
                    etiqueta={
                      <span className="block min-w-0">
                        {/* line-clamp-2 en vez de truncate: si algún día entra un rótulo más largo,
                            se parte en dos líneas en vez de perder texto por el borde. */}
                        <span className="block text-xs font-medium leading-tight line-clamp-2">{k.label}</span>
                        {/* AC1 — cada indicador dice que es de la red (texto, no solo color). */}
                        {networkActive && (
                          <NetworkScopeBadge
                            scope={net.scope}
                            hijos={net.children}
                            className="mt-1"
                            testId={`kpi-red-${k.label}`}
                          />
                        )}
                      </span>
                    }
                    cifra={
                      isError ? (
                        <span
                          className="flex items-center gap-1.5 min-w-0 text-xl"
                          style={{ color: "var(--di-danger)" }}
                          title={errorMessage ?? "No se pudo cargar este indicador."}
                        >
                          <Icono nombre="alerta" className="h-4 w-4 shrink-0" />
                          <span>—</span>
                          <span className="sr-only">Error al cargar {k.label.toLowerCase()}</span>
                        </span>
                      ) : status === "loading" ? (
                        "—"
                      ) : (
                        k.value
                      )
                    }
                  />
                );
              })}
            </div>
          )}
        </div>
      </div>

      {/* Módulos aún no habilitados para el tenant ("Próximamente") — HU #12253. Estado
          aislado (comingSoonStatus): un fallo al consultar los flags no bloquea el resto
          del dashboard (AC5). Sin nada que anunciar (ni módulos por activar ni una compañía
          elegida) la sección no ocupa espacio: ni tarjetas ni mensaje de estado vacío. */}
      {comingSoonStatus !== "empty" && (
        <UiStateBoundary
          status={comingSoonStatus}
          errorMessage={activeModulesErrorMessage}
          onRetry={retry}
          skeletonRows={1}
          className="shrink-0"
        >
          <div className="grid grid-cols-1 sm:grid-cols-2 gap-3 shrink-0">
            {comingSoonModules.map((m) => (
              <div
                key={m.key}
                className="rounded-2xl p-4 flex items-center gap-3 bg-card border border-border"
              >
                <div
                  className="h-11 w-11 rounded-xl grid place-items-center shrink-0"
                  style={{ background: "var(--badge-neutral-bg)" }}
                >
                  <Icono nombre="reloj" className="h-5 w-5" style={{ color: "var(--badge-neutral-fg)" }} />
                </div>
                <div>
                  <p className="text-sm font-bold">{m.label}</p>
                  <p className="text-[11px] opacity-70 font-medium">Próximamente</p>
                </div>
              </div>
            ))}
          </div>
        </UiStateBoundary>
      )}

      {/* HU #14481 — cifras por estado y módulos del rol en acordeón. */}
      <InicioModulos />

      {/* Fila inferior: Distribución general + Validaciones Biométricas (cada una con su propio estado) + gráfica mensual (chartStatus) */}
      <div className="grid grid-cols-1 md:grid-cols-3 gap-3">
        <div className={`md:col-span-2 grid grid-cols-1 gap-3 ${canSeeBiometrics ? "md:grid-cols-2" : ""}`}>
          {/* Distribución general de trámites — solo visible si el módulo Trámites está
              habilitado (AC1), mismo default `true` que el bloque de KPIs. */}
          {tramitesModuleEnabled !== false && (
            <UiStateBoundary
              status={overviewStatus}
              errorMessage={errorMessage}
              onRetry={retry}
              emptyMessage="No hay trámites para el rango seleccionado."
              skeletonRows={3}
            >
              {/* Distribución general de trámites por estado (las 4 categorías, no solo traspasos) */}
              <section className="rounded-[9px] border border-[color:var(--di-border)] bg-card px-6 pb-6 pt-4 max-sm:p-4 flex flex-col">
                <h2 className="mb-4 text-2xl font-medium leading-tight text-[color:var(--kit-azul-texto)] flex flex-wrap items-center gap-2">
                  Distribución General de Trámites
                  {networkActive && <NetworkScopeBadge scope={net.scope} hijos={net.children} testId="distribucion-red" />}
                </h2>
                {globalFunnel.length === 0 ? (
                  <p className="text-xs opacity-50 mt-2">Sin trámites en el rango seleccionado.</p>
                ) : (
                  // UI Kit 2026 — dona de la propuesta con los MISMOS datos que pintaba la lista: cada estado con su
                  // acento, su cifra y su porcentaje; el total al centro. La leyenda sigue siendo la lista accesible.
                  <GraficoDona
                    total={totalFunnel}
                    porciones={globalFunnel.map((f) => ({
                      clave: f.status,
                      etiqueta: estadoLabel(f.status),
                      valor: f.count,
                      color: estadoAcentoVar(f.status),
                      detalle: `${totalFunnel > 0 ? Math.round((f.count / totalFunnel) * 100) : 0}\u00a0%`,
                    }))}
                  />
                )}
              </section>
            </UiStateBoundary>
          )}

          {canSeeBiometrics && (
          <UiStateBoundary
            status={biometricStatus}
            errorMessage={biometricErrorMessage}
            onRetry={retry}
            emptyMessage="Selecciona una compañía para ver sus validaciones biométricas."
            skeletonRows={3}
          >
            {/* Validaciones Biométricas: KPIs + aviso de próximas a vencer */}
            <section className="rounded-[9px] border border-[color:var(--di-border)] bg-card px-6 pb-6 pt-4 max-sm:p-4 flex flex-col">
              <h2 className="mb-4 text-2xl font-medium leading-tight text-[color:var(--kit-azul-texto)] flex flex-wrap items-center gap-2">
                Validaciones Biométricas
                {/* Sin ruta de red para biometría: sigue siendo del cliente propio y se rotula
                    para que no se lea como agregado de la red (AC1). */}
                {networkActive && (
                  <span className="text-[11px] font-medium opacity-70" data-testid="biometria-solo-propia">
                    {ETIQUETA_SOLO_COMPANIA_PROPIA}
                  </span>
                )}
              </h2>
              {/* HU #14481 (referencia de Samuel) — tres cajitas; el punto de color, con el acento del estado
                  de trámite equivalente (Aprobado, Preparado, Rechazado), las distingue sin ícono. */}
              <div className="grid grid-cols-3 gap-2">
                {[
                  { label: "Aprobadas", value: biometricStats?.aprobadas ?? 0, punto: estadoAcentoVar("aprobado") },
                  { label: "En proceso", value: biometricStats?.enProceso ?? 0, punto: estadoAcentoVar("preparado") },
                  { label: "Rechazadas", value: biometricStats?.rechazadas ?? 0, punto: estadoAcentoVar("rechazado") },
                ].map((k) => (
                  // Referencia de Samuel: tarjetita blanca con todo centrado — punto, nombre y cifra.
                  <div
                    key={k.label}
                    className="flex min-w-0 flex-col items-center gap-1.5 rounded-[9px] border border-[color:var(--di-border)] bg-[var(--di-surface-elevated)] px-2 py-3 text-center"
                  >
                    <span aria-hidden="true" className="h-3 w-3 shrink-0 rounded-full" style={{ background: k.punto }} />
                    <p className="text-xs font-medium leading-tight text-[color:var(--di-text-secondary)]">{k.label}</p>
                    <p className="text-2xl font-bold leading-tight tabular-nums text-[color:var(--di-text-primary)]">
                      {k.value}
                    </p>
                  </div>
                ))}
              </div>
              {expiringSoonCount > 0 && (
                <div
                  className="mt-3 flex items-center gap-2 rounded-xl px-3 py-2 text-xs font-medium"
                  // Aviso pendiente de la propuesta: cielo con texto en tinta; el naranja queda en el ícono.
                  style={{ background: "var(--di-sky)", color: "var(--di-heading)" }}
                >
                  <Icono nombre="alerta" className="h-3.5 w-3.5 shrink-0" style={{ color: "var(--di-alerta-punto)" }} />
                  {expiringSoonCount} validación{expiringSoonCount === 1 ? "" : "es"} próxima
                  {expiringSoonCount === 1 ? "" : "s"} a vencer
                </div>
              )}
            </section>
          </UiStateBoundary>
          )}
        </div>

        {/* Gráfico mensual por categoría — tendencia de 6 meses, independiente del rango
            filtrado. Solo visible si el módulo Trámites está habilitado (AC1). */}
        {tramitesModuleEnabled !== false && (
          <UiStateBoundary
            status={chartStatus}
            errorMessage={errorMessage}
            onRetry={retry}
            emptyMessage="No hay datos de tendencia en los últimos 6 meses."
            skeletonRows={3}
          >
            <section className="rounded-[9px] border border-[color:var(--di-border)] bg-card px-6 pb-6 pt-4 max-sm:p-4 flex flex-col">
              <h2 className="mb-4 text-2xl font-medium leading-tight text-[color:var(--kit-azul-texto)] flex flex-wrap items-center gap-2">
                Seguimiento operativo
                {networkActive && <NetworkScopeBadge scope={net.scope} hijos={net.children} testId="seguimiento-red" />}
              </h2>
              <div className="h-[280px] -mx-2">
                <ResponsiveContainer width="100%" height="100%">
                  <BarChart data={chartData} margin={{ top: 4, right: 8, left: -20, bottom: 0 }}>
                    {/* UI Kit 2026 (`barrasGrupo`): guías horizontales punteadas 3·4 en --kit-linea y ejes en 11 px gris. */}
                    <CartesianGrid
                      strokeDasharray="3 4"
                      stroke="var(--kit-linea)"
                      vertical={false}
                    />
                    <XAxis
                      dataKey="m"
                      tick={{ fontSize: 11, fill: "var(--di-text-tertiary)" }}
                      axisLine={false}
                      tickLine={false}
                    />
                    <YAxis
                      tick={{ fontSize: 11, fill: "var(--di-text-tertiary)" }}
                      axisLine={false}
                      tickLine={false}
                    />
                    <Tooltip
                      cursor={{ fill: "color-mix(in srgb, var(--color-flit-brand) 8%, transparent)" }}
                      contentStyle={{
                        background: "rgba(22,39,68,0.95)",
                        border: "none",
                        borderRadius: 10,
                        color: "white",
                        fontSize: 11,
                      }}
                      labelStyle={{ color: "var(--color-flit-tech)", fontWeight: 600 }}
                    />
                    {SERIES.filter((s) => showsInAggregates(s.family)).map((s) => (
                      <Bar key={s.label} dataKey={s.label} fill={s.color} radius={[3, 3, 0, 0]} />
                    ))}
                  </BarChart>
                </ResponsiveContainer>
              </div>
              {/* Leyenda centrada del kit (`.leyenda`): puntos de 10 px, texto de 13 px. */}
              <div className="mt-2 flex flex-wrap items-center justify-center gap-x-[18px] gap-y-1.5 text-[0.8125rem]">
                {SERIES.filter((s) => showsInAggregates(s.family)).map(({ color, label }) => (
                  <span key={label} className="flex items-center gap-2">
                    <span className="h-2.5 w-2.5 rounded-full" style={{ background: color }} />
                    {label}
                  </span>
                ))}
              </div>
            </section>
          </UiStateBoundary>
        )}
      </div>
    </div>
  );
}
