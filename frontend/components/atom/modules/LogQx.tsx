"use client";

import { useCallback, useEffect, useRef, useState } from "react";
import Link from "next/link";
import { ChevronDown, ChevronRight, Copy, Check, Search, Clock, Cpu, FileText } from "lucide-react";
import { ModuleTitle } from "./ModuleTitle";
import { StatusBadge, type StatusTone } from "@/components/atom/StatusBadge";
import { Pagination } from "@/components/atom/Pagination";
import { UiStateBoundary } from "@/components/admin/UiStateBoundary";
import { fetchLogQx, type LogQxEntry, type LogQxEvent, type LogQxStatus } from "@/lib/api/admin-log-qx";

/**
 * Módulo "LOG QX" (HU #10795, Feature #10792). Pantalla de soporte/administración montada
 * DENTRO del Shell SPA (`?m=log-qx`), gateada por el permiso `logqx.read` (o SuperAdmin) en
 * el dock y en el render de `app/page.tsx`. Consume GET /api/v1/admin/log-qx (HU #10793 +
 * gate/enmascarado HU #10794): busca radicaciones Quipux por placa, id de trámite o radicado
 * y muestra la línea de tiempo de cada una con el detalle técnico expandible (visor JSON +
 * copia). 4 estados de UI (cargando/error/vacío/lleno) vía `UiStateBoundary` y
 * "sin payload disponible" para eventos históricos sin `detail`.
 *
 * HU #10796 (correlación): si se monta con `initialInstanceId` (deep-link
 * `/?m=log-qx&instanceId=…` desde el detalle de un trámite), auto-aplica la búsqueda por ese
 * trámite al montar. Cada radicación enlaza de vuelta a su trámite (`/tramites/{id}`).
 */

/** Eje de búsqueda: los tres son excluyentes (se consulta por el elegido). */
type SearchAxis = "placa" | "instanceId" | "radicado";

const AXIS_META: Record<SearchAxis, { label: string; placeholder: string }> = {
  placa: { label: "Placa", placeholder: "Ej. ABC123" },
  instanceId: { label: "Trámite (ID)", placeholder: "UUID del trámite" },
  radicado: { label: "Código QX", placeholder: "Ej. 81 (registro) · 2/3 (estado)" },
};

/**
 * El eje "Trámite (ID)" busca por el UUID del trámite (el backend lo enlaza como `Guid`). Un valor
 * que no es UUID hace fallar el binding con 400: se valida en el cliente para dar un aviso claro en
 * vez de un error crudo (p. ej. escribir un número teniendo seleccionado "Trámite (ID)").
 */
const UUID_RE = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

/** Tono del chip de estado por estado final de la radicación (tones HU #10844). */
const STATUS_STYLE: Record<LogQxStatus, { label: string; tone: StatusTone }> = {
  pendiente: { label: "Pendiente", tone: "info" },
  registrado: { label: "Registrado", tone: "success" },
  aprobado: { label: "Aprobado", tone: "success" },
  rechazado: { label: "Rechazado", tone: "danger" },
  fallido: { label: "Fallido", tone: "danger" },
};

/** Etiquetas legibles de las etapas conocidas; las desconocidas se muestran tal cual. */
const STAGE_LABEL: Record<string, string> = {
  claimed: "Tomado de la cola",
  consolidado_generado: "Consolidado generado",
  s3_subido: "Documento subido",
  registro_enviado: "Registro enviado",
  registro_respuesta: "Respuesta de registro",
  registro_error: "Error de registro",
  consulta_enviada: "Consulta enviada",
  consulta_respuesta: "Respuesta de consulta",
  consulta_error: "Error de consulta",
  aprobado: "Aprobado por el OT",
  rechazado: "Rechazado por el OT",
  dead_letter: "Enviado a dead-letter",
  reintento_manual: "Reintento manual",
  cancelado_manual: "Cancelado manualmente",
};

