"use client";

import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import Link from "next/link";
import { useRouter } from "next/navigation";
import {
  BadgeCheck,
  Ban,
  CheckCircle2,
  ChevronRight,
  CircleDashed,
  Clock,
  FileCheck2,
  RefreshCw,
  Search,
  X,
  XCircle,
} from "lucide-react";
import { ModuleTitle } from "./ModuleTitle";
import { StatusBadge } from "@/components/atom/StatusBadge";
import { Pagination } from "@/components/atom/Pagination";
import { UiStateBoundary } from "@/components/admin/UiStateBoundary";
import { WIZARD_CTA_GRADIENT } from "@/components/operacion/wizard-field-styles";
import {
  fetchLogQxBandeja,
  type LogQxBandejaEntry,
  type LogQxBandejaEstado,
  type LogQxBandejaParams,
} from "@/lib/api/admin-log-qx";
import {
  ESTADOS_BANDEJA,
  ESTADO_BANDEJA,
  esperaEsAlta,
  formatEspera,
  formatFecha,
  Secretaria,
  secretaria,
} from "@/lib/logqx/labels";

/**
 * Módulo "LOG QX" — bandeja (HU #11788, Feature #11784). Reemplaza la pantalla que exigía una
 * búsqueda exacta por uno de tres ejes excluyentes para mostrar cualquier dato.
 *
 * Tres cambios de fondo respecto de la anterior:
 *  1. CARGA CON DATOS. No hay que buscar nada: entra y ve el periodo por defecto.
 *  2. UNA FILA POR TRÁMITE, no por radicación (ADR-0051, D1). Un trámite con tres intentos aparece
 *     una vez, con su contador de intentos.
 *  3. INCLUYE LOS "SIN RADICAR": trámites elegibles que nunca se encolaron. Es el caso más caro
 *     para soporte y hasta ahora era invisible, porque sin radicación no había fila que listar.
 *
 * El detalle vive en pantalla propia (`/log-qx/{submissionId}`); aquí solo el vistazo expandible,
 * que resuelve el caso frecuente sin navegar ni perder los filtros.
 */

const PAGE_SIZE = 25;

/** Filtros que viajan en el estado y en el query string, para restituirlos al volver. */
interface Filtros {
  desde: string;
  hasta: string;
  placa: string;
  referencia: string;
  documento: string;
  estado: LogQxBandejaEstado | "";
  /**
   * Acotado a un trámite concreto. No tiene campo en el formulario: llega por el deep-link
   * `?m=log-qx&instanceId=…` desde el detalle del trámite (HU #10796). Se muestra como un chip
   * retirable para que quede claro POR QUÉ la lista está acotada.
   */
  instanceId: string;
}

const FILTROS_VACIOS: Filtros = {
  desde: "",
  hasta: "",
  placa: "",
  referencia: "",
  documento: "",
  estado: "",
  instanceId: "",
};

function hoyMenos(dias: number): string {
  const d = new Date();
  d.setDate(d.getDate() - dias);
  return d.toISOString().slice(0, 10);
}

