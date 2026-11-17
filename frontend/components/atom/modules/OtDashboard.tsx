"use client";

// Inicio de una sesión de organismo de tránsito (Feature #11939; rediseño en 3 dashboards, 2026-11-17).
//
// Existe porque `Dashboard` —el inicio del gestor— le pregunta a `/analytics/*`, que filtra por el tenant de quien
// llama. Un trámite vive en el tenant de la EMPRESA CLIENTE; el usuario del OT vive en el tenant del ORGANISMO. Las
// respuestas volvían 200 y vacías: no era un conteo mal hecho, era la fuente de datos equivocada. Todo lo de aquí sale
// de `/admin/ot/metrics/*`.
//
// Tres franjas, en el orden del prototipo (`OtView`), con el diseño del UI Kit 2026:
//  1. Carrusel + cifras de la cola. Las cifras describen el AHORA y no las mueve el rango de fechas.
//  2. Tiempos de gestión (en DÍAS, sin meta: no hay SLA en el dominio) + cola crítica (lo que lleva más esperando).
//  3. Resoluciones y causales de rechazo + trámites por cliente corporativo.
// El rango de fechas gobierna solo las franjas 2 (tiempos) y 3.

import { useCallback, useEffect, useMemo, useState } from "react";
import Link from "next/link";
import { Icono } from "@flit/ui/Icono";
import { buttonClass } from "@flit/ui/Button";
import { TarjetaMetrica } from "@flit/ui/TarjetaMetrica";
import { DataTable, type DataTableColumn } from "@flit/ui/DataTable";
import { usePaginacion } from "@flit/ui/usePaginacion";
import { Pestanas } from "@flit/ui/Pestanas";
import { BarraProgreso } from "@flit/ui/BarraProgreso";
import { StatusBadge, type StatusBadgeTone } from "@flit/ui/StatusBadge";
import { fetchOtProfile } from "@/lib/api/admin-ot";
import { fetchTransitOffices } from "@/lib/api/admin-companies";
import { getToken } from "@/lib/api/client";
import { ApiError } from "@/lib/api/types";
import { decodeJwtPayload, hasPermission, isOtAdmin, isOtUser, isSuperAdmin } from "@/lib/auth/jwt";
import {
  fetchOtClientsByFamily,
  fetchOtDrilldown,
  fetchOtOperationalPanel,
  fetchOtRejectionReasons,
  fetchOtReport,
  OT_DRILLDOWN_BUCKETS,
  type OtClientFamilyRow,
  type OtClientsByFamily,
  type OtDrilldown,
  type OtDrilldownBucket,
  type OtDrilldownItem,
  type OtMetricsParams,
  type OtOperationalPanel,
  type OtRejectionReasons,
  type OtReportSummary,
} from "@/lib/api/ot-metrics";
import { useActiveBanners } from "@/hooks/useActiveBanners";
import { resolveOtTransitOfficeId } from "@/components/admin/transit-offices/ot-nav";
import {
  DrilldownPanel,
  type DrilldownState,
} from "@/components/admin/transit-offices/_reportes/DrilldownPanel";
import { defaultRange } from "@/components/admin/transit-offices/_reportes/filters";
import { RangoFechas, resolverPreset } from "@/components/atom/RangoFechas";
import { isValidRange, type DateRange } from "@/components/atom/modules/_reportes/range";
import { CarruselInicio, type SlideBienvenida } from "@/components/inicio/CarruselInicio";
import { PanelInicio } from "@/components/inicio/PanelInicio";
import { GraficoArea } from "@/components/inicio/GraficoArea";
import { GraficoDona } from "@/components/inicio/GraficoDona";
import { FILTROS_REJILLA_CLS } from "@/components/inicio/filtros-inicio";

/** Título del slide fijo cuando aún no se conoce el nombre del organismo (A18 es N/A: no es «Hola, {nombre}»). */
export const OT_DASHBOARD_HERO_TITLE = "Tu cola de trabajo";
/** Antetítulo del slide fijo, del prototipo. */
export const OT_DASHBOARD_HERO_ANTETITULO = "Control de la operación";
/** KPIs de la cola (A19 es N/A: no se clonan ni se unifican con COPY.E03). */
export const OT_KPI_PENDIENTES_HOY = "Pendientes de hoy";
export const OT_KPI_PRIORITARIOS = "Trámites prioritarios";
export const OT_KPI_MAS_DE_7_DIAS = "Sin gestión más de 7 días";
export const OT_KPI_POR_ASIGNAR_PLACA = "Por asignar / preasignar placa";

/** Ventana de la mediana de decisión del mensaje de bienvenida. Coincide con el rango que se manda al panel. */
const VENTANA_MEDIANA_DIAS = 30;

