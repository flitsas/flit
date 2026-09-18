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
import {
  Activity,
  AlertTriangle,
  ChevronLeft,
  ChevronRight,
  Clock,
  ExternalLink,
  FileText,
  CheckCircle,
  Car,
  Layers,
} from "lucide-react";
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
import { bannerImageUrl, type ActiveBanner } from "@/lib/api/public-banners";
import { useActiveBanners } from "@/hooks/useActiveBanners";
import { bannerAmbientGradient, useDominantColor } from "@/hooks/useDominantColor";
import { useNetworkScope } from "@/hooks/useNetworkScope";
import { NetworkScopeSelector } from "@/components/operacion/NetworkScopeSelector";
import { NetworkScopeBadge } from "@/components/operacion/NetworkScopeBadge";
import { ETIQUETA_SOLO_COMPANIA_PROPIA } from "@/lib/tramites/network-scope";
import { estadoChipStyle, estadoLabel } from "@/lib/tramites/estados";
import { CompanySelector } from "./_reportes/CompanySelector";
import { DateRangeFilter } from "./_reportes/DateRangeFilter";
import { isValidOptionalRange, sinRango, type DateRange } from "./_reportes/range";
import { ApiError } from "@/lib/api/types";
import type {
  ActiveModulesResponse,
  AnalyticsOverviewResponse,
  CategoryMetrics,
  CompanyListItem,
  MonthlyTrendPoint,
} from "@/lib/api/types";
import type { BiometricValidationStats } from "@/lib/api/types/procedure-runtime";

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