const OUTCOME_STYLE: Record<string, { label: string; color: string }> = {
  ok: { label: "OK", color: "#5B8A1F" },
  error_transitorio: { label: "Error transitorio", color: "#FF4E00" },
  error_definitivo: { label: "Error definitivo", color: "#FF4E00" },
  omitido: { label: "Omitido", color: "#8A94A6" },
};

const DEFAULT_PAGE_SIZE = 20;

function formatFecha(iso: string | null | undefined): string {
  if (!iso) return "—";
  const d = new Date(iso);
  if (Number.isNaN(d.getTime())) return iso;
  return new Intl.DateTimeFormat("es-CO", { dateStyle: "medium", timeStyle: "short" }).format(d);
}

function formatDuration(ms: number | null): string | null {
  if (ms == null) return null;
  return ms >= 1000 ? `${(ms / 1000).toFixed(2)} s` : `${ms} ms`;
}

/**
 * Estado del trámite que devuelve Quipux en `estadoTramite.codigo`: 1 = no hay cambios (sigue en
 * trámite), 2 = aprobado, 3 = rechazado. Se muestra con su significado para que soporte no vea un
 * número pelado. Solo 2 y 3 son terminales; el 1 aparece en el detail de eventos de sondeo mientras
 * el organismo aún no resuelve. Otros códigos no documentados se muestran tal cual; ausente = "—".
 */
function formatEstadoTramite(code: number | null | undefined): string {
  if (code == null) return "—";
  if (code === 1) return "1 — Sin cambios";
  if (code === 2) return "2 — Aprobado";
  if (code === 3) return "3 — Rechazado";
  return code.toString();
}