export function LogQx({ initialInstanceId }: { initialInstanceId?: string } = {}) {
  const router = useRouter();

  // Con deep-link se abre SIN rango de fechas: quien llega desde un trámite quiere ver ese trámite,
  // y la ventana por defecto podría dejarlo fuera si es antiguo.
  const [filtros, setFiltros] = useState<Filtros>(() =>
    initialInstanceId
      ? { ...FILTROS_VACIOS, instanceId: initialInstanceId }
      : {
          ...FILTROS_VACIOS,
          desde: hoyMenos(30),
          hasta: new Date().toISOString().slice(0, 10),
        },
  );
  const [aplicados, setAplicados] = useState<Filtros>(filtros);

  const [entries, setEntries] = useState<LogQxBandejaEntry[] | null>(null);
  const [contadores, setContadores] = useState<Record<string, number>>({});
  const [total, setTotal] = useState(0);
  const [page, setPage] = useState(1);
  const [error, setError] = useState<string | null>(null);
  const [fetching, setFetching] = useState(false);
  const [abierta, setAbierta] = useState<string | null>(null);

  // Guard anti-race: solo se aplica el resultado de la petición más reciente.
  const reqIdRef = useRef(0);

  const load = useCallback(async (f: Filtros, targetPage: number) => {
    const reqId = ++reqIdRef.current;
    setFetching(true);
    try {
      const params: LogQxBandejaParams = {
        desde: f.desde ? new Date(`${f.desde}T00:00:00`).toISOString() : undefined,
        hasta: f.hasta ? new Date(`${f.hasta}T23:59:59`).toISOString() : undefined,
        placa: f.placa || undefined,
        referencia: f.referencia || undefined,
        documento: f.documento || undefined,
        estado: f.estado || undefined,
        instanceId: f.instanceId || undefined,
        page: targetPage,
        pageSize: PAGE_SIZE,
      };
      const res = await fetchLogQxBandeja(params);
      if (reqId !== reqIdRef.current) return;
      setEntries(res.data);
      setTotal(res.totalCount);
      setContadores(Object.fromEntries(res.contadores.map((c) => [c.estado, c.total])));
      setError(null);
    } catch (err) {
      if (reqId !== reqIdRef.current) return;
      setEntries(null);
      setError(err instanceof Error ? err.message : "No se pudo cargar el LOG QX.");
    } finally {
      if (reqId === reqIdRef.current) setFetching(false);
    }
  }, []);

  // Carga inicial y refetch. Sincroniza con el backend (fuente externa); no deriva estado ya
  // disponible en render. Mismo patrón que Auditoria.
  useEffect(() => {
    // eslint-disable-next-line react-hooks/set-state-in-effect
    void load(aplicados, page);
  }, [aplicados, page, load]);

  const aplicar = useCallback(
    (f: Filtros) => {
      setPage(1);
      setAbierta(null);
      setAplicados(f);
    },
    [],
  );

  const handleSubmit = useCallback(
    (e: React.FormEvent) => {
      e.preventDefault();
      aplicar(filtros);
    },
    [aplicar, filtros],
  );

  const limpiar = useCallback(() => {
    const base = { ...FILTROS_VACIOS, desde: hoyMenos(30), hasta: new Date().toISOString().slice(0, 10) };
    setFiltros(base);
    aplicar(base);
  }, [aplicar]);

  /** El contador actúa como filtro rápido; volver a pulsarlo lo retira. */
  const alternarEstado = useCallback(
    (estado: LogQxBandejaEstado) => {
      const siguiente: Filtros = {
        ...filtros,
        estado: aplicados.estado === estado ? "" : estado,
      };
      setFiltros(siguiente);
      aplicar(siguiente);
    },
    [aplicar, aplicados.estado, filtros],
  );

  const abrirTrazabilidad = useCallback(
    (entry: LogQxBandejaEntry) => {
      if (!entry.submissionId) return;
      const qs = new URLSearchParams();
      Object.entries(aplicados).forEach(([k, v]) => {
        if (v) qs.set(k, String(v));
      });
      if (page > 1) qs.set("page", String(page));
      // Los filtros viajan en la URL para poder restituirlos al volver de la trazabilidad.
      router.push(`/log-qx/${entry.submissionId}?${qs.toString()}`);
    },
    [aplicados, page, router],
  );

  const status: "loading" | "error" | "empty" | "ready" =
    fetching && entries === null
      ? "loading"
      : error !== null && entries === null
        ? "error"
        : entries !== null && entries.length === 0
          ? "empty"
          : "ready";

  const hayFiltros = useMemo(
    () =>
      Boolean(
        aplicados.placa || aplicados.referencia || aplicados.documento
          || aplicados.estado || aplicados.instanceId,
      ),
    [aplicados],
  );

  const quitarTramite = useCallback(() => {
    const siguiente: Filtros = {
      ...filtros,
      instanceId: "",
      desde: filtros.desde || hoyMenos(30),
      hasta: filtros.hasta || new Date().toISOString().slice(0, 10),
    };
    setFiltros(siguiente);
    aplicar(siguiente);
  }, [aplicar, filtros]);

  return (
    <div className="app-bg min-h-screen px-6 pt-6 pb-10 flex flex-col gap-4 text-[#162744] dark:text-white">
      <ModuleTitle
        title="LOG QX"
        subtitle="Trámites con integración Quipux. Filtra por fecha, placa, documento o estado, y abre la trazabilidad completa del que necesites."
      />

      {aplicados.instanceId && (
        <div className="flex shrink-0 flex-wrap items-center gap-2 rounded-xl border border-[#557EFF]/30 bg-[#557EFF]/[0.07] px-3 py-2 text-xs">
          <span>
            Mostrando solo el trámite{" "}
            <span className="font-mono">{aplicados.instanceId}</span>
          </span>
          <button
            type="button"
            onClick={quitarTramite}
            className="inline-flex items-center gap-1 rounded-full border border-[#557EFF]/40 px-2 py-0.5 font-medium text-[#557EFF] hover:bg-[#557EFF]/10"
          >
            <X className="h-3 w-3" aria-hidden="true" /> Ver todos
          </button>
        </div>
      )}

      {/* Tira de contadores por estado, con la misma presentación que los KPIs de Trámites
          (`EstadoFunnel`): una tarjeta única dividida en columnas, con icono en pastilla del tono
          del estado, etiqueta y conteo. Antes eran siete cajas grises indistinguibles entre sí. */}
      <div
        role="group"
        aria-label="Contadores por estado"
        className="grid shrink-0 grid-cols-2 divide-[#EEF2F7] overflow-hidden rounded-2xl border border-[#DFE5ED] bg-white sm:grid-cols-4 sm:divide-x lg:grid-cols-7 dark:divide-white/5 dark:border-white/10 dark:bg-[#162744]"
      >
        {ESTADOS_BANDEJA.map((estado) => {
          const meta = ESTADO_BANDEJA[estado];
          const Icon = ESTADO_ICON[estado];
          const activo = aplicados.estado === estado;
          const total = contadores[estado] ?? 0;
          return (
            <button
              key={estado}
              type="button"
              aria-label={`${meta.label}: ${total} trámite${total === 1 ? "" : "s"}`}
              aria-pressed={activo}
              onClick={() => alternarEstado(estado)}
              className="flex flex-col items-center gap-1 px-2 py-3 transition hover:bg-[#557EFF]/[0.06] focus:outline-none focus-visible:ring-2 focus-visible:ring-inset focus-visible:ring-[#557EFF]"
              style={activo ? { background: meta.style.bg } : undefined}
            >
              {/* El icono es elemento gráfico (umbral 3:1): lleva el tono PURO del estado. */}
              <span
                className="grid h-8 w-8 shrink-0 place-items-center rounded-full"
                style={{ background: meta.style.bg }}
              >
                <Icon className="h-4 w-4" style={{ color: meta.style.accent }} aria-hidden="true" />
              </span>
              <span className="max-w-full truncate text-xs font-medium text-[#162744]/70 dark:text-white/70">
                {meta.label}
              </span>
              <span
                className="text-xl font-bold leading-none tabular-nums text-[#162744] dark:text-white"
                aria-hidden="true"
              >
                {total}
              </span>
              {/* El filtro activo no depende solo del fondo. */}
              <span
                className="h-0.5 w-6 rounded-full"
                style={{ background: activo ? meta.style.color : "transparent" }}
                aria-hidden="true"
              />
            </button>
          );
        })}
      </div>

      <form
        onSubmit={handleSubmit}
        className="flex flex-wrap items-end gap-2 shrink-0 rounded-2xl border border-[#DFE5ED] dark:border-white/10 bg-white dark:bg-[#0B0F14] p-3"
        role="search"
        aria-label="Filtros del LOG QX"
      >
        <Campo label="Desde" htmlFor="lq-desde">
          <input
            id="lq-desde"
            type="date"
            value={filtros.desde}
            onChange={(e) => setFiltros({ ...filtros, desde: e.target.value })}
            className={inputCls}
          />
        </Campo>
        <Campo label="Hasta" htmlFor="lq-hasta">
          <input
            id="lq-hasta"
            type="date"
            value={filtros.hasta}
            onChange={(e) => setFiltros({ ...filtros, hasta: e.target.value })}
            className={inputCls}
          />
        </Campo>
        <Campo label="Placa" htmlFor="lq-placa">
          <input
            id="lq-placa"
            type="text"
            placeholder="ABC123"
            value={filtros.placa}
            onChange={(e) => setFiltros({ ...filtros, placa: e.target.value })}
            className={`${inputCls} w-[110px]`}
          />
        </Campo>
        <Campo label="Trámite" htmlFor="lq-ref">
          <input
            id="lq-ref"
            type="text"
            placeholder="TRM-2026-…"
            value={filtros.referencia}
            onChange={(e) => setFiltros({ ...filtros, referencia: e.target.value })}
            className={`${inputCls} w-[150px]`}
          />
        </Campo>
        <Campo label="Documento QX" htmlFor="lq-doc">
          <input
            id="lq-doc"
            type="text"
            placeholder="placa o VIN"
            value={filtros.documento}
            onChange={(e) => setFiltros({ ...filtros, documento: e.target.value })}
            className={`${inputCls} w-[150px]`}
          />
        </Campo>
        <span className="flex-1" />
        {(hayFiltros || fetching) && (
          <button type="button" onClick={limpiar} className={ghostCls}>
            <X className="h-3.5 w-3.5" aria-hidden="true" /> Limpiar
          </button>
        )}
        <button
          type="submit"
          disabled={fetching}
          className="flex items-center gap-2 rounded-lg px-4 py-2 text-xs font-semibold text-white disabled:opacity-50 focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2"
          style={{ background: WIZARD_CTA_GRADIENT }}
        >
          <Search className="h-3.5 w-3.5" aria-hidden="true" /> Aplicar
        </button>
      </form>

      <UiStateBoundary
        status={status}
        skeletonRows={6}
        errorMessage={error ?? "No se pudo cargar el LOG QX."}
        onRetry={() => void load(aplicados, page)}
        emptyMessage="Ningún trámite con integración Quipux coincide con los filtros. Amplía el rango de fechas o quita algún filtro."
      >
        {entries && entries.length > 0 && (
          <>
            {/* Tabla en el patrón del resto de la consola (companies / trámites): cabecera en
                pastilla #DFE5ED y cada fila como tarjeta blanca separada, no una rejilla de
                bordes. */}
            <div className="overflow-x-auto">
              <table className="w-full min-w-[1040px] border-separate border-spacing-y-2 text-xs">
                <thead>
                  <tr
                    className="text-left text-[10px] font-semibold uppercase"
                    style={{ color: "#162744" }}
                  >
                    <th className="rounded-l-xl px-3 py-2.5" style={{ background: "#DFE5ED", width: 34 }}>
                      <span className="sr-only">Detalle</span>
                    </th>
                    {COLUMNAS.map((c) => (
                      <th key={c} className="px-4 py-2.5" style={{ background: "#DFE5ED" }}>
                        {c}
                      </th>
                    ))}
                    <th className="rounded-r-xl px-4 py-2.5" style={{ background: "#DFE5ED" }}>
                      Antigüedad
                    </th>
                  </tr>
                </thead>
                <tbody>
                  {entries.map((entry) => (
                    <FilaTramite
                      key={entry.procedureInstanceId}
                      entry={entry}
                      abierta={abierta === entry.procedureInstanceId}
                      onToggle={() =>
                        setAbierta(
                          abierta === entry.procedureInstanceId ? null : entry.procedureInstanceId,
                        )
                      }
                      onAbrir={() => abrirTrazabilidad(entry)}
                    />
                  ))}
                </tbody>
              </table>
            </div>
            <Pagination
              page={page}
              pageSize={PAGE_SIZE}
              totalCount={total}
              onPageChange={(p) => {
                setAbierta(null);
                setPage(Math.max(1, p));
              }}
              className="mt-0"
            />
          </>
        )}
      </UiStateBoundary>
    </div>
  );
}