/** Filas de la cola crítica. El resto está en la bandeja («Ver todos»). */
const COLA_CRITICA_FILAS = 5;

/** Causales que se muestran: las que más pesan. El catálogo completo vive en Reportes. */
const CAUSALES_TOP = 3;

type Estado = "cargando" | "listo" | "error" | "sin-permiso";

const MENSAJE_SIN_PERMISO_COLA = "No tienes permiso para ver esta información.";

function sinPermisoDeCola(): boolean {
  const claims = decodeJwtPayload(getToken());
  if (!claims || !isOtUser(claims) || isSuperAdmin(claims) || isOtAdmin(claims)) return false;
  return !hasPermission(claims, "ot.reportes.manage");
}

/** Abre el detalle de un bloque de la cola. */
type AbrirBloque = (bucket: OtDrilldownBucket, label: string) => void;

/** Familias del filtro de resoluciones. `""` es «Todos». */
type Familia = "" | "MATRICULAS" | "TRASPASO" | "OTROS";

const FAMILIAS: { id: Familia | "todos"; label: string }[] = [
  { id: "todos", label: "Todos" },
  { id: "MATRICULAS", label: "Matrícula inicial" },
  { id: "TRASPASO", label: "Traspasos" },
  { id: "OTROS", label: "Otros trámites" },
];

/** Rango de arranque de las franjas 2 y 3: los últimos 30 días, resueltos en día LOCAL como el selector. */
function rangoInicial(): DateRange {
  return resolverPreset("ultimos_30", new Date()) ?? defaultRange();
}

