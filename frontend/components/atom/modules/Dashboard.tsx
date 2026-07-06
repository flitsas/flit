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
  Sparkles,
  ShieldCheck,
  FileText,
  CheckCircle,
  Car,
} from "lucide-react";
import { UiStateBoundary, type UiStatus } from "@/components/admin/UiStateBoundary";
import { fetchAnalyticsOverview, fetchMonthlyTrend } from "@/lib/api/analytics";
import { fetchCompaniesIndex } from "@/lib/api/admin-companies";
import { tramitesClient } from "@/lib/api/tramites-client";
import { getToken } from "@/lib/api/client";
import { decodeJwtPayload, isSuperAdmin } from "@/lib/auth/jwt";
import { CompanySelector } from "./_reportes/CompanySelector";
import { DateRangeFilter } from "./_reportes/DateRangeFilter";
import { defaultRange, isValidRange, type DateRange } from "./_reportes/range";
import { ApiError } from "@/lib/api/types";
import type {
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
    (sum, cat) => sum + (cat.byStatus.find((s) => s.status === "completed")?.count ?? 0),
    0,
  );
}

const STATUS_LABELS: Record<string, string> = {
  draft: "Borrador",
  submitted: "Enviado",
  in_review: "En revisión",
  pending_ot: "Pendiente OT",
  approved_ot: "Aprobado OT",
  completed: "Completado",
  cancelled: "Cancelado",
  rejected_ot: "Rechazado",
};

const STATUS_COLORS: Record<string, string> = {
  draft: "#557EFF",
  submitted: "#00DBD5",
  in_review: "#F9AC00",
  pending_ot: "#8CC63F",
  approved_ot: "#00DBD5",
  completed: "#8CC63F",
  cancelled: "#FF4E00",
  rejected_ot: "#FF4E00",
};

function describeError(error: unknown): string {
  if (error instanceof ApiError) {
    if (error.status === 403) return "No tienes acceso a las métricas de esa compañía.";
    if (error.status === 401) return "Tu sesión expiró. Vuelve a iniciar sesión.";
  }
  return "No se pudieron cargar las métricas del dashboard.";
}

// ── Slides del banner ─────────────────────────────────────────────────────────

function buildSlides(displayName: string) {
  return [
    {
      type: "welcome" as const,
      title: `Hola, ${displayName} 👋`,
      body: "Tus procesos y validaciones se encuentran sincronizados. Continúa gestionando tu operación de manera segura y eficiente.",
      bg: "linear-gradient(120deg,#00dbd5 0%,#557eff 100%)",
    },
    {
      type: "news" as const,
      title: "Nueva integración disponible",
      body: "Sistema de validación de identidad con Inteligencia Artificial ya integrado en tus trámites.",
      bg: "linear-gradient(120deg,#16a34a 0%,#22c55e 100%)",
    },
    {
      type: "info" as const,
      title: "Nuevas novedades de la plataforma",
      body: "Hemos publicado mejoras en validación RUNT, reportes ejecutivos y trazabilidad de firmas digitales.",
      bg: "#557eff",
    },
  ] as const;
}

// ── Componente principal ──────────────────────────────────────────────────────