const inputCls =
  "rounded-lg border border-[#D9DEE8] dark:border-white/15 bg-white dark:bg-[#0B0F14] px-2.5 py-2 text-xs outline-none focus:border-[#557EFF] focus:ring-2 focus:ring-[#557EFF]/20";

const ghostCls =
  "inline-flex items-center gap-1.5 rounded-lg border border-[#D9DEE8] dark:border-white/15 px-3 py-2 text-xs font-medium opacity-80 hover:opacity-100 hover:border-[#557EFF]";

/** Cabeceras entre el chevron y «Antigüedad»; el orden lo fijó el PO. */
const COLUMNAS = [
  "Trámite",
  "Placa",
  "Tipo",
  "Estado",
  "Empresa",
  "Secretaría",
  "Documento QX",
  "Última actividad",
] as const;

/** Icono por estado, en la línea de `ESTADO_ICON` de la tira de KPIs de trámites. */
const ESTADO_ICON: Record<LogQxBandejaEstado, typeof CircleDashed> = {
  sin_radicar: CircleDashed,
  pendiente: FileCheck2,
  radicado: BadgeCheck,
  en_tramite: RefreshCw,
  aprobado: CheckCircle2,
  rechazado: XCircle,
  fallido: Ban,
};

const tdCls = "border-y px-4 py-3 align-middle";