export function OtDashboard() {
  const [panel, setPanel] = useState<OtOperationalPanel | null>(null);
  const [params, setParams] = useState<OtMetricsParams | null>(null);
  const [estado, setEstado] = useState<Estado>("cargando");
  const [mensajeError, setMensajeError] = useState<string | null>(null);
  const [intento, setIntento] = useState(0);
  const [drilldown, setDrilldown] = useState<DrilldownState | null>(null);
  const [organismo, setOrganismo] = useState<string | null>(null);
  const [rango, setRango] = useState<DateRange>(rangoInicial);

  useEffect(() => {
    const controller = new AbortController();

    async function cargar() {
      if (sinPermisoDeCola()) {
        setPanel(null);
        setMensajeError(null);
        setEstado("sin-permiso");
        return;
      }
      setEstado("cargando");
      try {
        // El id del organismo sale del perfil, con la caché de sesión que ya usa el dock.
        const transitOfficeId = await resolveOtTransitOfficeId(async () => {
          const perfil = await fetchOtProfile(controller.signal);
          return perfil.transitOfficeId;
        });
        if (controller.signal.aborted) return;
        // La cola describe este momento: su rango es fijo (solo gobierna la mediana del mensaje) y el selector de
        // fechas no lo mueve. Se fija antes de pedir el panel para que las franjas 2 y 3 no dependan de él.
        const consulta: OtMetricsParams = { ...defaultRange(), transitOfficeId };
        setParams(consulta);
        const datos = await fetchOtOperationalPanel(consulta);
        if (controller.signal.aborted) return;
        setPanel(datos);
        setEstado("listo");
      } catch (err) {
        if (controller.signal.aborted || (err as Error)?.name === "AbortError") return;
        if (err instanceof ApiError && err.status === 403) {
          setEstado("sin-permiso");
          return;
        }
        setMensajeError(
          err instanceof Error && err.message ? err.message : "No se pudo cargar el estado de la cola.",
        );
        setEstado("error");
      }
    }

    void cargar();
    return () => controller.abort();
  }, [intento]);

  // Nombre del organismo para el carrusel. Falla en silencio: sin catálogo el slide se queda con el título genérico.
  useEffect(() => {
    const transitOfficeId = params?.transitOfficeId;
    if (!transitOfficeId) return;
    const controller = new AbortController();
    void fetchTransitOffices(undefined, controller.signal)
      .then((catalogo) => {
        if (controller.signal.aborted) return;
        const oficina = catalogo.find((o) => o.id === transitOfficeId);
        if (oficina) setOrganismo(`${oficina.name} (${oficina.code})`);
      })
      .catch(() => undefined);
    return () => controller.abort();
  }, [params?.transitOfficeId]);

  const reintentar = useCallback(() => setIntento((n) => n + 1), []);

  // Banners Activos globales (HU #12242, AC2): mismo set y mismo hook que ve el gestor.
  const activeBanners = useActiveBanners();
  const bienvenida = useMemo(
    () => mensajesDelOrganismo(organismo, estado === "listo" ? panel : null),
    [organismo, estado, panel],
  );

  // El detalle se pide con los MISMOS parámetros del panel, para que la lista nunca contradiga a la tarjeta que la
  // abrió: es el backend quien recalcula el bloque con idénticos predicados.
  const abrirBloque = useCallback<AbrirBloque>(
    (bucket, label) => {
      if (!params) return;
      setDrilldown({ bucket, label, loading: true, error: null, data: null });
      fetchOtDrilldown(params, bucket)
        .then((data) => setDrilldown({ bucket, label, loading: false, error: null, data }))
        .catch((e: unknown) =>
          setDrilldown({
            bucket,
            label,
            loading: false,
            error: e instanceof Error ? e.message : "No se pudo cargar el detalle.",
            data: null,
          }),
        );
    },
    [params],
  );

  const transitOfficeId = params?.transitOfficeId;
  const conPermiso = estado !== "sin-permiso";
  const rangoValido = isValidRange(rango);
  const periodo = useMemo<OtMetricsParams | null>(
    () => (transitOfficeId && rangoValido ? { from: rango.from, to: rango.to, transitOfficeId } : null),
    [transitOfficeId, rangoValido, rango.from, rango.to],
  );

  return (
    <div className="app-bg flex min-h-screen flex-col gap-4 px-6 pb-10 pt-6 text-foreground max-sm:px-4" data-testid="ot-inicio">
      {/* Franja 1: carrusel a la izquierda; filtros y cifras de la cola a la derecha. */}
      <div className="grid grid-cols-1 items-stretch gap-3 lg:grid-cols-2">
        <CarruselInicio
          bienvenida={bienvenida}
          banners={activeBanners}
          aria-label="Novedades del organismo"
          data-testid="ot-inicio-carrusel"
        />
        <div className="flex min-w-0 flex-col gap-3">
          {conPermiso && (
            <div className={FILTROS_REJILLA_CLS}>
              <RangoFechas value={rango} onChange={setRango} idPrefix="ot-inicio-rango" />
            </div>
          )}
          {estado === "sin-permiso" ? (
            <p role="status" className="rounded-[9px] border border-[color:var(--di-border)] bg-card px-5 py-4 text-sm text-muted-foreground">
              {MENSAJE_SIN_PERMISO_COLA}
            </p>
          ) : estado === "error" ? (
            <ErrorPanel message={mensajeError} onRetry={reintentar} />
          ) : (
            <KpisCola panel={estado === "listo" ? panel : null} onAbrir={abrirBloque} />
          )}
        </div>
      </div>

      {conPermiso && transitOfficeId && (
        <>
          {!rangoValido && (
            <p role="status" className="text-sm text-muted-foreground">
              Elige un rango de fechas completo para ver los tiempos, las resoluciones y los clientes.
            </p>
          )}

          {/* Franja 2: tiempos de gestión del periodo + lo que lleva más esperando ahora. */}
          <div className="grid grid-cols-1 gap-3 lg:grid-cols-2">
            <TiemposGestion periodo={periodo} />
            <ColaCritica params={params} transitOfficeId={transitOfficeId} />
          </div>

          {/* Franja 3: en qué terminó lo recibido + quién lo radicó. */}
          <div className="grid grid-cols-1 gap-3 lg:grid-cols-2">
            <Resoluciones periodo={periodo} />
            <ClientesCorporativos periodo={periodo} />
          </div>
        </>
      )}

      <DrilldownPanel
        state={drilldown}
        transitOfficeId={transitOfficeId ?? ""}
        onClose={() => setDrilldown(null)}
      />
    </div>
  );
}

// ── Carrusel ──────────────────────────────────────────────────────────────────

/**
 * Slide fijo del carrusel del organismo. El del gestor anunciaba «validación de identidad con IA en TUS trámites» a
 * quien no radica ni valida biometrías; aquí el único mensaje propio habla de la cola. Los banners Activos van detrás
 * (los pone `CarruselInicio`).
 */
function mensajesDelOrganismo(organismo: string | null, panel: OtOperationalPanel | null): SlideBienvenida[] {
  const porRevisar = panel?.cola.porRevisar ?? null;
  const estancados = panel?.antiguedad.masDe7Dias ?? 0;
  const medianaHoras = panel?.movimiento.tiempoMedianoDecisionHoras ?? null;

  const cola =
    porRevisar === null
      ? "Aquí ves el estado de tu cola en este momento y el movimiento del día."
      : porRevisar === 0
        ? "No tienes trámites esperando decisión en este momento."
        : `Tienes ${porRevisar} ${porRevisar === 1 ? "trámite" : "trámites"} esperando tu decisión` +
          (estancados > 0
            ? `, ${estancados} ${estancados === 1 ? "lleva" : "llevan"} más de 7 días esperando`
            : "") +
          ".";

  const mediana =
    porRevisar !== null && medianaHoras !== null
      ? ` Tu mediana de decisión de los últimos ${VENTANA_MEDIANA_DIAS} días es de ${formatDias(medianaHoras / 24)}.`
      : "";

  return [
    {
      antetitulo: OT_DASHBOARD_HERO_ANTETITULO,
      titulo: organismo ?? OT_DASHBOARD_HERO_TITLE,
      cuerpo: `${cola}${mediana} Los datos son del día calendario de Bogotá.`,
    },
  ];
}