export function LogQx({ initialInstanceId }: { initialInstanceId?: string } = {}) {
  // Controles de búsqueda (UI) y la búsqueda efectivamente aplicada al backend.
  const [axis, setAxis] = useState<SearchAxis>("placa");
  const [text, setText] = useState("");
  const [applied, setApplied] = useState<{ axis: SearchAxis; text: string } | null>(null);

  const [entries, setEntries] = useState<LogQxEntry[] | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [fetching, setFetching] = useState(false);

  const [page, setPage] = useState(1);
  const [total, setTotal] = useState(0);

  // Guard anti-race: solo se aplica el resultado de la petición más reciente.
  const reqIdRef = useRef(0);

  // HU #10796 (AC1) — deep-link: si llega `initialInstanceId` (desde el enlace "Ver LOG QX" del
  // detalle de un trámite), auto-aplica la búsqueda por ese trámite UNA sola vez al montar. El
  // efecto de fetch de abajo se dispara solo con `setApplied`. `seededRef` evita re-aplicar en
  // el doble-mount de Strict Mode o en re-renders si la prop cambia de referencia.
  const seededRef = useRef(false);
  useEffect(() => {
    if (seededRef.current || !initialInstanceId) return;
    seededRef.current = true;
    setAxis("instanceId");
    setText(initialInstanceId);
    setApplied({ axis: "instanceId", text: initialInstanceId });
  }, [initialInstanceId]);

  const load = useCallback(async (search: { axis: SearchAxis; text: string }, targetPage: number) => {
    const value = search.text.trim() || undefined;

    // Guard: el eje "Trámite (ID)" requiere un UUID. Sin esto, un valor no-UUID (p. ej. "3") revienta
    // el binding Guid del backend con 400. Se avisa en el cliente y no se dispara la petición.
    if (search.axis === "instanceId" && value && !UUID_RE.test(value)) {
      reqIdRef.current++; // invalida cualquier respuesta en vuelo
      setEntries(null);
      setTotal(0);
      setFetching(false);
      setError(
        'El identificador de trámite no tiene un formato válido. '
          + 'Si buscas por número, cambia el eje a "Código QX".',
      );
      return;
    }

    const reqId = ++reqIdRef.current;
    setFetching(true);
    try {
      const res = await fetchLogQx({
        placa: search.axis === "placa" ? value : undefined,
        instanceId: search.axis === "instanceId" ? value : undefined,
        radicado: search.axis === "radicado" ? value : undefined,
        page: targetPage,
        pageSize: DEFAULT_PAGE_SIZE,
      });
      if (reqId !== reqIdRef.current) return; // respuesta obsoleta
      setEntries(res.data);
      setTotal(res.totalCount);
      setError(null);
    } catch (err) {
      if (reqId !== reqIdRef.current) return;
      setEntries(null);
      setError(err instanceof Error ? err.message : "No se pudo cargar el LOG QX.");
    } finally {
      if (reqId === reqIdRef.current) setFetching(false);
    }
  }, []);

  // Refetch cuando cambia la búsqueda aplicada o la página. Sincroniza con el backend
  // (fuente externa), no deriva estado ya disponible en render (mismo patrón que Auditoria).
  useEffect(() => {
    // eslint-disable-next-line react-hooks/set-state-in-effect
    if (applied) void load(applied, page);
  }, [applied, page, load]);

  const handleSubmit = useCallback(
    (e: React.FormEvent) => {
      e.preventDefault();
      setPage(1);
      setApplied({ axis, text });
    },
    [axis, text],
  );

  const handleRetry = useCallback(() => {
    if (applied) void load(applied, page);
  }, [applied, page, load]);

  // 4 estados de UI. Antes de la primera búsqueda: prompt inicial (empty con guía).
  const notSearchedYet = applied === null;
  const isEmpty = entries !== null && entries.length === 0;
  const initialLoadFailed = error !== null && entries === null;

  const status: "loading" | "error" | "empty" | "ready" = fetching && entries === null
    ? "loading"
    : initialLoadFailed
      ? "error"
      : notSearchedYet || isEmpty
        ? "empty"
        : "ready";

  return (
    <div className="app-bg min-h-screen px-6 pt-6 pb-10 flex flex-col gap-4 text-[#162744] dark:text-white">
      <ModuleTitle
        title="LOG QX"
        subtitle="Trazabilidad de punta a punta de la integración Quipux: busca por placa, trámite o código QX y consulta la línea de tiempo de la radicación con su detalle técnico."
      />

      <form
        onSubmit={handleSubmit}
        className="flex flex-wrap items-end gap-2 shrink-0"
        role="search"
        aria-label="Búsqueda del LOG QX"
      >
        <label className="flex flex-col gap-1 text-[11px]">
          <span className="opacity-60">Buscar por</span>
          <select
            value={axis}
            onChange={(e) => setAxis(e.target.value as SearchAxis)}
            aria-label="Eje de búsqueda"
            className="rounded-lg border bg-white px-2 py-2 text-xs outline-none focus:border-[#557EFF] dark:bg-[#0B0F14]"
          >
            {(Object.keys(AXIS_META) as SearchAxis[]).map((k) => (
              <option key={k} value={k}>
                {AXIS_META[k].label}
              </option>
            ))}
          </select>
        </label>
        <label className="flex flex-col gap-1 text-[11px] flex-1 min-w-[200px]">
          <span className="opacity-60">{AXIS_META[axis].label}</span>
          <input
            type="text"
            value={text}
            onChange={(e) => setText(e.target.value)}
            placeholder={AXIS_META[axis].placeholder}
            aria-label={`Valor de búsqueda: ${AXIS_META[axis].label}`}
            className="rounded-lg border bg-white px-3 py-2 text-xs outline-none focus:border-[#557EFF] dark:bg-[#0B0F14]"
          />
        </label>
        <button
          type="submit"
          disabled={fetching}
          className="flex items-center gap-2 rounded-lg px-4 py-2 text-xs font-semibold text-white focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 disabled:opacity-50"
          style={{ background: "#557EFF" }}
        >
          <Search className="h-3.5 w-3.5" /> Buscar
        </button>
      </form>

      <UiStateBoundary
        status={status}
        skeletonRows={5}
        errorMessage={error ?? "No se pudo cargar el LOG QX."}
        onRetry={handleRetry}
        emptyMessage={
          notSearchedYet
            ? "Ingresa una placa, un id de trámite o un código QX y presiona Buscar para consultar el LOG QX."
            : "Ninguna radicación Quipux coincide con la búsqueda. Verifica el valor o cambia el eje de búsqueda."
        }
      >
        {entries && entries.length > 0 && (
          <>
            <ul className="space-y-3 shrink-0" aria-label="Radicaciones del LOG QX">
              {entries.map((entry) => (
                <LogQxEntryCard key={entry.id} entry={entry} />
              ))}
            </ul>
            {total > DEFAULT_PAGE_SIZE && (
              <Pagination
                page={page}
                pageSize={DEFAULT_PAGE_SIZE}
                totalCount={total}
                onPageChange={(p) => setPage(Math.max(1, p))}
                className="mt-0 justify-end"
              />
            )}
          </>
        )}
      </UiStateBoundary>
    </div>
  );
}

