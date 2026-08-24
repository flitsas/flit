"use client";

import { useCallback, useEffect, useRef, useState } from "react";
import { ChevronRight, Download } from "lucide-react";
import { Pagination } from "@/components/atom/Pagination";
import { UiStateBoundary } from "@/components/admin/UiStateBoundary";
import { fetchLogQxEventos, type LogQxEvent, type LogQxEventosPage } from "@/lib/api/admin-log-qx";
import {
  CODIGO_QX,
  codigoQx,
  etapa,
  formatDuracion,
  formatFecha,
  origen,
  resultado,
} from "@/lib/logqx/labels";
import { bogotaClock, buildXlsx, XLSX_MIME, type XlsxCell } from "@/lib/xlsx";

/**
 * Pestaña «Log completo» (HU #11790). Todos los eventos de la radicación, filtrados y paginados EN
 * SERVIDOR.
 *
 * El interruptor de ocultar consultas viene ACTIVO: es lo que convierte las 1.065 filas del caso de
 * referencia en las cinco que dicen algo. Apagarlo devuelve la totalidad — no se pierde ningún
 * registro, solo deja de mostrarse por defecto, y se informa cuántos se ocultaron para que una
 * lista corta no parezca pérdida de datos.
 */

const PAGE_SIZE = 50;