// ── Franja 1: cifras de la cola ──────────────────────────────────────────────

/**
 * Las 4 cifras de la cola, en 2×2. Cada una abre su lista (`DrilldownPanel`); una en cero no se ofrece como enlace.
 * No las mueve el rango: describen la cola de este momento.
 */
function KpisCola({ panel, onAbrir }: { panel: OtOperationalPanel | null; onAbrir: AbrirBloque }) {
  const cargando = panel === null;
  return (
    <div className="grid flex-1 grid-cols-2 gap-3" data-testid="ot-inicio-kpis">
      <Kpi
        label={OT_KPI_PENDIENTES_HOY}
        value={panel?.movimiento.entregadosHoy}
        cargando={cargando}
        ilustracion="tramites"
        hint="Radicados ingresados hoy"
        onAbrir={() => onAbrir(OT_DRILLDOWN_BUCKETS.entregadosHoy, OT_KPI_PENDIENTES_HOY)}
      />
      <Kpi
        label={OT_KPI_PRIORITARIOS}
        value={panel?.cola.prioritarios}
        cargando={cargando}
        ilustracion="pendientes"
        hint="Atención preferencial"
        pendiente
        onAbrir={() => onAbrir(OT_DRILLDOWN_BUCKETS.prioritarios, OT_KPI_PRIORITARIOS)}
      />
      <Kpi
        label={OT_KPI_MAS_DE_7_DIAS}
        value={panel?.antiguedad.masDe7Dias}
        cargando={cargando}
        ilustracion="vencidos"
        hint="Llevan más de 7 días en el organismo"
        pendiente
        onAbrir={() => onAbrir(OT_DRILLDOWN_BUCKETS.masDe7Dias, OT_KPI_MAS_DE_7_DIAS)}
      />
      <Kpi
        label={OT_KPI_POR_ASIGNAR_PLACA}
        value={panel?.cola.esperandoAsignarPlaca}
        cargando={cargando}
        ilustracion="preasignacion"
        hint="Matrículas iniciales"
        onAbrir={() => onAbrir(OT_DRILLDOWN_BUCKETS.esperandoPlaca, OT_KPI_POR_ASIGNAR_PLACA)}
      />
    </div>
  );
}

function Kpi({
  label,
  value,
  cargando,
  ilustracion,
  pendiente,
  hint,
  onAbrir,
}: {
  label: string;
  value: number | undefined;
  cargando: boolean;
  /** Ilustración del prototipo a la derecha de la cifra (HU #14481). */
  ilustracion: string;
  /** Lo que espera al organismo: con trámites, la tarjeta lleva el borde de pendiente (`.metrica--pendiente`). */
  pendiente?: boolean;
  hint?: string;
  onAbrir?: () => void;
}) {
  const numero = cargando ? null : (value ?? 0);
  // Un bloque en cero NO se ofrece como enlace: llevaría a una lista vacía, una promesa incumplida.
  const navegable = numero !== null && numero > 0 && Boolean(onAbrir);
  return (
    // Receta del Inicio del gestor (HU #14481): `TarjetaMetrica` con la ilustración fijada abajo a la derecha (con
    // `max-w-none`, o la rejilla la aplasta). `content-start`: si la fila la estira, la cifra sigue bajo el rótulo.
    <TarjetaMetrica
      className="relative w-full grid-cols-1 content-start p-4 [&>:nth-child(3)]:absolute [&>:nth-child(3)]:bottom-3.5 [&>:nth-child(3)]:right-3.5 [&>:nth-child(3)]:h-9 [&>:nth-child(3)]:w-9 [&>:nth-child(3)]:max-w-none [&>:nth-child(3)]:shrink-0 [&>span:nth-child(2)]:pr-11 [&>span:nth-child(4)]:pr-11 [&>span:nth-child(4)]:font-normal [&>span:nth-child(4)]:text-[color:var(--di-text-tertiary)]"
      etiqueta={label}
      cifra={
        numero === null ? (
          <span className="text-3xl" aria-label="Cargando">…</span>
        ) : numero === 0 ? (
          // Un KPI vacío se dice en palabras, igual que en el Inicio del gestor.
          <span className="text-base font-medium">Ninguno</span>
        ) : (
          <span className="text-3xl">{numero}</span>
        )
      }
      icono={ilustracion}
      pie={hint}
      pendiente={Boolean(pendiente) && navegable}
      onClick={navegable ? onAbrir : undefined}
      aria-label={navegable ? `Ver los ${numero} trámites: ${label.toLowerCase()}` : undefined}
    />
  );
}