/** Tarjeta de una radicación: cabecera con estado/códigos + línea de tiempo de eventos. */
function LogQxEntryCard({ entry }: { entry: LogQxEntry }) {
  const st = STATUS_STYLE[entry.status] ?? {
    label: entry.status,
    tone: "neutral" as StatusTone,
  };

  return (
    <li className="rounded-2xl border bg-white dark:bg-[#0B0F14] border-[#DFE5ED] dark:border-white/10 overflow-hidden">
      <div className="flex flex-wrap items-center justify-between gap-3 px-4 py-3 border-b border-[#DFE5ED] dark:border-white/10">
        <div className="min-w-0">
          <div className="flex items-center gap-2 flex-wrap">
            <span className="font-semibold text-sm">{entry.referenceNumber}</span>
            <StatusBadge label={st.label} tone={st.tone} />
          </div>
          <p className="text-[11px] opacity-70 mt-0.5 truncate">
            {entry.procedureTypeName} · {entry.clientTenantName}
            {entry.plate ? ` · Placa ${entry.plate}` : ""}
          </p>
        </div>
        <div className="flex items-center gap-3 flex-wrap">
          <dl className="flex flex-wrap gap-x-4 gap-y-1 text-[11px]">
            <MetaField label="Código registro (QX)" value={entry.qxRegisterCode?.toString() ?? "—"} />
            <MetaField label="Estado trámite (QX)" value={formatEstadoTramite(entry.qxProcedureCode)} />
            <MetaField label="DIVIPO" value={entry.divipoCode ?? "—"} />
            <MetaField label="Creado" value={formatFecha(entry.createdAt)} />
          </dl>
          {/* HU #10796 (AC2) — correlación de vuelta al detalle del trámite. */}
          <Link
            href={`/tramites/${entry.procedureInstanceId}`}
            aria-label={`Ver el trámite ${entry.referenceNumber}`}
            className="inline-flex items-center gap-1 rounded-lg border border-[#DFE5ED] dark:border-white/15 px-2.5 py-1.5 text-[11px] font-semibold text-[#557EFF] hover:bg-[#557EFF]/[0.08] focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2"
          >
            <FileText className="h-3.5 w-3.5" aria-hidden="true" /> Ver trámite
          </Link>
        </div>
      </div>

      {entry.rejectionReason && (
        <p
          className="px-4 py-2 text-[11px] border-b border-[#DFE5ED] dark:border-white/10"
          style={{ color: "#FF4E00", background: "rgba(255,78,0,0.06)" }}
        >
          Motivo de rechazo: {entry.rejectionReason}
        </p>
      )}

      <ol className="divide-y divide-[#DFE5ED] dark:divide-white/10" aria-label="Línea de tiempo de la radicación">
        {entry.events.map((ev, i) => (
          <LogQxEventRow key={`${entry.id}-${i}`} event={ev} index={i} />
        ))}
      </ol>
    </li>
  );
}