export function LogCompleto({ submissionId }: { submissionId: string }) {
  const [ocultarSinNovedad, setOcultarSinNovedad] = useState(true);
  const [soloErrores, setSoloErrores] = useState(false);
  const [page, setPage] = useState(1);

  const [data, setData] = useState<LogQxEventosPage | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [fetching, setFetching] = useState(false);
  const [abierto, setAbierto] = useState<number | null>(null);

  const reqIdRef = useRef(0);

  const load = useCallback(
    async (opts: { ocultar: boolean; errores: boolean; page: number }) => {
      const reqId = ++reqIdRef.current;
      setFetching(true);
      try {
        const res = await fetchLogQxEventos(submissionId, {
          ocultarSinNovedad: opts.ocultar,
          soloErrores: opts.errores,
          page: opts.page,
          pageSize: PAGE_SIZE,
        });
        if (reqId !== reqIdRef.current) return;
        setData(res);
        setError(null);
      } catch (err) {
        if (reqId !== reqIdRef.current) return;
        setData(null);
        setError(err instanceof Error ? err.message : "No se pudo cargar el log.");
      } finally {
        if (reqId === reqIdRef.current) setFetching(false);
      }
    },
    [submissionId],
  );

  useEffect(() => {
    // eslint-disable-next-line react-hooks/set-state-in-effect
    void load({ ocultar: ocultarSinNovedad, errores: soloErrores, page });
  }, [load, ocultarSinNovedad, soloErrores, page]);

  const cambiar = useCallback((fn: () => void) => {
    setAbierto(null);
    setPage(1);
    fn();
  }, []);

  const status: "loading" | "error" | "empty" | "ready" =
    fetching && data === null
      ? "loading"
      : error !== null
        ? "error"
        : data && data.data.length === 0
          ? "empty"
          : data
            ? "ready"
            : "empty";

  return (
    <div>
      <div className="flex flex-wrap items-center gap-3 border-b border-[#DDE5F0] bg-[#F4F6FA] px-4 py-3 dark:border-white/10 dark:bg-white/5">
        <label className="inline-flex cursor-pointer select-none items-center gap-2.5 text-[12.5px] font-medium">
          <input
            type="checkbox"
            checked={ocultarSinNovedad}
            onChange={(e) => cambiar(() => setOcultarSinNovedad(e.target.checked))}
            className="h-4 w-4 accent-[#4F74C9]"
          />
          Ocultar consultas sin novedad
        </label>

        {ocultarSinNovedad && data && data.ocultosSinNovedad > 0 && (
          <span className="rounded-full bg-[#F05A35]/15 px-2.5 py-1 font-mono text-[11.5px] tabular-nums text-[#D9521F]">
            {data.ocultosSinNovedad.toLocaleString("es-CO")} consultas sin novedad ocultas
          </span>
        )}

        <span className="flex-1" />

        <span className="inline-flex overflow-hidden rounded-lg border border-[#D9DEE8] dark:border-white/15">
          <button
            type="button"
            aria-pressed={!soloErrores}
            onClick={() => cambiar(() => setSoloErrores(false))}
            className={`px-3 py-1.5 text-[12px] ${!soloErrores ? "bg-[#4F74C9] font-semibold text-white" : "opacity-70"}`}
          >
            Todo
          </button>
          <button
            type="button"
            aria-pressed={soloErrores}
            onClick={() => cambiar(() => setSoloErrores(true))}
            className={`border-l border-[#D9DEE8] px-3 py-1.5 text-[12px] dark:border-white/15 ${
              soloErrores ? "bg-[#4F74C9] font-semibold text-white" : "opacity-70"
            }`}
          >
            Solo errores
          </button>
        </span>

        <button
          type="button"
          onClick={() => exportar(data?.data ?? [], submissionId, ocultarSinNovedad, data)}
          disabled={!data || data.data.length === 0}
          className="inline-flex items-center gap-1.5 rounded-lg border border-[#D9DEE8] px-3 py-1.5 text-[12px] font-medium opacity-80 hover:opacity-100 disabled:opacity-40 dark:border-white/15"
        >
          <Download className="h-3.5 w-3.5" aria-hidden="true" /> Exportar
        </button>
      </div>

      <UiStateBoundary
        status={status}
        skeletonRows={8}
        errorMessage={error ?? "No se pudo cargar el log."}
        onRetry={() => void load({ ocultar: ocultarSinNovedad, errores: soloErrores, page })}
        emptyMessage={
          soloErrores
            ? "Esta radicación no registró ningún error."
            : "Ningún evento coincide con el filtro. Desactiva «ocultar consultas sin novedad» para ver la totalidad."
        }
      >
        {data && data.data.length > 0 && (
          <>
            <div className="overflow-x-auto">
              <table className="w-full min-w-[820px] border-collapse">
                <thead>
                  <tr className="bg-[#F4F6FA] dark:bg-white/5">
                    <th className={thCls} style={{ width: 28 }} />
                    <th className={thCls}>Fecha y hora</th>
                    <th className={thCls}>Etapa</th>
                    <th className={thCls}>Resultado</th>
                    <th className={thCls}>Código</th>
                    <th className={thCls}>Duración</th>
                    <th className={thCls}>Origen</th>
                  </tr>
                </thead>
                <tbody>
                  {data.data.map((ev, i) => (
                    <FilaEvento
                      key={`${ev.occurredAt}-${i}`}
                      evento={ev}
                      indice={i}
                      abierto={abierto === i}
                      onToggle={() => setAbierto(abierto === i ? null : i)}
                    />
                  ))}
                </tbody>
              </table>
            </div>

            <div className="px-4 pb-3">
              <Pagination
                page={page}
                pageSize={PAGE_SIZE}
                totalCount={data.totalCount}
                onPageChange={(p) => {
                  setAbierto(null);
                  setPage(Math.max(1, p));
                }}
              />
              <p className="mt-1 text-center text-[11px] opacity-55">
                {data.totalCount.toLocaleString("es-CO")} de{" "}
                {data.totalEventos.toLocaleString("es-CO")} eventos de esta radicación
              </p>
            </div>
          </>
        )}
      </UiStateBoundary>
    </div>
  );
}

const thCls =
  "text-left text-[10px] font-bold uppercase tracking-wider opacity-55 px-3 py-2.5 border-b border-[#DDE5F0] dark:border-white/10 whitespace-nowrap";

const tdCls = "px-3 py-2 border-b border-[#DDE5F0] dark:border-white/10 text-[12.5px]";