// ── Carga por recurso ─────────────────────────────────────────────────────────

/**
 * Carga un recurso por clave. El estado «cargando» se DERIVA de que la clave del último resultado no sea la pedida, en
 * vez de marcarse dentro del efecto (`react-hooks/set-state-in-effect`). Cada panel tiene su propio ciclo: si uno se
 * cae, los demás se siguen viendo.
 */
function useRecurso<T>(clave: string | null, cargar: ((signal: AbortSignal) => Promise<T>) | null) {
  const [resultado, setResultado] = useState<{ clave: string; dato: T | null; error: string | null } | null>(null);

  useEffect(() => {
    if (!clave || !cargar) return;
    const controller = new AbortController();
    cargar(controller.signal)
      .then((dato) => {
        if (!controller.signal.aborted) setResultado({ clave, dato, error: null });
      })
      .catch((err: unknown) => {
        if (controller.signal.aborted || (err as Error)?.name === "AbortError") return;
        setResultado({
          clave,
          dato: null,
          error: err instanceof ApiError && err.status === 400 && err.message ? err.message : "error",
        });
      });
    return () => controller.abort();
  }, [clave, cargar]);

  const vigente = clave !== null && resultado?.clave === clave ? resultado : null;
  const estado: Estado = !vigente ? "cargando" : vigente.error ? "error" : "listo";
  // Un 400 del rango (p. ej. más de un año) trae un motivo que se puede mostrar; el resto se dice genérico.
  const motivo = vigente?.error && vigente.error !== "error" ? vigente.error : null;
  return { dato: vigente?.dato ?? null, estado, motivo };
}

function claveDe(periodo: OtMetricsParams | null, extra = ""): string | null {
  return periodo ? `${periodo.transitOfficeId}|${periodo.from}|${periodo.to}|${extra}` : null;
}

/** Del informe solo interesa el resumen: se pide la página mínima. */
function cargarResumen(periodo: OtMetricsParams, family: Familia) {
  return (signal: AbortSignal): Promise<OtReportSummary> =>
    fetchOtReport({ ...periodo, family: family || undefined, page: 1, pageSize: 1 }, signal).then((r) => r.resumen);
}

// ── Franja 2 ──────────────────────────────────────────────────────────────────

const GRANULARIDAD: Record<string, string> = { dia: "día", semana: "semana", mes: "mes" };

/**
 * Tiempo promedio de decisión por periodo, en días. Sin línea de meta: no hay SLA en el dominio. Los periodos sin
 * decisiones no se dibujan en cero —se leería «se decidió al instante»—, se omiten.
 */
function TiemposGestion({ periodo }: { periodo: OtMetricsParams | null }) {
  const cargar = useMemo(() => (periodo ? cargarResumen(periodo, "") : null), [periodo]);
  const { dato: resumen, estado, motivo } = useRecurso(claveDe(periodo, "resumen"), cargar);
  const puntos = (resumen?.serie ?? [])
    .filter((p) => p.tiempoPromedioDecisionDias != null)
    .map((p) => ({ etiqueta: p.label, valor: p.tiempoPromedioDecisionDias ?? 0 }));
  const granularidad = GRANULARIDAD[resumen?.granularidad ?? ""] ?? "periodo";

  return (
    <PanelInicio
      titulo="Tiempos de gestión"
      subtitulo={`Tiempo promedio de decisión, en días, por ${granularidad}`}
      icono="cronometro"
      data-testid="ot-inicio-tiempos"
    >
      {!periodo || estado === "cargando" ? (
        <Esqueleto filas={3} />
      ) : estado === "error" ? (
        <FalloPanel texto={motivo ?? "No se pudieron cargar los tiempos de gestión."} />
      ) : puntos.length === 0 ? (
        <Vacio>No hubo decisiones en el periodo elegido.</Vacio>
      ) : (
        <GraficoArea puntos={puntos} nombreSerie="Tiempo promedio de decisión" formato={formatDiasCorto} />
      )}
    </PanelInicio>
  );
}

/** Ruta de la bandeja del organismo filtrada por la placa o el VIN del trámite: «ir a decidirlo». */
function enlaceTramite(transitOfficeId: string, item: OtDrilldownItem): string {
  const query = new URLSearchParams();
  if (item.placa) query.set("placa", item.placa);
  else if (item.vin) query.set("vin", item.vin);
  query.set("status", item.status);
  return `/admin/transit-offices/${transitOfficeId}/client-procedures?${query.toString()}`;
}

