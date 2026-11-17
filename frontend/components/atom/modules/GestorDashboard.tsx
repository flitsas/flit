"use client";

import { useCallback, useEffect, useMemo, useState, type ReactNode } from "react";
import Link from "next/link";
import { Icono } from "@flit/ui/Icono";
import { IconoAviso } from "@flit/ui/IconoAviso";
import { TarjetaMetrica } from "@flit/ui/TarjetaMetrica";
import { DataTable, type DataTableColumn } from "@flit/ui/DataTable";
import { StatusBadge } from "@flit/ui/StatusBadge";
import { Placa } from "@flit/ui/Placa";
import { buttonClass } from "@flit/ui/Button";
import { UiStateBoundary, type UiStatus } from "@/components/admin/UiStateBoundary";
import { fetchAnalyticsOverview, fetchNetworkAnalyticsOverview } from "@/lib/api/analytics";
import {
  fetchColaInactividad,
  type CategoriaGestor,
  type ColaInactividad,
  type ColaInactividadItem,
} from "@/lib/api/analytics-gestor";
import { getToken } from "@/lib/api/client";
import { TramitesApiError, tramitesClient } from "@/lib/api/tramites-client";
import { ApiError } from "@/lib/api/types";
import type { AnalyticsOverviewResponse } from "@/lib/api/types";
import { decodeJwtPayload } from "@/lib/auth/jwt";
import { useShellNav } from "@flit/shell/nav-context";
import { useActiveBanners } from "@/hooks/useActiveBanners";
import { useNetworkScope } from "@/hooks/useNetworkScope";
import { useFamilyAccess } from "@/hooks/useFamilyAccess";
import { NetworkScopeSelector } from "@/components/operacion/NetworkScopeSelector";
import { NetworkScopeBadge } from "@/components/operacion/NetworkScopeBadge";
import { EstadoFunnel } from "@/components/operacion/EstadoFunnel";
import { ETIQUETA_SOLO_COMPANIA_PROPIA } from "@/lib/tramites/network-scope";
import { estadoAcentoVar, estadoLabel, type EstadoFiltro } from "@/lib/tramites/estados";
import { RangoFechas } from "@/components/atom/RangoFechas";
import { CarruselInicio } from "@/components/inicio/CarruselInicio";
import { PanelInicio } from "@/components/inicio/PanelInicio";
import { GraficoDona } from "@/components/inicio/GraficoDona";
import { useConteosEstado } from "@/components/inicio/useConteosEstado";
import { TRAMITES_DOCK_KEY, TRAMITES_PATH, tramitesPorAtajo, tramitesPorEstado } from "@/components/inicio/destinos";
import { FILTRO_CONTROL_CLS, FILTROS_REJILLA_CLS } from "@/components/inicio/filtros-inicio";
import { isValidOptionalRange, sinRango, type DateRange } from "./_reportes/range";

/**
 * Inicio del Gestor (rediseño de los 3 dashboards). Tres franjas, con el contenido y el orden del prototipo y el
 * aspecto del UI Kit 2026:
 * 1. Carrusel de bienvenida + rango de fechas y KPIs por familia (overview, con el gate de familia y el distintivo de
 *    red de siempre).
 * 2. «Estado general de trámites»: la franja de estados de /tramites. Pulsar un estado filtra la cola de la franja 3.
 * 3. «Trámites por firma» (dona con / sin firmas: el atajo «Trámites sin firmas» de /tramites) y «Cola de gestión» (los 5
 *    trámites más quietos).
 * Biometría, «Seguimiento operativo», `InicioModulos` y las tarjetas «Próximamente» salen del Inicio.
 */

/** Hero del Inicio (A18 es N/A: no se unifica con el hero OT). */
export const DASHBOARD_HERO_PREFIX = "Hola,";