function FilaEvento({
  evento,
  indice,
  abierto,
  onToggle,
}: {
  evento: LogQxEvent;
  indice: number;
  abierto: boolean;
  onToggle: () => void;
}) {
  const res = resultado(evento.outcome);
  const esError = evento.outcome !== "ok";

  return (
    <>
      <tr
        className={`cursor-pointer ${abierto ? "bg-[#4F74C9]/[0.06]" : "hover:bg-[#4F74C9]/[0.04]"}`}
        onClick={onToggle}
        tabIndex={0}
        role="button"
        aria-expanded={abierto}
        onKeyDown={(e) => {
          if (e.key === "Enter" || e.key === " ") {
            e.preventDefault();
            onToggle();
          }
        }}
      >
        <td className={tdCls}>
          <ChevronRight
            className={`h-3.5 w-3.5 opacity-50 transition-transform ${abierto ? "rotate-90" : ""}`}
            aria-hidden="true"
          />
        </td>
        <td className={`${tdCls} font-mono tabular-nums whitespace-nowrap`}>
          {formatFecha(evento.occurredAt)}
        </td>
        <td className={tdCls}>{etapa(evento.stage)}</td>
        <td className={tdCls}>
          <span
            className={`text-[10px] font-bold uppercase ${
              esError
                ? evento.outcome === "error_definitivo"
                  ? "text-[#D3352A]"
                  : "text-[#D9521F]"
                : "text-[#5FA82C]"
            }`}
          >
            {res.label}
          </span>
        </td>
        {/* Código de negocio de Quipux con su significado. NUNCA rotulado como HTTP. */}
        <td className={`${tdCls} whitespace-nowrap`}>{codigoQx(evento.responseCode)}</td>
        <td className={`${tdCls} font-mono tabular-nums`}>
          {formatDuracion(evento.durationMs) ?? "—"}
        </td>
        <td className={tdCls}>{origen(evento.origin)}</td>
      </tr>
      {abierto && (
        <tr>
          <td colSpan={7} className="border-b border-[#DDE5F0] bg-[#EEF3FB] p-0 dark:border-white/10 dark:bg-white/[0.03]">
            <Payloads evento={evento} indice={indice} />
          </td>
        </tr>
      )}
    </>
  );
}

/**
 * Lo enviado y lo recibido, LADO A LADO. La v1 los partía en dos modales separados que no se podían
 * ver a la vez, que es justo lo que hace falta para entender un fallo.
 *
 * El detail que llega ya viene sanitizado y enmascarado por el backend; aquí solo se presenta.
 */
function Payloads({ evento, indice }: { evento: LogQxEvent; indice: number }) {
  const [crudo, setCrudo] = useState(false);
  const detalle = evento.detail;

  if (!detalle) {
    return (
      <div className="px-5 py-6 text-center text-[12.5px] italic opacity-55">
        Sin payload disponible para este evento.
      </div>
    );
  }

  const { enviado, recibido } = repartir(detalle);

  return (
    <div className="grid gap-3.5 px-4 py-3.5 md:grid-cols-2">
      <Columna titulo="Lo que enviamos" etiqueta="Rq" tono="info" datos={enviado} />
      <Columna titulo="Lo que respondió Quipux" etiqueta="Rs" tono="ok" datos={recibido}>
        <button
          type="button"
          onClick={() => setCrudo((v) => !v)}
          className="ml-auto text-[11px] font-medium text-[#4F74C9] hover:underline"
        >
          {crudo ? "ocultar original" : "ver original"}
        </button>
      </Columna>
      {crudo && (
        <pre
          id={`logqx-raw-${indice}`}
          className="overflow-x-auto rounded-[9px] border border-[#DDE5F0] bg-white p-3 font-mono text-[11.5px] leading-relaxed opacity-80 md:col-span-2 dark:border-white/10 dark:bg-[#0B0F14]"
        >
          {JSON.stringify(detalle, null, 2)}
        </pre>
      )}
    </div>
  );
}

/**
 * Reparte el detail en «enviado» y «recibido». El backend guarda un único jsonb por evento, así que
 * la separación es por convención de claves: las de respuesta son las que Quipux devuelve.
 */
function repartir(detalle: Record<string, unknown>): {
  enviado: [string, unknown][];
  recibido: [string, unknown][];
} {
  const CLAVES_RESPUESTA = new Set([
    "codigo", "descripcion", "estado_tramite", "mensaje", "motivo", "status", "duration_ms",
  ]);

  const enviado: [string, unknown][] = [];
  const recibido: [string, unknown][] = [];

  for (const [k, v] of Object.entries(detalle)) {
    // `origen` es metadato de FLIT, no de ninguno de los dos lados.
    if (k === "origen") continue;
    (CLAVES_RESPUESTA.has(k) ? recibido : enviado).push([k, v]);
  }

  return { enviado, recibido };
}