function Campo({
  label,
  htmlFor,
  children,
}: {
  label: string;
  htmlFor: string;
  children: React.ReactNode;
}) {
  return (
    <label htmlFor={htmlFor} className="flex flex-col gap-1 text-[10px]">
      <span className="font-semibold uppercase tracking-wider opacity-55">{label}</span>
      {children}
    </label>
  );
}

/** Fila del trámite + su vistazo expandible. */
function FilaTramite({
  entry,
  abierta,
  onToggle,
  onAbrir,
}: {
  entry: LogQxBandejaEntry;
  abierta: boolean;
  onToggle: () => void;
  onAbrir: () => void;
}) {
  const meta = ESTADO_BANDEJA[entry.estado] ?? { label: entry.estado, tone: "neutral" as const };
  const espera = formatEspera(entry.horasEsperando);
  const alta = esperaEsAlta(entry.horasEsperando);

  return (
    <>
      <tr
        className={`cursor-pointer transition ${
          abierta ? "bg-[#557EFF]/[0.06]" : "bg-white hover:bg-[#557EFF]/[0.04] dark:bg-[#0B0F14]"
        }`}
        onClick={onToggle}
        tabIndex={0}
        role="button"
        aria-expanded={abierta}
        onKeyDown={(e) => {
          if (e.key === "Enter" || e.key === " ") {
            e.preventDefault();
            onToggle();
          }
        }}
      >
        <td className={`${tdCls} rounded-l-xl border-l px-3`}>
          <ChevronRight
            className={`h-3.5 w-3.5 opacity-50 transition-transform ${abierta ? "rotate-90" : ""}`}
            aria-hidden="true"
          />
        </td>
        <td className={tdCls}>
          <span className="font-mono font-semibold text-[#557EFF]">{entry.referenceNumber}</span>
          {entry.intentos > 1 && (
            <span className="ml-1.5 rounded-full bg-[#FF4E00]/15 px-1.5 py-0.5 font-mono text-[10px] font-bold text-[#C2410C]">
              {entry.intentos} intentos
            </span>
          )}
        </td>
        <td className={tdCls}>
          {entry.plate ? (
            <span className="rounded border border-[#D9DEE8] bg-[#F4F6FA] px-1.5 py-0.5 font-mono font-bold tracking-wide dark:border-white/15 dark:bg-white/5">
              {entry.plate}
            </span>
          ) : (
            <span className="opacity-40">sin placa</span>
          )}
        </td>
        <td className={tdCls}>{entry.procedureTypeName}</td>
        <td className={tdCls}>
          {/* Mismo chip que el listado de trámites: la paleta por estado, no los cinco tonos
              semánticos, que dejaban tres de estos siete estados con el mismo color. */}
          <StatusBadge
            label={meta.label}
            bg={meta.style.bg}
            color={meta.style.color}
            border={meta.style.border}
          />
        </td>
        <td className={`${tdCls} opacity-75`}>{entry.clientTenantName}</td>
        <td className={`${tdCls} opacity-75`}>{entry.transitOfficeName}</td>
        <td className={tdCls}>
          {entry.documentoQx ? (
            <span className="font-mono opacity-80" title={entry.documentoQx}>
              {entry.documentoQx.length > 26
                ? `…${entry.documentoQx.slice(-24)}`
                : entry.documentoQx}
            </span>
          ) : (
            <span className="opacity-40">—</span>
          )}
        </td>
        <td className={`${tdCls} whitespace-nowrap opacity-75`}>
          {entry.ultimaActividad ? formatFecha(entry.ultimaActividad) : "—"}
        </td>
        <td className={`${tdCls} rounded-r-xl border-r`}>
          {espera ? (
            <span
              className={`whitespace-nowrap font-mono tabular-nums ${alta ? "font-bold text-[#C2410C]" : ""}`}
            >
              {espera}
              {alta ? " ⚠" : ""}
            </span>
          ) : (
            <span className="opacity-40">—</span>
          )}
        </td>
      </tr>
      {abierta && (
        <tr>
          {/* El detalle es su propia tarjeta, coherente con las filas-tarjeta de la tabla. */}
          <td colSpan={10} className="rounded-xl border bg-[#F4F7FC] p-0 dark:bg-white/[0.03]">
            <Vistazo entry={entry} onAbrir={onAbrir} />
          </td>
        </tr>
      )}
    </>
  );
}