/** Estados de la franja 2, en el orden del prototipo. */
export const ESTADOS_INICIO_GESTOR: readonly EstadoFiltro[] = [
  "borrador",
  "preparado",
  "preasignacion",
  "asignado",
  "entregado",
  "subsanacion",
  "aprobado",
  "rechazado",
  "anulado",
  "revocado",
];

/** Atajo de la búsqueda rápida de /tramites que define «sin firmas» (lib/tramites/busquedaRapida.ts). */
const ATAJO_SIN_FIRMAS = "sin_firmas" as const;

/** Desde cuántos días sin gestión la inactividad se pinta en rojo. */
export const DIAS_INACTIVIDAD_CRITICA = 10;

const FAMILIAS = [
  { categoria: "matriculas", etiqueta: "Matrícula inicial", ilustracion: "matricula", family: "MATRICULAS" },
  { categoria: "traspasos", etiqueta: "Traspasos", ilustracion: "traspaso", family: "TRASPASO" },
  { categoria: "otros", etiqueta: "Otros trámites", ilustracion: "otros-tramites", family: "OTROS" },
] as const;

type FiltroTipo = CategoriaGestor | "todos";

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

const plural = (n: number, uno: string, varios: string) => (n === 1 ? uno : varios);

/** Cuerpo del slide de bienvenida con datos reales: preparados (listos para radicar) y en subsanación. */
function cuerpoBienvenida(conteos: Record<string, number> | null): ReactNode {
  if (!conteos) {
    return "Tus procesos se encuentran sincronizados. Continúa gestionando tu operación de manera segura y eficiente.";
  }
  const listos = conteos.preparado ?? 0;
  const subsanacion = conteos.subsanacion ?? 0;
  if (listos === 0 && subsanacion === 0) {
    return "No tienes trámites listos para radicar ni en subsanación. Todo al día.";
  }
  return (
    <>
      Tienes{" "}
      <strong>
        {listos} {plural(listos, "trámite listo", "trámites listos")} para radicar
      </strong>{" "}
      y {subsanacion} en subsanación.
    </>
  );
}

/** Lo que va en la cifra de un KPI: un número, o una palabra cuando no hay número que dar (nunca un guion). */
function cifraKpi(status: UiStatus, valor: number, errorMessage?: string): ReactNode {
  if (status === "loading") return <span className="text-base font-medium">Cargando…</span>;
  if (status === "error") {
    return (
      <span
        className="flex items-center gap-1.5 text-base font-medium"
        style={{ color: "var(--di-danger)" }}
        title={errorMessage ?? "No se pudo cargar este indicador."}
      >
        <IconoAviso tipo="error" className="h-4 w-4 shrink-0" />
        Sin datos
      </span>
    );
  }
  if (valor === 0) return <span className="text-base font-medium">Ninguno</span>;
  return valor;
}