function tonoEspera(dias: number): StatusBadgeTone {
  if (dias > 7) return "danger";
  if (dias > 3) return "warning";
  return "info";
}

/**
 * Lo que lleva más esperando la decisión del organismo, AHORA (no lo mueve el rango). Sale del mismo detalle que abre
 * «Esperan mi decisión», que el backend ya ordena por días de espera de mayor a menor.
 */
function ColaCritica({ params, transitOfficeId }: { params: OtMetricsParams | null; transitOfficeId: string }) {
  const cargar = useMemo(
    () =>
      params
        ? (signal: AbortSignal): Promise<OtDrilldown> => fetchOtDrilldown(params, OT_DRILLDOWN_BUCKETS.porRevisar, signal)
        : null,
    [params],
  );
  const { dato, estado } = useRecurso(params ? `${transitOfficeId}|cola-critica` : null, cargar);
  const filas = useMemo(
    () =>
      [...(dato?.items ?? [])]
        .sort((a, b) => (b.diasEsperando ?? 0) - (a.diasEsperando ?? 0))
        .slice(0, COLA_CRITICA_FILAS),
    [dato],
  );

  const columnas: DataTableColumn<OtDrilldownItem>[] = [
    {
      key: "radicado",
      header: "Radicado",
      render: (r) => <span className="font-medium tabular-nums">{r.referenceNumber}</span>,
    },
    {
      key: "tramite",
      header: "Trámite",
      render: (r) => (
        <span className="flex flex-col">
          <span>{r.tipoTramite || r.familia || "Trámite"}</span>
          <span className="text-xs text-[color:var(--di-text-tertiary)]">{r.clientTenantName}</span>
        </span>
      ),
    },
    {
      key: "espera",
      header: "Tiempo en espera",
      render: (r) => {
        const dias = r.diasEsperando ?? 0;
        return <StatusBadge label={formatDiasEnteros(dias)} tone={tonoEspera(dias)} />;
      },
    },
    {
      key: "accion",
      header: <span className="sr-only">Acción</span>,
      align: "right",
      render: (r) => (
        <Link
          href={enlaceTramite(transitOfficeId, r)}
          className={buttonClass({ variant: "secondary", size: "sm" })}
          aria-label={`Revisar el trámite ${r.referenceNumber}`}
        >
          Revisar
        </Link>
      ),
    },
  ];

  return (
    <PanelInicio
      titulo="Cola crítica"
      subtitulo="Lo que lleva más tiempo esperando tu decisión"
      icono="vencido"
      data-testid="ot-inicio-cola-critica"
      accion={
        <Link
          href={`/admin/transit-offices/${transitOfficeId}/client-procedures?status=entregado`}
          className="text-sm font-semibold text-[color:var(--di-action)] hover:underline"
        >
          Ver todos
        </Link>
      }
    >
      <DataTable
        columns={columnas}
        rows={filas}
        getRowKey={(r) => r.procedureInstanceId}
        status={estado === "cargando" ? "loading" : estado === "error" ? "error" : filas.length === 0 ? "empty" : "ready"}
        emptyMessage="No hay trámites esperando tu decisión."
        errorMessage="No se pudo cargar la cola crítica."
        ariaLabel="Cola crítica"
        tarjeta={false}
        minWidth={520}
      />
    </PanelInicio>
  );
}

// ── Franja 3 ──────────────────────────────────────────────────────────────────

/**
 * En qué terminó lo RECIBIDO en el periodo (aprobado contra rechazado) y por qué se rechaza, filtrado por familia.
 * Solo se monta el contenido de la pestaña activa: las demás no piden nada hasta que se eligen.
 */
function Resoluciones({ periodo }: { periodo: OtMetricsParams | null }) {
  const [familia, setFamilia] = useState<Familia | "todos">("todos");
  return (
    <PanelInicio titulo="Resoluciones y causales de rechazo" icono="causal" data-testid="ot-inicio-resoluciones">
      <Pestanas
        ariaLabel="Tipo de trámite"
        active={familia}
        onChange={setFamilia}
        tabs={FAMILIAS.map((f) => ({
          id: f.id,
          label: f.label,
          content:
            f.id === familia ? <ResolucionesDeFamilia periodo={periodo} familia={f.id === "todos" ? "" : f.id} /> : null,
        }))}
      />
    </PanelInicio>
  );
}