// eslint-disable-next-line @typescript-eslint/no-unused-vars
export function Dashboard({ onNewTramite: _onNewTramite }: { onNewTramite: () => void }) {
  // Identidad del usuario
  const [displayName, setDisplayName] = useState("—");
  const [isSuper, setIsSuper] = useState(false);

  // Selector de compañía (solo SuperAdmin)
  const [companies, setCompanies] = useState<CompanyListItem[]>([]);
  const [tenantId, setTenantId] = useState("");

  // Rango de fechas de las métricas (KPIs, distribución general, validaciones biométricas) — visible a todos los roles.
  const [range, setRange] = useState<DateRange>(() => defaultRange());

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

  // Banner carousel
  const [slide, setSlide] = useState(0);

  // Leer identidad del JWT en cliente (el token solo existe en cliente tras el montaje).
  useEffect(() => {
    const payload = decodeJwtPayload(getToken());
    const name = (payload?.display_name as string | undefined) ?? (payload?.email as string | undefined) ?? "—";
    // eslint-disable-next-line react-hooks/set-state-in-effect
    setDisplayName(name);
    setIsSuper(isSuperAdmin(payload));
  }, []);

  // Cargar catálogo de compañías para el selector SuperAdmin
  useEffect(() => {
    if (!isSuper) return;
    const controller = new AbortController();
    fetchCompaniesIndex({ pageSize: 100, estadoActivo: true }, controller.signal)
      .then((res) => {
        if (!controller.signal.aborted) setCompanies(res.data);
      })
      .catch(() => { /* silencioso: el selector queda vacío, el dashboard sigue operativo */ });
    return () => controller.abort();
  }, [isSuper]);

  // Auto-avance del carrusel
  const slides = useMemo(() => buildSlides(displayName), [displayName]);
  useEffect(() => {
    const id = setInterval(() => setSlide((s) => (s + 1) % slides.length), 6000);
    return () => clearInterval(id);
  }, [slides.length]);

  // Cargar datos reales (mes actual + últimos 6 meses para el gráfico)
  useEffect(() => {
    const controller = new AbortController();

    async function load() {
      setStatus("loading");

      if (!isValidRange(range)) {
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
        const [overviewRes, trendRes] = await Promise.all([
          fetchAnalyticsOverview({ from: monthRange.from, to: monthRange.to, tenantId: tid }, controller.signal),
          fetchMonthlyTrend({ from: trendRange.from, to: trendRange.to, tenantId: tid }, controller.signal),
        ]);
        if (controller.signal.aborted) return;
        setOverview(overviewRes);
        setTrend(trendRes.items);
        setStatus("ready");
      } catch (err) {
        if (controller.signal.aborted || (err as Error).name === "AbortError") return;
        setErrorMessage(describeError(err));
        setStatus("error");
      }
    }

    void load();
    return () => controller.abort();
  }, [range, tenantId, reloadKey]);

  // Cargar KPIs de validaciones biométricas (card "Validaciones Biométricas"), independiente
  // del overview de analytics.
  useEffect(() => {
    const controller = new AbortController();

    async function loadBiometrics() {
      // El endpoint /biometric-validations NO tiene vista global (a diferencia de /analytics):
      // exige un tenant concreto vía header. Un SuperAdmin en "Todas las compañías" debe ver un
      // aviso, no datos silenciosamente equivocados (el tenant del propio JWT del SuperAdmin).
      if (isSuper && !tenantId) {
        setBiometricStatus("empty");
        return;
      }
      setBiometricStatus("loading");

      if (!isValidRange(range)) {
        setBiometricErrorMessage("La fecha inicial no puede ser posterior a la fecha final.");
        setBiometricStatus("error");
        return;
      }

      const createdFrom = `${range.from}T00:00:00`;
      const createdTo = `${range.to}T23:59:59`;

      try {
        const [statsRes, expiringRes] = await Promise.all([
          tramitesClient.listTenantBiometricValidations({ createdFrom, createdTo, pageSize: 10 }, tenantId || undefined),
          tramitesClient.listTenantBiometricValidations({ vigenciaEstado: "por_vencer", pageSize: 10 }, tenantId || undefined),
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
  }, [range, tenantId, isSuper, reloadKey]);

  const retry = useCallback(() => setReloadKey((k) => k + 1), []);

  // Derivar métricas del overview
  const categories = useMemo(() => overview?.categories ?? [], [overview]);
  const totalTramites = categories.reduce((sum, c) => sum + c.total, 0);
  const matriculas = categories.find((c) => c.category === "matriculas")?.total ?? 0;
  const traspasos = categories.find((c) => c.category === "traspasos")?.total ?? 0;
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

  const s = slides[slide];

  return (
    <div className="app-bg min-h-screen px-6 pt-6 pb-10 flex flex-col gap-4 text-[#162744] dark:text-white">
      {/* Fila superior: Banner + KPIs */}
      <div className="grid grid-cols-1 md:grid-cols-3 gap-3 shrink-0">
        {/* Banner carousel */}
        <div
          className="relative md:col-span-2 rounded-2xl px-6 py-5 text-white overflow-hidden flex flex-col justify-between"
          style={{ background: s.bg, minHeight: "220px" }}
        >
          <div className="absolute -right-10 -top-10 h-36 w-36 rounded-full opacity-15 bg-white" />
          <div className="relative flex flex-col gap-3 max-w-[85%]">
            <div className="flex items-center gap-3">
              <div
                className="h-10 w-10 rounded-xl grid place-items-center shrink-0"
                style={{ background: "rgba(255,255,255,0.18)" }}
              >
                {s.type === "welcome" ? (
                  <Activity className="h-5 w-5" />
                ) : s.type === "news" ? (
                  <Sparkles className="h-5 w-5" />
                ) : (
                  <ShieldCheck className="h-5 w-5" />
                )}
              </div>
              <h2 className="text-2xl md:text-3xl font-bold leading-tight">{s.title}</h2>
            </div>
            <p className="text-sm md:text-base opacity-95 leading-snug line-clamp-3">{s.body}</p>
          </div>
          <div className="flex items-center justify-between mt-3 relative">
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
                aria-label="Anterior"
                className="h-6 w-6 rounded-full grid place-items-center bg-white/15 hover:bg-white/25"
              >
                <ChevronLeft className="h-3 w-3" />
              </button>
              <button
                onClick={() => setSlide((v) => (v + 1) % slides.length)}
                aria-label="Siguiente"
                className="h-6 w-6 rounded-full grid place-items-center bg-white/15 hover:bg-white/25"
              >
                <ChevronRight className="h-3 w-3" />
              </button>
            </div>
          </div>
        </div>

        {/* KPIs 2×2 (con filtro de fechas y selector de compañía para SuperAdmin encima) */}
        <div className="flex flex-col gap-3">
          <div className="flex flex-col gap-2">
            <DateRangeFilter value={range} onChange={setRange} disabled={status === "loading"} />
            {isSuper && (
              <CompanySelector
                companies={companies}
                value={tenantId}
                onChange={setTenantId}
                disabled={status === "loading"}
                defaultLabel="Todas las compañías"
              />
            )}
          </div>
          <div className="grid grid-cols-2 gap-3 flex-1">
            {[
              { label: "Total Trámites", value: totalTramites, icon: FileText, color: "#557EFF" },
              { label: "Matrículas", value: matriculas, icon: Car, color: "#00DBD5" },
              { label: "Traspasos", value: traspasos, icon: Activity, color: "#F9AC00" },
              { label: "Completados", value: completados, icon: CheckCircle, color: "#8CC63F" },
            ].map((k) => {
              const Icon = k.icon;
              const isError = status === "error";
              return (
                <div
                  key={k.label}
                  className="rounded-2xl p-4 flex items-center justify-between bg-white dark:bg-[#0B0F14] border border-[#DFE5ED] dark:border-white/10"
                >
                  <div>
                    <p className="text-[11px] opacity-70 font-medium">{k.label}</p>
                    {isError ? (
                      <p
                        className="text-xl font-bold mt-1 flex items-center gap-1.5"
                        style={{ color: "#FF4E00" }}
                        title={errorMessage ?? "No se pudo cargar este indicador."}
                      >
                        <AlertTriangle className="h-4 w-4 shrink-0" aria-hidden="true" />
                        <span>—</span>
                        <span className="sr-only">Error al cargar {k.label.toLowerCase()}</span>
                      </p>
                    ) : (
                      <p className="text-3xl font-bold mt-1" style={{ color: k.color }}>
                        {status === "loading" ? "—" : k.value}
                      </p>
                    )}
                  </div>
                  <div
                    className="h-11 w-11 rounded-xl grid place-items-center"
                    style={{ background: isError ? "#FF4E001A" : `${k.color}1A` }}
                  >
                    <Icon className="h-5 w-5" style={{ color: isError ? "#FF4E00" : k.color }} />
                  </div>
                </div>
              );
            })}
          </div>
        </div>
      </div>

      {/* Fila inferior: Distribución general + Validaciones Biométricas (cada una con su propio estado) + gráfica mensual (chartStatus) */}
      <div className="grid grid-cols-1 md:grid-cols-3 gap-3">
        <div className="md:col-span-2 grid grid-cols-1 md:grid-cols-2 gap-3">
          <UiStateBoundary
            status={overviewStatus}
            errorMessage={errorMessage}
            onRetry={retry}
            emptyMessage="No hay trámites para el rango seleccionado."
            skeletonRows={3}
          >
            {/* Distribución general de trámites por estado (las 4 categorías, no solo traspasos) */}
            <section className="rounded-2xl p-4 bg-white dark:bg-[#0B0F14] border border-[#DFE5ED] dark:border-white/10 flex flex-col">
              <h2 className="text-sm font-bold mb-3">Distribución General de Trámites</h2>
              {globalFunnel.length === 0 ? (
                <p className="text-xs opacity-50 mt-2">Sin trámites en el rango seleccionado.</p>
              ) : (
                <ul className="space-y-2">
                  {globalFunnel.map((f, i) => {
                    const color = STATUS_COLORS[f.status] ?? "#557EFF";
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
                          {STATUS_LABELS[f.status] ?? f.status}
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

          <UiStateBoundary
            status={biometricStatus}
            errorMessage={biometricErrorMessage}
            onRetry={retry}
            emptyMessage="Selecciona una compañía para ver sus validaciones biométricas."
            skeletonRows={3}
          >
            {/* Validaciones Biométricas: KPIs + aviso de próximas a vencer */}
            <section className="rounded-2xl p-4 bg-white dark:bg-[#0B0F14] border border-[#DFE5ED] dark:border-white/10 flex flex-col">
              <h2 className="text-sm font-bold mb-3">Validaciones Biométricas</h2>
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
        </div>

        {/* Gráfico mensual por categoría — tendencia de 6 meses, independiente del rango filtrado */}
        <UiStateBoundary
          status={chartStatus}
          errorMessage={errorMessage}
          onRetry={retry}
          emptyMessage="No hay datos de tendencia en los últimos 6 meses."
          skeletonRows={3}
        >
          <section className="rounded-2xl p-4 bg-white dark:bg-[#0B0F14] border border-[#DFE5ED] dark:border-white/10 flex flex-col">
            <h2 className="text-sm font-bold mb-3">Seguimiento operativo</h2>
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
      </div>
    </div>
  );
}