export function GestorDashboard({ onNewTramite }: { onNewTramite: () => void }) {
  // Lo recibe de page.tsx; el Inicio del Gestor ya no pinta el CTA (crear trámite vive en /tramites).
  void onNewTramite;

  const [displayName, setDisplayName] = useState("—");
  const [range, setRange] = useState<DateRange>(() => sinRango()); // BUG #12588 — arranca SIN rango.
  const [estado, setEstado] = useState<EstadoFiltro | "">("");
  const [tipo, setTipo] = useState<FiltroTipo>("todos");
  const [reloadKey, setReloadKey] = useState(0);
  const retry = useCallback(() => setReloadKey((k) => k + 1), []);

  const net = useNetworkScope();
  const { networkActive, ready: networkReady } = net;
  const networkChildTenantId = net.scope.childTenantId;
  const { showsInAggregates } = useFamilyAccess();
  const activeBanners = useActiveBanners();
  // Gate del dock (como el InicioModulos retirado): sin Trámites en el dock no se piden conteos, firmas ni cola, y las
  // franjas 2 y 3 no se pintan; quedan el carrusel y los KPIs.
  const { groups } = useShellNav();
  const veTramites = useMemo(
    () => groups.some((g) => g.items.some((e) => e.key === TRAMITES_DOCK_KEY)),
    [groups],
  );
  const conteosEstado = useConteosEstado(veTramites);
  const conteos = conteosEstado.estado === "listo" ? conteosEstado.conteos : null;

  const [overview, setOverview] = useState<AnalyticsOverviewResponse | null>(null);
  const [overviewStatus, setOverviewStatus] = useState<UiStatus>("loading");
  const [overviewError, setOverviewError] = useState<string>();

  const [firma, setFirma] = useState<{ conFirmas: number; sinFirmas: number } | null>(null);
  const [firmaStatus, setFirmaStatus] = useState<UiStatus | "demasiados">("loading");
  const [firmaError, setFirmaError] = useState<string>();

  const [cola, setCola] = useState<ColaInactividad | null>(null);
  const [colaStatus, setColaStatus] = useState<UiStatus>("loading");
  const [colaError, setColaError] = useState<string>();

  // El token solo existe en cliente tras el montaje.
  useEffect(() => {
    const payload = decodeJwtPayload(getToken());
    // eslint-disable-next-line react-hooks/set-state-in-effect
    setDisplayName((payload?.display_name as string | undefined) ?? (payload?.email as string | undefined) ?? "—");
  }, []);

  // Franja 1 — KPIs por familia. Con la red activa, el overview de la red (HU #12364), como el Inicio de siempre.
  useEffect(() => {
    const controller = new AbortController();
    async function load() {
      setOverviewStatus("loading");
      if (!isValidOptionalRange(range)) {
        setOverviewError("La fecha inicial no puede ser posterior a la fecha final.");
        setOverviewStatus("error");
        return;
      }
      try {
        const res = networkActive
          ? await fetchNetworkAnalyticsOverview(
              { from: range.from, to: range.to, childTenantId: networkChildTenantId },
              controller.signal,
            )
          : await fetchAnalyticsOverview({ from: range.from, to: range.to }, controller.signal);
        if (controller.signal.aborted) return;
        setOverview(res);
        setOverviewStatus("ready");
      } catch (err) {
        if (controller.signal.aborted || (err as Error).name === "AbortError") return;
        setOverviewError(describeError(err, networkActive));
        setOverviewStatus("error");
      }
    }
    // La cabeza espera a conocer su alcance guardado para no pedir «lo propio» y luego «la red».
    if (!networkReady) return () => controller.abort();
    void load();
    return () => controller.abort();
  }, [range, reloadKey, networkActive, networkChildTenantId, networkReady]);

  // Franja 3 — trámites por firma. Es EXACTAMENTE el atajo «Trámites sin firmas» de /tramites (busquedaRapida=sin_firmas,
  // la misma regla del servidor: identidad validada, firma de baúl vigente, representante legal): «sin firmas» = el total
  // de ese listado; «con firmas» = los borradores del mismo periodo menos esos. Sigue al rango, como los KPIs.
  useEffect(() => {
    const controller = new AbortController();
    async function load() {
      if (!veTramites) return;
      setFirmaStatus("loading");
      if (!isValidOptionalRange(range)) {
        setFirmaError("La fecha inicial no puede ser posterior a la fecha final.");
        setFirmaStatus("error");
        return;
      }
      const fechas = {
        createdFrom: range.from ? `${range.from}T00:00:00` : undefined,
        createdTo: range.to ? `${range.to}T23:59:59` : undefined,
      };
      try {
        const [conteos, sinFirmas] = await Promise.all([
          tramitesClient.searchEstadoCounts(fechas),
          tramitesClient.searchInstances({
            ...fechas,
            estado: "borrador",
            busquedaRapida: ATAJO_SIN_FIRMAS,
            skip: 0,
            take: 1,
          }),
        ]);
        if (controller.signal.aborted) return;
        // El cliente de conteos devuelve {} si falla: eso es «sin datos», no «cero borradores».
        if (Object.keys(conteos).length === 0) {
          setFirmaError(describeError(null));
          setFirmaStatus("error");
          return;
        }
        const borradores = conteos.borrador ?? 0;
        const sin = Math.min(sinFirmas.total, borradores);
        setFirma({ conFirmas: borradores - sin, sinFirmas: sin });
        setFirmaStatus(borradores > 0 ? "ready" : "empty");
      } catch (err) {
        if (controller.signal.aborted) return;
        // 422: más borradores que el tope del atajo. Se dice en palabras, sin inventar cifras.
        if (err instanceof TramitesApiError && err.status === 422) {
          setFirma(null);
          setFirmaStatus("demasiados");
          return;
        }
        setFirmaError(describeError(err));
        setFirmaStatus("error");
      }
    }
    void load();
    return () => controller.abort();
  }, [range, reloadKey, veTramites]);

  // Franja 3 — cola de gestión: la filtran el estado elegido en la franja 2 y el selector de tipo.
  useEffect(() => {
    const controller = new AbortController();
    async function load() {
      if (!veTramites) return;
      setColaStatus("loading");
      try {
        const res = await fetchColaInactividad(
          { estado: estado || undefined, familia: tipo === "todos" ? undefined : tipo, limit: 5 },
          controller.signal,
        );
        if (controller.signal.aborted) return;
        setCola(res);
        setColaStatus(res.items.length > 0 ? "ready" : "empty");
      } catch (err) {
        if (controller.signal.aborted || (err as Error).name === "AbortError") return;
        setColaError(describeError(err));
        setColaStatus("error");
      }
    }
    void load();
    return () => controller.abort();
  }, [estado, tipo, reloadKey, veTramites]);

  const bienvenida = useMemo(
    () => [
      {
        antetitulo: "Tu operación, sin pausas",
        titulo: `${DASHBOARD_HERO_PREFIX} ${displayName}`,
        cuerpo: cuerpoBienvenida(conteos),
      },
    ],
    [displayName, conteos],
  );

  const familiasVisibles = FAMILIAS.filter((f) => showsInAggregates(f.family));
  const totalPorCategoria = (categoria: string) =>
    overview?.categories.find((c) => c.category === categoria)?.total ?? 0;

  const conteosFranja = useMemo(() => {
    const out: Partial<Record<EstadoFiltro, number>> = {};
    for (const e of ESTADOS_INICIO_GESTOR) out[e] = conteos?.[e] ?? 0;
    return out;
  }, [conteos]);
  const totalTramites = ESTADOS_INICIO_GESTOR.reduce((s, e) => s + (conteosFranja[e] ?? 0), 0);

  const etiquetaPropia = networkActive ? (
    <span className="text-[11px] font-medium opacity-70" data-testid="inicio-solo-propia">
      {ETIQUETA_SOLO_COMPANIA_PROPIA}
    </span>
  ) : null;

  const columnasCola: DataTableColumn<ColaInactividadItem>[] = [
    {
      key: "radicado",
      header: "Radicado",
      render: (r) => <span className="font-semibold">{r.referencia ?? "Sin radicado"}</span>,
    },
    {
      key: "vehiculo",
      header: "Placa/VIN",
      render: (r) =>
        r.placa ? (
          <Placa placa={r.placa} />
        ) : (
          <span className="font-mono text-xs">{r.vin ?? "Sin placa ni VIN"}</span>
        ),
    },
    {
      key: "inactividad",
      header: "Inactividad",
      render: (r) => (
        <StatusBadge
          tone={r.diasSinMovimiento >= DIAS_INACTIVIDAD_CRITICA ? "danger" : "warning"}
          label={`${r.diasSinMovimiento} ${plural(r.diasSinMovimiento, "día", "días")} sin gestión`}
        />
      ),
    },
    {
      key: "accion",
      header: <span className="sr-only">Acción</span>,
      align: "right",
      render: (r) => (
        <Link
          href={`${TRAMITES_PATH}/${r.id}`}
          className={buttonClass({ variant: "ghost", size: "sm" })}
          aria-label={`Gestionar ${r.referencia ?? "trámite"}`}
        >
          Gestionar
        </Link>
      ),
    },
  ];

  const filtroCola = [
    estado ? estadoLabel(estado) : null,
    tipo !== "todos" ? FAMILIAS.find((f) => f.categoria === tipo)?.etiqueta : null,
  ]
    .filter(Boolean)
    .join(" · ");

  return (
    <div className="app-bg min-h-screen px-6 pt-6 pb-10 max-sm:px-4 flex flex-col gap-4 text-foreground">
      {/* Franja 1 — carrusel a la izquierda; filtros y KPIs a la derecha. */}
      <div className="grid grid-cols-1 lg:grid-cols-2 gap-3 items-stretch">
        <CarruselInicio bienvenida={bienvenida} banners={activeBanners} aria-label="Novedades de tu operación" />

        <div className="flex min-w-0 flex-col gap-3" data-testid="dashboard-hero-column">
          <div
            className={`${FILTROS_REJILLA_CLS} [&>[data-testid=rango-fechas]>label:first-child]:col-span-2`}
            data-testid="dashboard-filter-row"
          >
            <RangoFechas value={range} onChange={setRange} permiteSinRango idPrefix="dashboard-rango" />
          </div>
          {/* HU #12364 — solo para una cabeza de red. */}
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
          <div className="grid grid-cols-1 sm:grid-cols-3 items-start gap-3" data-testid="dashboard-kpi-grid">
            {familiasVisibles.map((k) => (
              <TarjetaMetrica
                key={k.categoria}
                data-testid={`kpi-${k.categoria}`}
                // Columna de 1/3: el rótulo usa todo el ancho y la ilustración se fija abajo a la derecha (como el
                // Inicio de siempre), para que no se coma el ancho de la cifra.
                className="relative grid-cols-1 content-start gap-y-0.5 p-3.5 [&>:nth-child(3)]:absolute [&>:nth-child(3)]:bottom-3 [&>:nth-child(3)]:right-3 [&>:nth-child(3)]:h-10 [&>:nth-child(3)]:w-10 [&>:nth-child(3)]:shrink-0 [&>:nth-child(3)]:max-w-none [&>span:nth-child(2)]:pr-12 [&>span:nth-child(2)]:text-2xl"
                icono={k.ilustracion}
                etiqueta={
                  <span className="block min-w-0">
                    <span className="block text-xs font-medium leading-tight line-clamp-2">{k.etiqueta}</span>
                    {networkActive && (
                      <NetworkScopeBadge
                        scope={net.scope}
                        hijos={net.children}
                        className="mt-1"
                        testId={`kpi-red-${k.categoria}`}
                      />
                    )}
                  </span>
                }
                cifra={cifraKpi(overviewStatus, totalPorCategoria(k.categoria), overviewError)}
              />
            ))}
          </div>
        </div>
      </div>

      {/* Franjas 2 y 3 — solo con Trámites en el dock. */}
      {veTramites && (
      <>
      {/* Franja 2 — estado general; pulsar un estado filtra la cola de gestión. */}
      <PanelInicio
        data-testid="panel-estado-general"
        titulo={<>Estado general de trámites {etiquetaPropia}</>}
        subtitulo="Pulsa cualquier estado para filtrar la bandeja"
        accion={
          estado ? (
            <button
              type="button"
              onClick={() => setEstado("")}
              className={buttonClass({ variant: "ghost", size: "sm" })}
            >
              Quitar filtro <Icono nombre="cerrar" size={16} />
            </button>
          ) : conteosEstado.estado === "listo" ? (
            <span className="text-[0.8125rem] text-[color:var(--di-text-tertiary)]">
              {totalTramites} {plural(totalTramites, "trámite", "trámites")} en total
            </span>
          ) : null
        }
      >
        {conteosEstado.estado === "sin-datos" ? (
          <p className="text-sm text-[color:var(--di-text-tertiary)]">No se pudieron cargar los conteos por estado.</p>
        ) : (
          <EstadoFunnel
            estados={ESTADOS_INICIO_GESTOR}
            counts={conteosFranja}
            selected={estado}
            onSelect={setEstado}
          />
        )}
      </PanelInicio>

      {/* Franja 3 — firma y cola. */}
      <div className="grid grid-cols-1 lg:grid-cols-2 gap-3">
        <PanelInicio
          data-testid="panel-firma"
          titulo={<>Trámites por firma {etiquetaPropia}</>}
          icono="firma"
          subtitulo="Borradores según la firma de vendedor y comprador"
          accion={
            <Link href={tramitesPorAtajo(ATAJO_SIN_FIRMAS)} className={buttonClass({ variant: "ghost", size: "sm" })}>
              Ver detalle
            </Link>
          }
        >
          {firmaStatus === "demasiados" ? (
            <p className="text-sm text-[color:var(--di-text-secondary)]" data-testid="firma-demasiados">
              Hay demasiados borradores para contar las firmas; acota el periodo.
            </p>
          ) : (
          <UiStateBoundary
            status={firmaStatus}
            errorMessage={firmaError}
            onRetry={retry}
            emptyMessage="No hay borradores en el periodo seleccionado."
            skeletonRows={3}
          >
            {firma && (
              <GraficoDona
                total={firma.conFirmas + firma.sinFirmas}
                porciones={[
                  {
                    clave: "con-firmas",
                    etiqueta: "Borradores con firmas",
                    valor: firma.conFirmas,
                    color: estadoAcentoVar("aprobado"),
                  },
                  {
                    clave: "sin-firmas",
                    etiqueta: "Borradores sin firmas",
                    valor: firma.sinFirmas,
                    color: "var(--kit-naranja)",
                  },
                ]}
              />
            )}
          </UiStateBoundary>
          )}
        </PanelInicio>

        <PanelInicio
          data-testid="panel-cola"
          titulo={<>Cola de gestión {etiquetaPropia}</>}
          icono="reloj"
          subtitulo={`Los de mayor inactividad${filtroCola ? ` · ${filtroCola}` : ""}`}
          accion={
            <>
              <label className={`flex items-center gap-2 ${FILTRO_CONTROL_CLS}`}>
                <span className="sr-only">Filtrar por tipo de trámite</span>
                <select
                  value={tipo}
                  onChange={(e) => setTipo(e.target.value as FiltroTipo)}
                  aria-label="Filtrar por tipo de trámite"
                  className="min-h-9 rounded-[9px] border border-[color:var(--di-border-control)] bg-[var(--di-surface-elevated)] px-3 text-sm text-[color:var(--di-text-primary)]"
                >
                  <option value="todos">Todos</option>
                  {familiasVisibles.map((f) => (
                    <option key={f.categoria} value={f.categoria}>
                      {f.etiqueta}
                    </option>
                  ))}
                </select>
              </label>
              <Link
                href={estado ? tramitesPorEstado(estado) : TRAMITES_PATH}
                className={buttonClass({ variant: "ghost", size: "sm" })}
              >
                Ver todos{cola && cola.total > 0 ? ` (${cola.total})` : ""}
              </Link>
            </>
          }
        >
          <DataTable
            tarjeta={false}
            columns={columnasCola}
            rows={cola?.items ?? []}
            getRowKey={(r) => r.id}
            status={colaStatus}
            errorMessage={colaError}
            onRetry={retry}
            emptyMessage="No hay trámites en la cola para este filtro."
            allowHorizontalScroll={false}
            ariaLabel="Cola de gestión"
          />
        </PanelInicio>
      </div>
      </>
      )}
    </div>
  );
}

