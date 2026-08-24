"use client";

import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import Link from "next/link";
import { useRouter } from "next/navigation";
import { ChevronRight, FileText, Search, X } from "lucide-react";
import { ModuleTitle } from "./ModuleTitle";
import { StatusBadge } from "@/components/atom/StatusBadge";
import { Pagination } from "@/components/atom/Pagination";
import { UiStateBoundary } from "@/components/admin/UiStateBoundary";
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
        <div className="flex shrink-0 flex-wrap items-center gap-2 rounded-xl border border-[#4F74C9]/30 bg-[#4F74C9]/[0.07] px-3 py-2 text-xs">
          <span>
            Mostrando solo el trámite{" "}
            <span className="font-mono">{aplicados.instanceId}</span>
          </span>
          <button
            type="button"
            onClick={quitarTramite}
            className="inline-flex items-center gap-1 rounded-full border border-[#4F74C9]/40 px-2 py-0.5 font-medium text-[#4F74C9] hover:bg-[#4F74C9]/10"
          >
            <X className="h-3 w-3" aria-hidden="true" /> Ver todos
          </button>
        </div>
      )}

      <div className="flex flex-wrap gap-2 shrink-0" role="group" aria-label="Contadores por estado">
        {ESTADOS_BANDEJA.map((estado) => {
          const meta = ESTADO_BANDEJA[estado];
          const activo = aplicados.estado === estado;
          return (
            <button
              key={estado}
              type="button"
              aria-pressed={activo}
              onClick={() => alternarEstado(estado)}
              className={`flex-1 min-w-[120px] rounded-xl border px-3 py-2 text-left transition ${
                activo
                  ? "border-[#4F74C9] bg-[#4F74C9]/[0.08]"
                  : "border-[#DDE5F0] dark:border-white/10 bg-white dark:bg-[#0B0F14] hover:border-[#C9D6EA]"
              }`}
            >
              <span className="block font-mono text-xl font-bold tabular-nums">
                {contadores[estado] ?? 0}
              </span>
              <span className="block text-[11px] font-medium opacity-70">{meta.label}</span>
            </button>
          );
        })}
      </div>

      <form
        onSubmit={handleSubmit}
        className="flex flex-wrap items-end gap-2 shrink-0 rounded-2xl border border-[#DDE5F0] dark:border-white/10 bg-white dark:bg-[#0B0F14] p-3"
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
          style={{ background: "linear-gradient(90deg,#4FD4CC 0%,#4F74C9 100%)" }}
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
            <div className="rounded-2xl border border-[#DDE5F0] dark:border-white/10 bg-white dark:bg-[#0B0F14] overflow-hidden">
              <div className="overflow-x-auto">
                <table className="w-full min-w-[1040px] border-collapse">
                  <thead>
                    <tr className="bg-[#F4F6FA] dark:bg-white/5">
                      <th className={thCls} style={{ width: 28 }} />
                      <th className={thCls}>Trámite</th>
                      <th className={thCls}>Placa</th>
                      <th className={thCls}>Tipo</th>
                      <th className={thCls}>Estado</th>
                      <th className={thCls}>Empresa</th>
                      <th className={thCls}>Secretaría</th>
                      <th className={thCls}>Documento QX</th>
                      <th className={thCls}>Última actividad</th>
                      <th className={thCls}>Antigüedad</th>
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
  "rounded-lg border border-[#D9DEE8] dark:border-white/15 bg-white dark:bg-[#0B0F14] px-2.5 py-2 text-xs outline-none focus:border-[#4F74C9] focus:ring-2 focus:ring-[#4F74C9]/20";

const ghostCls =
  "inline-flex items-center gap-1.5 rounded-lg border border-[#D9DEE8] dark:border-white/15 px-3 py-2 text-xs font-medium opacity-80 hover:opacity-100 hover:border-[#4F74C9]";

const thCls =
  "text-left text-[10px] font-bold uppercase tracking-wider opacity-55 px-3 py-2.5 border-b border-[#DDE5F0] dark:border-white/10 whitespace-nowrap";