function countCompleted(categories: CategoryMetrics[]): number {
  return categories.reduce(
    (sum, cat) => sum + (cat.byStatus.find((s) => s.status === "aprobado")?.count ?? 0),
    0,
  );
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

// ── Slides del banner ─────────────────────────────────────────────────────────
//
// HU #12242 (Feature #12236): el slide fijo de bienvenida siempre va primero, no es configurable
// y no puede faltar. Detrás de él van los banners Activos del Administrador (AC1); sin banners
// activos el carrusel solo muestra el slide fijo (AC3).

/** Fondo del carrusel, IGUAL en todos los slides (welcome y banner) — ver render de `Dashboard`. */
const BRAND_GRADIENT = "linear-gradient(120deg,#00dbd5 0%,#557eff 100%)";

type WelcomeSlide = {
  type: "welcome";
  title: string;
  body: string;
};

type BannerSlide = {
  type: "banner";
  id: string;
  name: string;
  imageUrl: string;
  linkUrl: string | null;
};

type Slide = WelcomeSlide | BannerSlide;

function buildSlides(displayName: string, welcomeBody: string, banners: ActiveBanner[]): Slide[] {
  const welcome: WelcomeSlide = {
    type: "welcome",
    title: `Hola, ${displayName} 👋`,
    body: welcomeBody,
  };

  const bannerSlides: BannerSlide[] = banners.map((banner) => ({
    type: "banner",
    id: banner.id,
    name: banner.name,
    imageUrl: bannerImageUrl(banner.id),
    linkUrl: banner.linkUrl,
  }));

  return [welcome, ...bannerSlides];
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

  // Banner carousel
  const [slide, setSlide] = useState(0);

  // Banners Activos (HU #12242, AC1). Un fallo de red ya degrada en silencio dentro del hook
  // (AC3); aquí solo se filtran, además, los banners cuya imagen falló al cargar (`onError` del
  // <img>) para que ese slide puntual desaparezca sin romper el resto del carrusel.
  const activeBanners = useActiveBanners();
  const [failedBannerIds, setFailedBannerIds] = useState<Set<string>>(new Set());
  const visibleBanners = useMemo(
    () => activeBanners.filter((banner) => !failedBannerIds.has(banner.id)),
    [activeBanners, failedBannerIds],
  );
  const markBannerFailed = useCallback((id: string) => {
    setFailedBannerIds((prev) => (prev.has(id) ? prev : new Set(prev).add(id)));
  }, []);

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
  const slides = useMemo(
    () => buildSlides(displayName, welcomeBody, visibleBanners),
    [displayName, welcomeBody, visibleBanners],
  );
  useEffect(() => {
    const id = setInterval(() => setSlide((s) => (s + 1) % slides.length), 6000);
    return () => clearInterval(id);
  }, [slides.length]);
  const matriculas = categories.find((c) => c.category === "matriculas")?.total ?? 0;
  const traspasos = categories.find((c) => c.category === "traspasos")?.total ?? 0;
  const otros = categories.find((c) => c.category === "otros")?.total ?? 0;
  const completados = countCompleted(categories);

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

  // Defensivo: si un banner falla después de posicionar el índice en él (p. ej. `onError` de la
  // última imagen visible), `slides` puede encoger antes de que el índice se reacomode.
  const s = slides[slide] ?? slides[0];
  const bannerColor = useDominantColor(s.type === "banner" ? s.imageUrl : undefined);

  return (
    <div className="app-bg min-h-screen px-6 pt-6 pb-10 flex flex-col gap-4 text-[#162744] dark:text-white">
      {/* Fila superior: Banner + KPIs */}
      <div className="grid grid-cols-1 md:grid-cols-3 gap-3 shrink-0">
        {/* Banner carousel */}
        <div
          className="relative md:col-span-2 rounded-2xl text-white overflow-hidden flex flex-col justify-between"
          style={{ minHeight: "220px" }}
        >
          {/* Capa de fondo: gradiente de marca fijo en el slide de bienvenida; en un banner, el
              color PROMEDIO de esa misma imagen (así combina con cualquier banner, no solo con
              el azul/turquesa de marca) — es el respaldo que se ve cuando `object-contain` deja
              margen (abajo de `md`, ver siguiente bloque); en `md+` es invisible, cubierto por el
              banner a pantalla completa. */}
          <div
            className="absolute inset-0"
            style={{ background: s.type === "welcome" ? BRAND_GRADIENT : bannerAmbientGradient(bannerColor) }}
          />
          {s.type !== "welcome" && (
            // AC1/AC2 — ajuste adaptable, mismo criterio que Spotify/YouTube/Amazon: con espacio
            // de sobra (`md:` en adelante, banner a 2/3 de ancho junto a los KPIs) se ajusta
            // completo al contenedor (object-cover, sesgado a la derecha para no cortar el
            // texto); en pantallas angostas (abajo de `md`, el banner pasa a ancho completo y el
            // recorte horizontal sería mucho más agresivo) se ve la imagen COMPLETA sin recortar
            // (object-contain) sobre el gradiente de fondo.
            <img
              src={s.imageUrl}
              alt={s.name}
              onError={() => markBannerFailed(s.id)}
              className="absolute inset-0 h-full w-full object-contain object-center md:object-cover md:object-[80%_center]"
            />
          )}
          {/* Velo para que los controles (puntos/flechas) mantengan contraste sobre cualquier
              imagen de banner; sobre el gradiente del slide fijo es imperceptible. */}
          <div
            className="absolute inset-0"
            style={{ background: "linear-gradient(180deg, rgba(0,0,0,0) 45%, rgba(0,0,0,0.35) 100%)" }}
          />
          {s.type === "welcome" && (
            <div className="absolute -right-10 -top-10 h-36 w-36 rounded-full opacity-15 bg-white" />
          )}
          {s.type === "welcome" ? (
            <div className="relative flex flex-col gap-3 max-w-[85%] px-6 pt-5">
              <div className="flex items-center gap-3">
                <div
                  className="h-10 w-10 rounded-xl grid place-items-center shrink-0"
                  style={{ background: "rgba(255,255,255,0.18)" }}
                >
                  <Activity className="h-5 w-5" />
                </div>
                <h2 className="text-2xl md:text-3xl font-bold leading-tight">{s.title}</h2>
              </div>
              <p className="text-sm md:text-base opacity-95 leading-snug line-clamp-3">{s.body}</p>
            </div>
          ) : s.linkUrl ? (
            // Sin título visible (solo el banner): el enlace cubre toda la imagen — al acercarse,
            // se opaca un poco y aparece el ícono de enlace; clic en cualquier punto abre el
            // enlace. El nombre sigue siendo el nombre accesible (aria-label), no texto en pantalla.
            <a
              href={s.linkUrl}
              target="_blank"
              rel="noopener noreferrer"
              aria-label={s.name}
              className="group absolute inset-0"
            >
              <span className="absolute inset-0 bg-black/0 transition-colors duration-200 group-hover:bg-black/25" />
              <span className="absolute inset-0 flex items-center justify-center opacity-0 transition-opacity duration-200 group-hover:opacity-100">
                <span className="flex h-10 w-10 items-center justify-center rounded-full bg-white/90 text-[#162744] shadow-lg">
                  <ExternalLink className="h-5 w-5" aria-hidden="true" />
                </span>
              </span>
            </a>
          ) : (
            <span className="sr-only">{s.name}</span>
          )}
          {/* Fijos al fondo del contenedor (`absolute inset-x-0 bottom-0`), no distribuidos por
              flex: con `flex flex-col justify-between` estos controles eran el único hijo "en
              flujo" cuando el slide es un banner (el enlace de arriba es `absolute inset-0`, o no
              hay nada más que un `sr-only`), así que `justify-between` los anclaba arriba en vez
              de abajo (Bug #12584, defecto 2). Al posicionarlos de forma absoluta dejan de
              depender del alto del contenido vecino. */}
          <div className="absolute inset-x-0 bottom-0 flex items-center justify-between px-6 pb-5">
            <div className="flex gap-1">
              {slides.map((_, i) => (
                <button
                  key={i}
                  onClick={() => setSlide(i)}
                  aria-label={`Slide ${i + 1}`}
                  className="h-1.5 rounded-full transition-all"
                  style={{
                    width: i === slide ? 16 : 5,
                    background: i === slide ? "#ffffff" : "rgba(255,255,255,0.5)",
                  }}
                />
              ))}
            </div>
            <div className="flex items-center gap-1">
              <button
                onClick={() => setSlide((v) => (v - 1 + slides.length) % slides.length)}
                disabled={slides.length <= 1}
                aria-label="Anterior"
                className="h-6 w-6 rounded-full grid place-items-center bg-white/15 hover:bg-white/25 disabled:cursor-not-allowed disabled:opacity-40 disabled:hover:bg-white/15"
              >
                <ChevronLeft className="h-3 w-3" />
              </button>
              <button
                onClick={() => setSlide((v) => (v + 1) % slides.length)}
                disabled={slides.length <= 1}
                aria-label="Siguiente"
                className="h-6 w-6 rounded-full grid place-items-center bg-white/15 hover:bg-white/25 disabled:cursor-not-allowed disabled:opacity-40 disabled:hover:bg-white/15"
              >
                <ChevronRight className="h-3 w-3" />
              </button>
            </div>
          </div>
        </div>

        {/* KPIs 2×2 (con filtro de fechas y selector de compañía para SuperAdmin encima) */}
        <div className="flex flex-col gap-3">
          <div className="flex flex-col gap-2">
            <DateRangeFilter
              value={range}
              onChange={setRange}
              disabled={status === "loading"}
              permiteSinRango
            />
            {isSuper && (
              <CompanySelector
                companies={companies}
                value={tenantId}
                onChange={setTenantId}
                disabled={status === "loading"}
                defaultLabel="Todas las compañías"
              />
            )}
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
          </div>
          {/* KPIs de Trámites — solo visibles si el módulo está habilitado para el tenant
              (AC1: `true` por defecto mientras carga, evita ocultar la sección con parpadeo). */}
          {tramitesModuleEnabled !== false && (
            <div className="grid grid-cols-3 gap-3 flex-1">
              {[
                { label: "Total Trámites", value: totalTramites, icon: FileText, color: "#557EFF" },
                { label: "Matrículas", value: matriculas, icon: Car, color: "#00DBD5" },
                { label: "Traspasos", value: traspasos, icon: Activity, color: "#F9AC00" },
                { label: "Otros Trámites", value: otros, icon: Layers, color: "#162744" },
                { label: "Completados", value: completados, icon: CheckCircle, color: "#8CC63F" },
              ].map((k) => {
                const Icon = k.icon;
                const isError = status === "error";
                return (
                  // El título ocupa la fila completa y el icono baja a la del número. Antes
                  // compartía fila con el icono dentro de un `min-w-0` con `truncate`, así que solo
                  // disponía de `ancho − 48px` y con la rejilla en 3 columnas los rótulos largos se
                  // cortaban en pantalla («Total Trá…», «Otros Trá…», «Completa…»). Recuperados esos
                  // 48px, el rótulo más largo cabe y el tamaño sube al piso tipográfico de 12px.
                  <div
                    key={k.label}
                    className="rounded-2xl p-3 flex flex-col gap-2 bg-white dark:bg-[#0B0F14] border border-[#DFE5ED] dark:border-white/10"
                  >
                    <div className="min-w-0">
                      {/* line-clamp-2 en vez de truncate: si algún día entra un rótulo más largo,
                          se parte en dos líneas en vez de perder texto por el borde. */}
                      <p className="text-xs opacity-70 font-medium leading-tight line-clamp-2">
                        {k.label}
                      </p>
                      {/* AC1 — cada indicador dice que es de la red (texto, no solo color). */}
                      {networkActive && (
                        <NetworkScopeBadge
                          scope={net.scope}
                          hijos={net.children}
                          className="mt-1"
                          testId={`kpi-red-${k.label}`}
                        />
                      )}
                    </div>
                    {/* `items-end`: la cifra y el icono se alinean por su base, no por su centro —
                        con alturas tan distintas (24px vs 36px) centrarlos descuadraba la fila. */}
                    <div className="flex items-end justify-between gap-2">
                      {isError ? (
                        <p
                          className="text-xl font-bold flex items-center gap-1.5 min-w-0"
                          style={{ color: "#FF4E00" }}
                          title={errorMessage ?? "No se pudo cargar este indicador."}
                        >
                          <AlertTriangle className="h-4 w-4 shrink-0" aria-hidden="true" />
                          <span>—</span>
                          <span className="sr-only">Error al cargar {k.label.toLowerCase()}</span>
                        </p>
                      ) : (
                        // `tabular-nums` y sin truncar: recortar un conteo mostraría una cifra
                        // falsa. Mismo criterio que la tira de contadores del OT.
                        <p className="text-2xl font-bold leading-none tabular-nums" style={{ color: k.color }}>
                          {status === "loading" ? "—" : k.value}
                        </p>
                      )}
                      <div
                        className="h-9 w-9 rounded-xl grid place-items-center shrink-0"
                        style={{ background: isError ? "#FF4E001A" : `${k.color}1A` }}
                      >
                        <Icon className="h-4 w-4" style={{ color: isError ? "#FF4E00" : k.color }} />
                      </div>
                    </div>
                  </div>
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
                className="rounded-2xl p-4 flex items-center gap-3 bg-white dark:bg-[#0B0F14] border border-[#DFE5ED] dark:border-white/10"
              >
                <div
                  className="h-11 w-11 rounded-xl grid place-items-center shrink-0"
                  style={{ background: "#7D87981A" }}
                >
                  <Clock className="h-5 w-5" style={{ color: "#7D8798" }} aria-hidden="true" />
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
              <section className="rounded-2xl p-4 bg-white dark:bg-[#0B0F14] border border-[#DFE5ED] dark:border-white/10 flex flex-col">
                <h2 className="text-sm font-bold mb-3 flex flex-wrap items-center gap-2">
                  Distribución General de Trámites
                  {networkActive && <NetworkScopeBadge scope={net.scope} hijos={net.children} testId="distribucion-red" />}
                </h2>
                {globalFunnel.length === 0 ? (
                  <p className="text-xs opacity-50 mt-2">Sin trámites en el rango seleccionado.</p>
                ) : (
                  <ul className="space-y-2">
                    {globalFunnel.map((f, i) => {
                      const color = estadoChipStyle(f.status).accent;
                      return (
                        <li
                          key={f.status}
                          className="flex items-center gap-3 p-2 rounded-xl bg-[rgba(85,126,255,0.06)] dark:bg-white/5"
                        >
                          <span
                            className="h-7 w-7 rounded-full grid place-items-center text-[11px] font-bold text-white shrink-0"
                            style={{ background: color }}
                          >
                            {i + 1}
                          </span>
                          <span className="flex-1 text-xs font-medium">
                            {estadoLabel(f.status)}
                          </span>
                          <span className="text-base font-bold" style={{ color }}>
                            {f.count}
                          </span>
                        </li>
                      );
                    })}
                  </ul>
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
            <section className="rounded-2xl p-4 bg-white dark:bg-[#0B0F14] border border-[#DFE5ED] dark:border-white/10 flex flex-col">
              <h2 className="text-sm font-bold mb-3 flex flex-wrap items-center gap-2">
                Validaciones Biométricas
                {/* Sin ruta de red para biometría: sigue siendo del cliente propio y se rotula
                    para que no se lea como agregado de la red (AC1). */}
                {networkActive && (
                  <span className="text-[11px] font-medium opacity-70" data-testid="biometria-solo-propia">
                    {ETIQUETA_SOLO_COMPANIA_PROPIA}
                  </span>
                )}
              </h2>
              <div className="grid grid-cols-2 gap-2">
                {[
                  { label: "Total", value: biometricStats?.total ?? 0, color: "#557EFF" },
                  { label: "Aprobadas", value: biometricStats?.aprobadas ?? 0, color: "#8CC63F" },
                  { label: "En proceso", value: biometricStats?.enProceso ?? 0, color: "#F9AC00" },
                  { label: "Rechazadas", value: biometricStats?.rechazadas ?? 0, color: "#FF4E00" },
                ].map((k) => (
                  <div key={k.label} className="rounded-xl border p-2.5">
                    <p className="text-[10px] opacity-70 font-medium">{k.label}</p>
                    <p className="text-lg font-bold mt-0.5" style={{ color: k.color }}>
                      {k.value}
                    </p>
                  </div>
                ))}
              </div>
              {expiringSoonCount > 0 && (
                <div
                  className="mt-3 flex items-center gap-2 rounded-xl px-3 py-2 text-xs font-medium"
                  style={{ background: "#F9AC001A", color: "#B26A00" }}
                >
                  <AlertTriangle className="h-3.5 w-3.5 shrink-0" />
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
            <section className="rounded-2xl p-4 bg-white dark:bg-[#0B0F14] border border-[#DFE5ED] dark:border-white/10 flex flex-col">
              <h2 className="text-sm font-bold mb-3 flex flex-wrap items-center gap-2">
                Seguimiento operativo
                {networkActive && <NetworkScopeBadge scope={net.scope} hijos={net.children} testId="seguimiento-red" />}
              </h2>
              <div className="h-[280px] -mx-2">
                <ResponsiveContainer width="100%" height="100%">
                  <BarChart data={chartData} margin={{ top: 4, right: 8, left: -20, bottom: 0 }}>
                    <CartesianGrid
                      strokeDasharray="3 3"
                      stroke="rgba(127,127,127,0.18)"
                      vertical={false}
                    />
                    <XAxis
                      dataKey="m"
                      tick={{ fontSize: 10, fill: "currentColor" }}
                      axisLine={false}
                      tickLine={false}
                    />
                    <YAxis
                      tick={{ fontSize: 10, fill: "currentColor" }}
                      axisLine={false}
                      tickLine={false}
                    />
                    <Tooltip
                      cursor={{ fill: "rgba(85,126,255,0.08)" }}
                      contentStyle={{
                        background: "rgba(22,39,68,0.95)",
                        border: "none",
                        borderRadius: 10,
                        color: "#fff",
                        fontSize: 11,
                      }}
                      labelStyle={{ color: "#00DBD5", fontWeight: 600 }}
                    />
                    <Bar dataKey="Matrículas" fill="#557EFF" radius={[4, 4, 0, 0]} />
                    <Bar dataKey="Traspasos" fill="#00DBD5" radius={[4, 4, 0, 0]} />
                    <Bar dataKey="Otros" fill="#F9AC00" radius={[4, 4, 0, 0]} />
                  </BarChart>
                </ResponsiveContainer>
              </div>
              <div className="flex items-center justify-center gap-3 mt-1 text-[9px]">
                {(
                  [
                    { color: "#557EFF", label: "Matrículas" },
                    { color: "#00DBD5", label: "Traspasos" },
                    { color: "#F9AC00", label: "Otros" },
                  ] as const
                ).map(({ color, label }) => (
                  <span key={label} className="flex items-center gap-1">
                    <span className="h-2 w-2 rounded-full" style={{ background: color }} />
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