/**
 * El vistazo: qué pasó, en una frase, más los pasos alcanzados. Deliberadamente SIN payloads —
 * quien necesita el detalle técnico abre la trazabilidad.
 */
function Vistazo({ entry, onAbrir }: { entry: LogQxBandejaEntry; onAbrir: () => void }) {
  return (
    <div className="flex flex-wrap items-start gap-5 px-8 py-4">
      <div className="min-w-[380px] flex-1">
        <p className="max-w-[70ch] text-[13px] leading-relaxed">{resumen(entry)}</p>
        <div className="mt-3 flex flex-wrap gap-1.5">
          {pasos(entry).map((p) => (
            <span
              key={p.label}
              className="inline-flex items-center gap-1.5 rounded-full border border-[#DFE5ED] bg-white px-2.5 py-1 text-[11px] font-medium dark:border-white/10 dark:bg-[#0B0F14]"
            >
              <p.Icon className="h-3.5 w-3.5" style={{ color: p.color }} aria-hidden="true" />
              {p.label}
            </span>
          ))}
        </div>
        {entry.rejectionReason && (
          <p className="mt-3 rounded-lg border-l-2 border-[#FF4E00] bg-[#FF4E00]/[0.08] px-3 py-2 text-xs text-[#C2410C]">
            Motivo del rechazo: {entry.rejectionReason}
          </p>
        )}
      </div>
      <div className="flex flex-col gap-2">
        {entry.submissionId ? (
          <button
            type="button"
            onClick={onAbrir}
            className="whitespace-nowrap rounded-lg px-4 py-2 text-xs font-semibold text-white transition hover:opacity-95 focus:outline-none focus-visible:ring-2 focus-visible:ring-[#557EFF] focus-visible:ring-offset-2"
            style={{ background: WIZARD_CTA_GRADIENT }}
          >
            Ver trazabilidad completa
          </button>
        ) : (
          <span className="max-w-[220px] text-[11px] italic opacity-55">
            Todavía no hay radicación que trazar.
          </span>
        )}
        <Link
          href={`/tramites/${entry.procedureInstanceId}`}
          onClick={(e) => e.stopPropagation()}
          className="inline-flex items-center justify-center whitespace-nowrap rounded-lg border border-[#DFE5ED] bg-white px-4 py-2 text-xs font-semibold text-[#557EFF] transition hover:bg-[#557EFF]/[0.08] focus:outline-none focus-visible:ring-2 focus-visible:ring-[#557EFF] dark:border-white/15 dark:bg-transparent"
        >
          Ver trámite
        </Link>
      </div>
    </div>
  );
}