const tdCls = "px-3 py-2.5 border-b border-[#DDE5F0] dark:border-white/10 align-middle";

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
        className={`cursor-pointer transition ${abierta ? "bg-[#4F74C9]/[0.06]" : "hover:bg-[#4F74C9]/[0.04]"}`}
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
        <td className={tdCls}>
          <ChevronRight
            className={`h-3.5 w-3.5 opacity-50 transition-transform ${abierta ? "rotate-90" : ""}`}
            aria-hidden="true"
          />
        </td>
        <td className={tdCls}>
          <span className="font-mono text-xs font-medium text-[#4F74C9]">{entry.referenceNumber}</span>
          {entry.intentos > 1 && (
            <span className="ml-1.5 rounded-full bg-[#F05A35]/15 px-1.5 py-0.5 font-mono text-[10px] font-bold text-[#D9521F]">
              {entry.intentos} intentos
            </span>
          )}
        </td>
        <td className={tdCls}>
          {entry.plate ? (
            <span className="rounded border border-[#D9DEE8] dark:border-white/15 bg-[#F4F6FA] dark:bg-white/5 px-1.5 py-0.5 font-mono text-xs font-bold tracking-wide">
              {entry.plate}
            </span>
          ) : (
            <span className="text-xs opacity-40">sin placa</span>
          )}
        </td>
        <td className={`${tdCls} text-xs`}>{entry.procedureTypeName}</td>
        <td className={tdCls}>
          <StatusBadge label={meta.label} tone={meta.tone} />
        </td>
        <td className={`${tdCls} text-xs opacity-75`}>{entry.clientTenantName}</td>
        <td className={`${tdCls} text-xs opacity-75`}>{entry.transitOfficeName}</td>
        <td className={tdCls}>
          {entry.documentoQx ? (
            <span className="font-mono text-[11px] opacity-80" title={entry.documentoQx}>
              {entry.documentoQx.length > 26
                ? `…${entry.documentoQx.slice(-24)}`
                : entry.documentoQx}
            </span>
          ) : (
            <span className="text-xs opacity-40">—</span>
          )}
        </td>
        <td className={`${tdCls} whitespace-nowrap text-xs opacity-75`}>
          {entry.ultimaActividad ? formatFecha(entry.ultimaActividad) : "—"}
        </td>
        <td className={tdCls}>
          {espera ? (
            <span
              className={`font-mono text-xs tabular-nums whitespace-nowrap ${alta ? "font-bold text-[#D9521F]" : ""}`}
            >
              {espera}
              {alta ? " ⚠" : ""}
            </span>
          ) : (
            <span className="text-xs opacity-40">—</span>
          )}
        </td>
      </tr>
      {abierta && (
        <tr>
          <td colSpan={10} className="border-b border-[#DDE5F0] dark:border-white/10 bg-[#EEF3FB] dark:bg-white/[0.03] p-0">
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
    <div className="flex flex-wrap items-start gap-5 px-10 py-4">
      <div className="flex-1 min-w-[380px]">
        <p className="max-w-[70ch] text-[13px] leading-relaxed">{resumen(entry)}</p>
        <div className="mt-3 flex flex-wrap gap-1.5">
          {pasos(entry).map((p, i) => (
            <span
              key={i}
              className="inline-flex items-center gap-1.5 rounded-full border border-[#DDE5F0] dark:border-white/10 bg-white dark:bg-[#0B0F14] px-2.5 py-1 text-[11px] font-medium"
            >
              <span aria-hidden="true">{p.icono}</span>
              {p.label}
            </span>
          ))}
        </div>
        {entry.rejectionReason && (
          <p className="mt-3 rounded-lg border-l-2 border-[#E43D30] bg-[#E43D30]/[0.06] px-3 py-2 text-xs text-[#D3352A]">
            Motivo del rechazo: {entry.rejectionReason}
          </p>
        )}
      </div>
      <div className="flex flex-col gap-2">
        {entry.submissionId ? (
          <button
            type="button"
            onClick={onAbrir}
            className="whitespace-nowrap rounded-lg bg-[#4F74C9] px-4 py-2 text-xs font-semibold text-white hover:brightness-110 focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2"
          >
            Ver trazabilidad completa →
          </button>
        ) : (
          <span className="max-w-[220px] text-[11px] italic opacity-55">
            Todavía no hay radicación que trazar.
          </span>
        )}
        <Link
          href={`/tramites/${entry.procedureInstanceId}`}
          onClick={(e) => e.stopPropagation()}
          className="inline-flex items-center justify-center gap-1.5 whitespace-nowrap rounded-lg border border-[#DDE5F0] dark:border-white/15 px-4 py-2 text-xs font-semibold text-[#4F74C9] hover:bg-[#4F74C9]/[0.08]"
        >
          <FileText className="h-3.5 w-3.5" aria-hidden="true" /> Ver trámite
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
      }, pero todavía no se ha encolado. Conviene revisar que la Secretaría de ${
        e.transitOfficeName
      } tenga el DIVIPO configurado y la integración activa para este tipo de trámite.`;

    case "pendiente":
      return `Está en cola para radicarse en Quipux${espera ? `, esperando desde hace ${espera}` : ""}. Aún no se ha enviado a la Secretaría de ${e.transitOfficeName}.`;

    case "radicado":
      return `Radicado en Quipux como ${e.documentoQx}. Todavía no se ha ejecutado la primera consulta de estado a la Secretaría de ${e.transitOfficeName}.`;

    case "en_tramite":
      return `Radicado en Quipux como ${e.documentoQx}. La Secretaría de ${
        e.transitOfficeName
      } aún no lo resuelve${espera ? `: llevamos ${espera} esperando` : ""}, con ${
        e.pollCount
      } consultas de estado realizadas.`;

    case "aprobado":
      return `La Secretaría de ${e.transitOfficeName} lo aprobó. Se radicó como ${e.documentoQx} y el trámite quedó resuelto.`;

    case "rechazado":
      return `La Secretaría de ${e.transitOfficeName} lo rechazó${
        e.rejectionReason ? "" : " sin dejar un motivo registrado"
      }. Se había radicado como ${e.documentoQx}.`;

    case "fallido":
      return `La radicación falló tras ${e.attempts} ${
        e.attempts === 1 ? "intento" : "intentos"
      }${e.intentos > 1 ? ` y ${e.intentos} radicaciones` : ""}. Este trámite nunca llegó a la Secretaría de ${e.transitOfficeName}.`;

    default:
      return `Estado ${e.estado} en la Secretaría de ${e.transitOfficeName}.`;
  }
}

/** Los pasos alcanzados, sin entrar en el detalle técnico. */
function pasos(e: LogQxBandejaEntry): { icono: string; label: string }[] {
  if (e.estado === "sin_radicar") {
    return [{ icono: "○", label: "Sin encolar" }, { icono: "⏳", label: "Elegible, a la espera" }];
  }

  const out: { icono: string; label: string }[] = [];

  if (e.estado === "pendiente") {
    out.push({ icono: "○", label: "En cola" });
  } else if (e.estado !== "fallido") {
    out.push({ icono: "✓", label: "Radicado" });
  }

  if (e.pollCount > 0) {
    out.push({ icono: "⏱", label: `${e.pollCount} consultas` });
  }

  if (e.estado === "en_tramite") out.push({ icono: "⏳", label: "Sin decisión" });
  if (e.estado === "radicado") out.push({ icono: "⏱", label: "Primera consulta pendiente" });
  if (e.estado === "aprobado") out.push({ icono: "✓", label: "Aprobado" });
  if (e.estado === "rechazado") out.push({ icono: "✕", label: "Rechazado" });
  if (e.estado === "fallido") out.push({ icono: "✕", label: "Radicación fallida" });

  return out;
}