function MetaField({ label, value }: { label: string; value: string }) {
  return (
    <div className="flex flex-col leading-tight">
      <dt className="opacity-55">{label}</dt>
      <dd className="font-mono">{value}</dd>
    </div>
  );
}

/** Fila de evento con detalle expandible (visor JSON + copia). */
function LogQxEventRow({ event, index }: { event: LogQxEvent; index: number }) {
  const [open, setOpen] = useState(false);
  const [copied, setCopied] = useState(false);
  const detailId = `logqx-event-detail-${index}`;

  const stageLabel = STAGE_LABEL[event.stage] ?? event.stage;
  const outcome = OUTCOME_STYLE[event.outcome] ?? { label: event.outcome, color: "#8A94A6" };
  const duration = formatDuration(event.durationMs);
  const hasDetail = event.detail !== null && event.detail !== undefined;
  const detailJson = hasDetail ? JSON.stringify(event.detail, null, 2) : "";

  const handleCopy = useCallback(async () => {
    try {
      await navigator.clipboard.writeText(detailJson);
      setCopied(true);
      setTimeout(() => setCopied(false), 1500);
    } catch {
      // Clipboard no disponible (p. ej. contexto no seguro): se ignora silenciosamente.
    }
  }, [detailJson]);

  return (
    <li>
      <button
        type="button"
        onClick={() => setOpen((v) => !v)}
        aria-expanded={open}
        aria-controls={detailId}
        className="w-full flex items-center gap-2 px-4 py-2.5 text-left hover:bg-black/[0.02] dark:hover:bg-white/[0.03] transition"
      >
        {open ? (
          <ChevronDown className="h-4 w-4 shrink-0 opacity-60" aria-hidden="true" />
        ) : (
          <ChevronRight className="h-4 w-4 shrink-0 opacity-60" aria-hidden="true" />
        )}
        <span className="text-xs font-medium min-w-0 truncate">{stageLabel}</span>
        <span className="text-[10px] font-semibold shrink-0" style={{ color: outcome.color }}>
          {outcome.label}
        </span>
        <span className="ml-auto flex items-center gap-3 text-[10px] opacity-70 shrink-0">
          {event.responseCode != null && <span className="font-mono">HTTP {event.responseCode}</span>}
          {duration && (
            <span className="inline-flex items-center gap-1">
              <Clock className="h-3 w-3" aria-hidden="true" />
              {duration}
            </span>
          )}
          {event.origin && (
            <span className="inline-flex items-center gap-1">
              <Cpu className="h-3 w-3" aria-hidden="true" />
              {event.origin}
            </span>
          )}
          <span>{formatFecha(event.occurredAt)}</span>
        </span>
      </button>

      {open && (
        <div id={detailId} className="px-4 pb-3 pl-10">
          {hasDetail ? (
            <div className="relative rounded-lg border border-[#DFE5ED] dark:border-white/10 bg-[#F5F7FB] dark:bg-white/5">
              <button
                type="button"
                onClick={handleCopy}
                aria-label="Copiar detalle JSON"
                className="absolute right-2 top-2 inline-flex items-center gap-1 rounded-md border border-[#DFE5ED] dark:border-white/15 bg-white dark:bg-[#0B0F14] px-2 py-1 text-[10px] font-semibold"
              >
                {copied ? (
                  <>
                    <Check className="h-3 w-3" aria-hidden="true" /> Copiado
                  </>
                ) : (
                  <>
                    <Copy className="h-3 w-3" aria-hidden="true" /> Copiar
                  </>
                )}
              </button>
              <pre className="overflow-x-auto p-3 pr-20 text-[11px] font-mono leading-relaxed whitespace-pre">
                {detailJson}
              </pre>
            </div>
          ) : (
            <p className="text-[11px] italic opacity-60">Sin payload disponible.</p>
          )}
        </div>
      )}
    </li>
  );
}