/**
 * El resumen en lenguaje natural: lo que un agente de soporte le repite al cliente por teléfono.
 * Antes había que deducirlo leyendo JSON.
 */
function resumen(e: LogQxBandejaEntry): string {
  const espera = formatEspera(e.horasEsperando);

  switch (e.estado) {
    case "sin_radicar":
      return `Este trámite cumple los requisitos para ir a Quipux${
        espera ? ` desde hace ${espera}` : ""
      }, pero todavía no se ha encolado. Conviene revisar que ${secretaria(
        e.transitOfficeName,
      )} tenga el DIVIPO configurado y la integración activa para este tipo de trámite.`;

    case "pendiente":
      return `Está en cola para radicarse en Quipux${
        espera ? `, esperando desde hace ${espera}` : ""
      }. Aún no se ha enviado a ${secretaria(e.transitOfficeName)}.`;

    case "radicado":
      return `Radicado en Quipux como ${e.documentoQx}. Todavía no se ha ejecutado la primera consulta de estado a ${secretaria(e.transitOfficeName)}.`;

    case "en_tramite":
      return `Radicado en Quipux como ${e.documentoQx}. ${Secretaria(
        e.transitOfficeName,
      )} aún no lo resuelve${espera ? `: llevamos ${espera} esperando` : ""}, con ${
        e.pollCount
      } consultas de estado realizadas.`;

    case "aprobado":
      return `${Secretaria(e.transitOfficeName)} lo aprobó. Se radicó como ${e.documentoQx} y el trámite quedó resuelto.`;

    case "rechazado":
      return `${Secretaria(e.transitOfficeName)} lo rechazó${
        e.rejectionReason ? "" : " sin dejar un motivo registrado"
      }. Se había radicado como ${e.documentoQx}.`;

    case "fallido":
      return `La radicación falló tras ${e.attempts} ${
        e.attempts === 1 ? "intento" : "intentos"
      }${
        e.intentos > 1 ? ` y ${e.intentos} radicaciones` : ""
      }. Este trámite nunca llegó a ${secretaria(e.transitOfficeName)}.`;

    default:
      return `Estado ${e.estado} en ${secretaria(e.transitOfficeName)}.`;
  }
}