function Columna({
  titulo,
  etiqueta,
  tono,
  datos,
  children,
}: {
  titulo: string;
  etiqueta: string;
  tono: "info" | "ok";
  datos: [string, unknown][];
  children?: React.ReactNode;
}) {
  return (
    <div className="min-w-0 overflow-hidden rounded-[9px] border border-[#DDE5F0] bg-white dark:border-white/10 dark:bg-[#0B0F14]">
      <div className="flex items-center gap-2 border-b border-[#DDE5F0] bg-[#F4F6FA] px-3 py-2 dark:border-white/10 dark:bg-white/5">
        <span
          className={`rounded px-1.5 py-0.5 font-mono text-[10px] font-bold ${
            tono === "info" ? "bg-[#4F74C9]/15 text-[#4F74C9]" : "bg-[#70CF3A]/20 text-[#5FA82C]"
          }`}
        >
          {etiqueta}
        </span>
        <h4 className="text-[12px] font-semibold opacity-75">{titulo}</h4>
        {children}
      </div>
      {datos.length === 0 ? (
        <p className="px-3 py-4 text-center text-[12px] italic opacity-50">Sin datos registrados.</p>
      ) : (
        <table className="w-full border-collapse">
          <tbody>
            {datos.map(([k, v]) => (
              <tr key={k}>
                <td className="w-px whitespace-nowrap border-b border-[#DDE5F0] px-3 py-1.5 text-[12px] font-medium opacity-70 last:border-0 dark:border-white/10">
                  {k}
                </td>
                <td className="break-all border-b border-[#DDE5F0] px-3 py-1.5 font-mono text-[11.5px] dark:border-white/10">
                  {formatValor(v)}
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
    </div>
  );
}

function formatValor(v: unknown): string {
  if (v === null || v === undefined) return "—";
  if (typeof v === "object") return JSON.stringify(v);
  return String(v);
}

/**
 * Exporta lo que está en pantalla, con los filtros vigentes, usando el escritor XLSX propio del
 * proyecto (`lib/xlsx.ts`, sin dependencias externas).
 *
 * Dos decisiones que evitan que el archivo mienta:
 *  · Las fechas van como `bogotaClock`, no como texto ni como UTC. Excel no guarda husos, así que
 *    un instante en UTC aparecería desplazado respecto de lo que la pantalla muestra en Bogotá.
 *  · Si el interruptor está puesto, se anota AL PIE cuántas consultas quedaron fuera. El .xlsx es
 *    lo que se reenvía por correo: sin ese aviso, quien lo recibe cuenta las filas y concluye que
 *    la radicación solo tuvo esos eventos.
 */
function exportar(
  eventos: LogQxEvent[],
  submissionId: string,
  ocultarSinNovedad: boolean,
  page: LogQxEventosPage | null,
): void {
  const rows: XlsxCell[][] = eventos.map((e) => [
    bogotaClock(e.occurredAt),
    etapa(e.stage),
    resultado(e.outcome).label,
    e.responseCode ?? null,
    e.responseCode != null ? (CODIGO_QX[e.responseCode] ?? "") : "",
    e.durationMs ?? null,
    origen(e.origin),
  ]);

  const notes: string[] = [];
  if (page) {
    notes.push(
      `Exportado desde el LOG QX · radicación ${submissionId} · ${page.totalCount} de ${page.totalEventos} eventos.`,
    );
    if (ocultarSinNovedad && page.ocultosSinNovedad > 0) {
      notes.push(
        `Se ocultaron ${page.ocultosSinNovedad} consultas de estado sin novedad. `
          + "Para incluirlas, desactiva «ocultar consultas sin novedad» y vuelve a exportar.",
      );
    }
  }

  const xlsx = buildXlsx({
    name: "Log QX",
    columns: [
      { header: "Fecha y hora", width: 20 },
      { header: "Etapa", width: 28 },
      { header: "Resultado", width: 18 },
      { header: "Código", width: 9 },
      { header: "Significado", width: 34 },
      { header: "Duración (ms)", width: 14 },
      { header: "Origen", width: 20 },
    ],
    rows,
    notes,
  });

  const blob = new Blob([xlsx as BlobPart], { type: XLSX_MIME });
  const url = URL.createObjectURL(blob);
  const anchor = document.createElement("a");
  anchor.href = url;
  anchor.download = `log-qx-${submissionId}.xlsx`;
  anchor.click();
  URL.revokeObjectURL(url);
}