function ResolucionesDeFamilia({ periodo, familia }: { periodo: OtMetricsParams | null; familia: Familia }) {
  // «Todos» reutiliza la clave del panel de tiempos: es la misma consulta del informe.
  const cargarRes = useMemo(() => (periodo ? cargarResumen(periodo, familia) : null), [periodo, familia]);
  const resumen = useRecurso(claveDe(periodo, familia ? `resumen-${familia}` : "resumen"), cargarRes);

  const cargarCausales = useMemo(
    () =>
      periodo
        ? (signal: AbortSignal): Promise<OtRejectionReasons> =>
            fetchOtRejectionReasons({ ...periodo, family: familia || undefined }, signal)
        : null,
    [periodo, familia],
  );
  const causales = useRecurso(claveDe(periodo, `causales-${familia}`), cargarCausales);

  if (!periodo || resumen.estado === "cargando") return <Esqueleto filas={3} />;
  if (resumen.estado === "error") {
    return <FalloPanel texto={resumen.motivo ?? "No se pudieron cargar las resoluciones."} />;
  }

  const r = resumen.dato;
  const aprobados = r?.aprobados ?? 0;
  // Un rechazo con la subsanación abierta sigue siendo un veredicto en contra, aunque el trámite vaya a volver.
  const rechazados = (r?.rechazados ?? 0) + (r?.enSubsanacion ?? 0);
  const decididos = aprobados + rechazados;
  const pctAprobados = decididos === 0 ? 0 : (aprobados / decididos) * 100;

  const top = (causales.dato?.causales ?? []).filter((c) => c.rechazos > 0).slice(0, CAUSALES_TOP);

  return (
    <div className="grid grid-cols-1 gap-5 xl:grid-cols-2">
      {decididos === 0 ? (
        <Vacio>Ningún trámite recibido en el periodo se ha aprobado ni rechazado todavía.</Vacio>
      ) : (
        <div className="flex flex-col gap-2">
          <GraficoDona
            total={decididos}
            centro={formatPct(pctAprobados)}
            rotuloCentro="Aprobados"
            porciones={[
              {
                clave: "aprobados",
                etiqueta: "Aprobados",
                valor: aprobados,
                color: "var(--flit-success-ink)",
                detalle: formatPct(pctAprobados),
              },
              {
                clave: "rechazados",
                etiqueta: "Rechazados",
                valor: rechazados,
                color: "var(--color-flit-alert)",
                detalle: formatPct(100 - pctAprobados),
              },
            ]}
          />
          <p className="text-xs text-[color:var(--di-text-tertiary)]" data-testid="ot-inicio-pct-aprobados">
            {formatPct(pctAprobados)} de lo decidido se aprobó. Universo: trámites recibidos en el periodo.
          </p>
        </div>
      )}

      <div className="flex min-w-0 flex-col gap-3" data-testid="ot-inicio-causales">
        <div>
          <h3 className="text-sm font-semibold text-[color:var(--di-text-primary)]">Principales causales de rechazo</h3>
          {/* El % es de RECHAZOS que incluyen la causal: un rechazo puede llevar varias, así que no suman 100. */}
          <p className="text-xs text-[color:var(--di-text-tertiary)]">
            % de los rechazos del periodo que incluyen la causal. Un rechazo puede tener varias.
          </p>
        </div>
        {causales.estado === "cargando" ? (
          <Esqueleto filas={2} />
        ) : causales.estado === "error" ? (
          <FalloPanel texto="No se pudieron cargar las causales." />
        ) : top.length === 0 ? (
          <Vacio>No hubo rechazos con causal en el periodo.</Vacio>
        ) : (
          <ul className="m-0 flex list-none flex-col gap-3 p-0">
            {top.map((c) => {
              // Cada causal es «% de rechazos que la incluyen»: por sí sola no puede pasar de 100. Si un dato malo lo
              // hiciera, la barra se acota en vez de desbordarse (la suma ENTRE causales sí puede pasar de 100).
              const pct = Math.min(100, Math.max(0, c.pct));
              return (
                <li key={c.reasonId} className="flex flex-col gap-1">
                  <span className="flex items-baseline justify-between gap-3 text-[0.8125rem]">
                    <span className="min-w-0 text-[color:var(--di-text-primary)]">{c.description}</span>
                    <strong className="shrink-0 tabular-nums">{formatPct(pct)}</strong>
                  </span>
                  <BarraProgreso
                    etiqueta={c.description}
                    valor={pct}
                    maximo={100}
                    textoValor={`${formatPct(pct)} de los rechazos (${c.rechazos})`}
                  />
                </li>
              );
            })}
          </ul>
        )}
      </div>
    </div>
  );
}