/**
 * Los pasos alcanzados, sin entrar en el detalle técnico.
 *
 * Los iconos son del set de la app (lucide) y no glifos sueltos (⏳, ✓, ○): esos se renderizan
 * como emoji, con su propio color y su propia caja, y desentonan con el resto de la consola.
 */
function pasos(e: LogQxBandejaEntry): { Icon: typeof CircleDashed; color: string; label: string }[] {
  const gris = ESTADO_BANDEJA.sin_radicar.style.accent;
  const espera = ESTADO_BANDEJA.en_tramite.style.accent;
  const ok = ESTADO_BANDEJA.aprobado.style.accent;
  const mal = ESTADO_BANDEJA.rechazado.style.accent;

  if (e.estado === "sin_radicar") {
    return [
      { Icon: CircleDashed, color: gris, label: "Sin encolar" },
      { Icon: Clock, color: espera, label: "Elegible, a la espera" },
    ];
  }

  const out: { Icon: typeof CircleDashed; color: string; label: string }[] = [];

  if (e.estado === "pendiente") {
    out.push({ Icon: CircleDashed, color: gris, label: "En cola" });
  } else if (e.estado !== "fallido") {
    out.push({ Icon: BadgeCheck, color: ok, label: "Radicado" });
  }

  if (e.pollCount > 0) {
    out.push({ Icon: RefreshCw, color: espera, label: `${e.pollCount} consultas` });
  }

  if (e.estado === "en_tramite") out.push({ Icon: Clock, color: espera, label: "Sin decisión" });
  if (e.estado === "radicado") {
    out.push({ Icon: Clock, color: espera, label: "Primera consulta pendiente" });
  }
  if (e.estado === "aprobado") out.push({ Icon: CheckCircle2, color: ok, label: "Aprobado" });
  if (e.estado === "rechazado") out.push({ Icon: XCircle, color: mal, label: "Rechazado" });
  if (e.estado === "fallido") {
    out.push({ Icon: Ban, color: ESTADO_BANDEJA.fallido.style.accent, label: "Radicación fallida" });
  }

  return out;
}