/** Quién le radicó al organismo en el periodo, por familia. Mismo universo que el informe. */
function ClientesCorporativos({ periodo }: { periodo: OtMetricsParams | null }) {
  // HU #13920 — un organismo puede tener muchos clientes: paginación estándar (10/20/30/50, «Filas por página»).
  const pg = usePaginacion("inicio.OtDashboard.clientes");
  const cargar = useMemo(
    () => (periodo ? (signal: AbortSignal): Promise<OtClientsByFamily> => fetchOtClientsByFamily(periodo, signal) : null),
    [periodo],
  );
  const { dato, estado, motivo } = useRecurso(claveDe(periodo, "clientes"), cargar);
  const filas = dato?.clientes ?? [];

  const numero = (v: number) => <span className="tabular-nums">{v}</span>;
  const columnas: DataTableColumn<OtClientFamilyRow>[] = [
    { key: "cliente", header: "Cliente", render: (r) => <span className="font-medium">{r.nombre}</span> },
    { key: "matriculas", header: "Matrícula inicial", align: "right", render: (r) => numero(r.matriculas) },
    { key: "traspasos", header: "Traspasos", align: "right", render: (r) => numero(r.traspasos) },
    { key: "otros", header: "Otros trámites", align: "right", render: (r) => numero(r.otros) },
    { key: "total", header: "Total", align: "right", render: (r) => <strong className="tabular-nums">{r.total}</strong> },
  ];

  return (
    <PanelInicio
      titulo="Trámites por Cliente Corporativo"
      subtitulo="Volumen radicado en el periodo, por tipo de trámite"
      icono="empresas"
      data-testid="ot-inicio-clientes"
    >
      <DataTable
        columns={columnas}
        rows={pg.paginar(filas)}
        getRowKey={(r) => r.clientTenantId}
        status={!periodo || estado === "cargando" ? "loading" : estado === "error" ? "error" : filas.length === 0 ? "empty" : "ready"}
        emptyMessage="Ningún cliente radicó trámites en el periodo."
        errorMessage={motivo ?? "No se pudo cargar el volumen por cliente."}
        ariaLabel="Trámites por cliente corporativo"
        tarjeta={false}
        minWidth={520}
        pagination={{
          page: pg.page,
          pageSize: pg.pageSize,
          totalCount: filas.length,
          onPageChange: pg.setPage,
          onPageSizeChange: pg.setPageSize,
        }}
      />
    </PanelInicio>
  );
}

// ── Piezas ────────────────────────────────────────────────────────────────────

/**
 * Un fallo NO se pinta como ceros: ese era el defecto de la pantalla anterior, una cola con problemas se leía como una
 * cola sana.
 */
function ErrorPanel({ message, onRetry }: { message: string | null; onRetry: () => void }) {
  return (
    <div
      role="alert"
      className="flex flex-1 flex-col items-start gap-3 rounded-[9px] border border-[var(--badge-warning-border)] bg-[var(--badge-warning-bg)] px-5 py-4"
    >
      <p className="flex items-center gap-2 text-sm font-semibold text-foreground">
        <Icono nombre="alerta" className="h-4 w-4 shrink-0 text-[var(--badge-warning-fg)]" />
        No se pudo cargar el estado de la cola
      </p>
      <p className="text-xs text-muted-foreground">{message ?? "Intenta de nuevo en un momento."}</p>
      <button type="button" onClick={onRetry} className={buttonClass({ variant: "secondary", size: "sm" })}>
        Reintentar
      </button>
    </div>
  );
}

function FalloPanel({ texto }: { texto: string }) {
  return (
    <p role="alert" className="py-6 text-center text-sm text-muted-foreground">
      {texto}
    </p>
  );
}

function Vacio({ children }: { children: React.ReactNode }) {
  return <p className="py-6 text-center text-sm text-muted-foreground">{children}</p>;
}

function Esqueleto({ filas }: { filas: number }) {
  return (
    <div className="flex flex-col gap-3" aria-hidden="true">
      {Array.from({ length: filas }, (_, i) => (
        <div key={i} className="h-8 animate-pulse rounded-lg bg-secondary" />
      ))}
    </div>
  );
}

const NUM_1 = new Intl.NumberFormat("es-CO", { maximumFractionDigits: 1 });

/** «1,5 días», «1 día», «menos de 1 día». */
function formatDias(dias: number): string {
  if (dias < 0.05) return "menos de 1 día";
  const n = NUM_1.format(dias);
  return `${n} ${n === "1" ? "día" : "días"}`;
}

/** Eje y tooltip de la gráfica de tiempos: «1,5 d». */
function formatDiasCorto(dias: number): string {
  return `${NUM_1.format(dias)} d`;
}

/** Días enteros de espera en la cola: «0 días», «1 día», «12 días». */
function formatDiasEnteros(dias: number): string {
  const n = Math.floor(dias);
  return `${n} ${n === 1 ? "día" : "días"}`;
}

/** Un decimal, como en el resto de porcentajes del producto. */
function formatPct(valor: number): string {
  return `${NUM_1.format(valor)} %`;
}
