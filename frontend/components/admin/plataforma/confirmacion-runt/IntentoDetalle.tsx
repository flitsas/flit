"use client";

import { useEffect, useMemo, useState } from "react";
import { Ban, Check, CircleDashed, Hourglass, X, type LucideIcon } from "lucide-react";
import { InlineAlert } from "@/components/atom/InlineAlert";
import { UiStateBoundary } from "@/components/admin/UiStateBoundary";
import { useToast } from "@/components/admin/Toast";
import {
  ConsultNowConflictError,
  consultRuntNow,
  getRuntConfirmationAttempt,
  getRuntConfirmationAttemptRaw,
  RUNT_VERDICT_LABEL,
  type RuntConfirmationAttemptDetail,
  type RuntConfirmationAttemptRow,
  type RuntConfirmationVerdict,
} from "@/lib/api/admin-runt-confirmation";
import { formatFechaHora } from "@/lib/format/date";
import { proveedorLabel, QUERY_KIND_LABEL } from "./historial-columns";
import { leerRespuestaRunt, type RuntRespuestaVista } from "./runt-respuesta";

type Pestana = "veredicto" | "respuesta" | "json";

const PESTANAS: Array<{ id: Pestana; label: string }> = [
  { id: "veredicto", label: "Veredicto" },
  { id: "respuesta", label: "Respuesta del RUNT" },
  { id: "json", label: "JSON" },
];

/** Mismo mapa de tonos que los hitos de Trazabilidad ICT: color + ícono, y el texto siempre al lado. */
const TONO: Record<RuntConfirmationVerdict, { fg: string; bg: string; Icon: LucideIcon }> = {
  confirmed: { fg: "#15803D", bg: "rgba(34,197,94,0.14)", Icon: Check },
  pending: { fg: "#C2410C", bg: "rgba(255,78,0,0.14)", Icon: Hourglass },
  discrepancy: { fg: "#991B1B", bg: "rgba(153,27,27,0.14)", Icon: X },
  unverifiable: { fg: "#64748B", bg: "rgba(148,163,184,0.16)", Icon: CircleDashed },
  error: { fg: "#6B21A8", bg: "rgba(107,33,168,0.14)", Icon: Ban },
};

const FLAG_LABEL: Record<string, string> = {
  discrepancia: "Discrepancia",
  no_verificable: "No verificable",
  tope: "Tope de intentos alcanzado",
};

/** Qué pasa después con este trámite, en palabras del proceso (el «Siguiente» del artifact). */
function siguiente(row: RuntConfirmationAttemptRow, detalle: RuntConfirmationAttemptDetail | null): string {
  if (detalle?.runtConfirmedAt) return "Confirmado en el RUNT: sale de las corridas.";
  switch (row.verdict) {
    case "confirmed":
      return "Confirmado en el RUNT: sale de las corridas.";
    case "error":
      return "El error del proveedor no cuenta como intento; el trámite entra en la siguiente corrida.";
    case "unverifiable":
      return "No se reintenta automáticamente. Se puede lanzar «Consultar ahora».";
    case "discrepancy":
      return detalle?.runtFlag === "tope"
        ? "Llegó al tope de intentos: ya no se consulta en las corridas; queda «Consultar ahora»."
        : "Marcado en discrepancia. Sigue consultándose en las corridas hasta el tope, por si el organismo lo radica de nuevo.";
    default:
      return detalle?.runtFlag === "tope"
        ? "Llegó al tope de intentos: ya no se consulta en las corridas; queda «Consultar ahora»."
        : "Se reintenta en la siguiente corrida.";
  }
}

/**
 * Detalle expandido de un intento (HU #12311 AC3/AC4), con la misma armazón que el detalle de
 * Trazabilidad ICT: tres pestañas —el veredicto en palabras, la respuesta del RUNT en secciones de
 * negocio y el JSON tal cual— y «Consultar ahora» con confirmación inline (nunca `window.confirm`)
 * cuando el trámite sigue aprobado y sin confirmar.
 */
export function IntentoDetalle({
  row,
  onNuevoIntento,
  onVerTramite,
}: {
  row: RuntConfirmationAttemptRow;
  onNuevoIntento: () => void;
  onVerTramite: () => void;
}) {
  const toast = useToast();
  const [pestana, setPestana] = useState<Pestana>("veredicto");
  const [detalle, setDetalle] = useState<RuntConfirmationAttemptDetail | null>(null);
  const [detalleError, setDetalleError] = useState(false);
  const [raw, setRaw] = useState<{ primary: unknown; seller?: unknown } | null>(null);
  const [rawEstado, setRawEstado] = useState<"idle" | "loading" | "error">("idle");
  const [confirmando, setConfirmando] = useState(false);
  const [consultando, setConsultando] = useState(false);
  const [conflicto, setConflicto] = useState<string | null>(null);

  useEffect(() => {
    const controller = new AbortController();
    getRuntConfirmationAttempt(row.id, controller.signal)
      .then((d) => {
        if (!controller.signal.aborted) setDetalle(d);
      })
      .catch(() => {
        if (!controller.signal.aborted) setDetalleError(true);
      });
    return () => controller.abort();
  }, [row.id]);

  // El crudo se pide la primera vez que se abre una pestaña que lo necesita, no antes.
  useEffect(() => {
    if (pestana === "veredicto" || raw !== null || rawEstado === "loading" || !row.hasRaw) return;
    const controller = new AbortController();
    // eslint-disable-next-line react-hooks/set-state-in-effect -- carga perezosa vía API al abrir la pestaña (patrón DetalleTramiteIct)
    setRawEstado("loading");
    getRuntConfirmationAttemptRaw(row.id, controller.signal)
      .then((json) => {
        if (controller.signal.aborted) return;
        setRaw(json);
        setRawEstado("idle");
      })
      .catch(() => {
        if (!controller.signal.aborted) setRawEstado("error");
      });
    return () => controller.abort();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [pestana, row.id, row.hasRaw]);

  const consultarAhora = async () => {
    setConsultando(true);
    setConflicto(null);
    try {
      const { attempt } = await consultRuntNow(row.procedureInstanceId);
      toast.show(attempt ? `Consulta realizada: ${RUNT_VERDICT_LABEL[attempt.verdict] ?? attempt.verdict}` : "Consulta realizada", "success");
      setConfirmando(false);
      onNuevoIntento();
    } catch (err) {
      if (err instanceof ConsultNowConflictError) setConflicto(err.message);
      else toast.show(err instanceof Error ? err.message : "No se pudo consultar ahora.", "error");
      setConfirmando(false);
    } finally {
      setConsultando(false);
    }
  };

  const puedeConsultar = detalle !== null && detalle.procedureStatus === "aprobado" && detalle.runtConfirmedAt === null;
  const tono = TONO[row.verdict] ?? TONO.pending;
  const Icon = tono.Icon;

  return (
    <div
      id={`intento-detalle-${row.id}`}
      className="mx-1 rounded-2xl border border-[#557EFF]/30 bg-white dark:border-white/10 dark:bg-[#0B0F14]"
      data-testid="intento-detalle"
    >
      <div role="tablist" aria-label={`Detalle del intento ${row.attemptNo} del trámite ${row.referenceNumber}`} className="flex flex-wrap gap-1 border-b border-[#DFE5ED] px-3 dark:border-white/10">
        {PESTANAS.map((p) => {
          const activa = pestana === p.id;
          const deshabilitada = p.id !== "veredicto" && !row.hasRaw;
          return (
            <button
              key={p.id}
              type="button"
              role="tab"
              aria-selected={activa}
              disabled={deshabilitada}
              title={deshabilitada ? "Este intento no guardó respuesta del proveedor" : undefined}
              onClick={() => setPestana(p.id)}
              className={`relative whitespace-nowrap px-3 py-2.5 text-xs font-semibold transition disabled:opacity-35 ${activa ? "text-[#557EFF]" : "opacity-60 hover:opacity-100"}`}
            >
              {p.label}
              {activa && <span className="absolute inset-x-2 -bottom-px h-0.5 rounded-t bg-[#557EFF]" aria-hidden="true" />}
            </button>
          );
        })}
      </div>

      <div className="p-4 text-xs text-[#162744] dark:text-white">
        {pestana === "veredicto" && (
          <div className="flex flex-col gap-4">
            {/* El veredicto y su porqué, juntos y arriba: es lo que vino a leer quien abrió la fila. */}
            <div className="grid grid-cols-[32px_1fr] gap-3">
              <span className="grid h-8 w-8 place-items-center rounded-full" style={{ background: tono.bg }} aria-hidden="true">
                <Icon className="h-4 w-4" style={{ color: tono.fg }} />
              </span>
              <div className="flex flex-col gap-1">
                <span className="text-sm font-semibold" style={{ color: tono.fg }}>
                  {RUNT_VERDICT_LABEL[row.verdict] ?? row.verdict}
                  <span className="ml-2 font-mono text-[11px] font-medium tabular-nums opacity-55">intento {row.attemptNo}</span>
                </span>
                <p className="text-[13px] leading-relaxed" data-testid="intento-motivo">{row.reasonText}</p>
                <p className="text-[11px] text-[#59677D] dark:text-white/60">
                  <span className="font-semibold">Siguiente: </span>
                  {siguiente(row, detalle)}
                </p>
              </div>
            </div>

            <div className="flex flex-wrap gap-x-8 gap-y-2 rounded-xl border border-[#DFE5ED] bg-[#F4F7FC] px-4 py-3 dark:border-white/10 dark:bg-white/[0.03]">
              <Dato label="Consultado" value={formatFechaHora(row.queriedAt)} mono />
              <Dato label="Proveedor" value={proveedorLabel(row.providerKey)} />
              <Dato label="Cómo" value={QUERY_KIND_LABEL[row.queryKind] ?? row.queryKind} />
              <Dato label="Origen" value={row.runId === null ? "Re-evaluación" : row.requestedBy ? "Manual (Consultar ahora)" : "Corrida programada"} />
              {row.flagApplied ? <Dato label="Marca dejada" value={FLAG_LABEL[row.flagApplied] ?? row.flagApplied} /> : null}
              {detalle ? (
                <>
                  <Dato label="Estado del trámite" value={detalle.procedureStatus ?? "—"} />
                  <Dato label="Confirmado en RUNT" value={detalle.runtConfirmedAt ? formatFechaHora(detalle.runtConfirmedAt) : "No"} />
                  <Dato label="Intentos con veredicto" value={String(detalle.runtAttempts)} mono />
                </>
              ) : null}
            </div>

            {detalleError ? (
              <InlineAlert tone="warning" compact>
                No se pudo cargar el estado actual del trámite; el motivo y la respuesta siguen disponibles.
              </InlineAlert>
            ) : null}

            <div className="flex flex-wrap items-center gap-2">
              <button
                type="button"
                onClick={onVerTramite}
                className="inline-flex items-center rounded-lg bg-[#557EFF]/10 px-3 py-2 text-[11px] font-semibold text-[#557EFF] hover:bg-[#557EFF]/20 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[#557EFF]"
              >
                Ver todos los intentos del trámite
              </button>

              {puedeConsultar && !confirmando ? (
                <button
                  type="button"
                  onClick={() => setConfirmando(true)}
                  data-testid="consultar-ahora"
                  className="ml-auto rounded-full bg-gradient-to-r from-[#22D3C5] to-[#557EFF] px-3 py-2 text-[11px] font-semibold text-white focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[#557EFF] focus-visible:ring-offset-2"
                >
                  Consultar ahora
                </button>
              ) : null}
              {confirmando ? (
                <span className="ml-auto flex flex-wrap items-center gap-2" role="group" aria-label="Confirmar consulta manual">
                  <span className="text-[11px]">Consulta inmediata al proveedor configurado. ¿Continuar?</span>
                  <button
                    type="button"
                    onClick={() => void consultarAhora()}
                    disabled={consultando}
                    data-testid="consultar-ahora-confirmar"
                    className="rounded-full bg-gradient-to-r from-[#22D3C5] to-[#557EFF] px-3 py-1.5 text-[11px] font-semibold text-white disabled:opacity-50"
                  >
                    {consultando ? "Consultando…" : "Sí, consultar"}
                  </button>
                  <button
                    type="button"
                    onClick={() => setConfirmando(false)}
                    disabled={consultando}
                    className="rounded-full border border-[#DFE5ED] px-3 py-1.5 text-[11px] font-semibold disabled:opacity-50 dark:border-white/10"
                  >
                    Cancelar
                  </button>
                </span>
              ) : null}
            </div>

            {conflicto ? (
              <InlineAlert tone="warning" compact>
                {conflicto}
              </InlineAlert>
            ) : null}
          </div>
        )}

        {pestana === "respuesta" && (
          <UiStateBoundary
            status={rawEstado === "loading" ? "loading" : rawEstado === "error" ? "error" : raw ? "ready" : "empty"}
            skeletonRows={3}
            errorMessage="No se pudo cargar la respuesta del proveedor."
            emptyMessage="Este intento no guardó respuesta del proveedor."
          >
            {raw ? <RespuestaRunt raw={raw} queryKind={row.queryKind} /> : null}
          </UiStateBoundary>
        )}

        {pestana === "json" && (
          <UiStateBoundary
            status={rawEstado === "loading" ? "loading" : rawEstado === "error" ? "error" : raw ? "ready" : "empty"}
            skeletonRows={3}
            errorMessage="No se pudo cargar la respuesta cruda del proveedor."
            emptyMessage="Este intento no guardó respuesta del proveedor."
          >
            {raw ? (
              <div className="flex flex-col gap-3">
                <JsonCrudo titulo={raw.seller !== undefined ? "Consulta con el documento del comprador" : "Respuesta del proveedor"} valor={raw.primary} />
                {raw.seller !== undefined ? <JsonCrudo titulo="Consulta con el documento del vendedor" valor={raw.seller} /> : null}
                <p className="text-[10px] opacity-55">Tal como lo entregó el proveedor, sin credenciales ni adjuntos binarios.</p>
              </div>
            ) : null}
          </UiStateBoundary>
        )}
      </div>
    </div>
  );
}

function Dato({ label, value, mono = false }: { label: string; value: string; mono?: boolean }) {
  return (
    <div className="flex flex-col">
      <span className="text-[10px] font-semibold uppercase tracking-wider opacity-55">{label}</span>
      <span className={`text-xs font-semibold ${mono ? "font-mono tabular-nums" : ""}`}>{value}</span>
    </div>
  );
}

function JsonCrudo({ titulo, valor }: { titulo: string; valor: unknown }) {
  return (
    <div className="flex flex-col gap-1">
      <span className="text-[10px] font-bold uppercase tracking-wider text-[#557EFF]">{titulo}</span>
      <pre
        tabIndex={0}
        aria-label={titulo}
        data-testid="intento-raw"
        className="max-h-72 overflow-auto rounded-lg border border-[#DFE5ED] bg-[#F4F7FC] p-3 font-mono text-[11px] leading-relaxed dark:border-white/10 dark:bg-white/[0.03]"
      >
        {JSON.stringify(valor, null, 2)}
      </pre>
    </div>
  );
}

/** La respuesta del RUNT en secciones de negocio; en traspaso, un bloque por consulta. */
function RespuestaRunt({ raw, queryKind }: { raw: { primary: unknown; seller?: unknown }; queryKind: string }) {
  const bloques = useMemo(() => {
    const out: Array<{ titulo: string; vista: RuntRespuestaVista }> = [
      { titulo: queryKind === "plate_pair" ? "Con el documento del comprador" : "Respuesta del proveedor", vista: leerRespuestaRunt(raw.primary) },
    ];
    if (raw.seller !== undefined) out.push({ titulo: "Con el documento del vendedor", vista: leerRespuestaRunt(raw.seller) });
    return out;
  }, [raw, queryKind]);

  return (
    <div className="flex flex-col gap-5">
      {bloques.map((b) => (
        <RespuestaBloque key={b.titulo} titulo={b.titulo} vista={b.vista} conTitulo={bloques.length > 1} />
      ))}
    </div>
  );
}

function RespuestaBloque({ titulo, vista, conTitulo }: { titulo: string; vista: RuntRespuestaVista; conTitulo: boolean }) {
  if (vista.resultado !== "encontrado") {
    return (
      <section className="flex flex-col gap-2">
        {conTitulo ? <h4 className="text-[10px] font-bold uppercase tracking-wider text-[#557EFF]">{titulo}</h4> : null}
        <p className="rounded-lg px-3 py-2 text-[11px] font-medium" style={{ background: "rgba(255,78,0,0.10)", color: "#C2410C" }}>
          {vista.resultado === "no_encontrado"
            ? `El proveedor no encontró el vehículo con ese documento${vista.mensaje ? `: «${vista.mensaje}»` : "."}`
            : "La respuesta no tiene la forma esperada; mira la pestaña JSON."}
        </p>
      </section>
    );
  }

  return (
    <div className="flex flex-col gap-3">
      {conTitulo ? <h4 className="text-[10px] font-bold uppercase tracking-wider text-[#557EFF]">{titulo}</h4> : null}
      <div className="grid gap-3 lg:grid-cols-[minmax(240px,1fr)_2fr]">
        {/* Secciones de negocio, no un volcado JSON (mismo patrón que Trazabilidad ICT → Datos). */}
        <section className="flex flex-col gap-2 rounded-xl border border-[#DFE5ED] p-3 dark:border-white/10">
          <h5 className="text-[10px] font-bold uppercase tracking-wider text-[#557EFF]">Vehículo en el RUNT</h5>
          {vista.vehiculo.map((d) => (
            <div key={d.etiqueta} className="flex justify-between gap-3 border-b border-[#DFE5ED] pb-1.5 text-[11px] last:border-b-0 last:pb-0 dark:border-white/10">
              <span className="shrink-0 opacity-55">{d.etiqueta}</span>
              <span className="min-w-0 break-words text-right font-medium">{d.valor ?? "—"}</span>
            </div>
          ))}
        </section>

        <section className="flex min-w-0 flex-col gap-2 rounded-xl border border-[#DFE5ED] p-3 dark:border-white/10">
          <h5 className="text-[10px] font-bold uppercase tracking-wider text-[#557EFF]">
            Solicitudes ante el RUNT
            <span className="ml-2 font-mono font-medium normal-case tracking-normal opacity-55">{vista.solicitudes.length}</span>
          </h5>
          {vista.mostrarSolicitudes?.toUpperCase() !== "SI" ? (
            <p className="text-[11px] italic opacity-55">
              El RUNT no expone el historial de solicitudes de este vehículo (mostrarSolicitudes = {vista.mostrarSolicitudes ?? "vacío"}).
            </p>
          ) : vista.solicitudes.length === 0 ? (
            <p className="text-[11px] italic opacity-55">Sin solicitudes registradas.</p>
          ) : (
            <div className="overflow-x-auto">
              <table className="w-full min-w-[520px] text-[11px]">
                <thead>
                  <tr className="text-left text-[10px] font-semibold uppercase opacity-55">
                    <th className="px-2 py-1">Fecha</th>
                    <th className="px-2 py-1">Trámite(s)</th>
                    <th className="px-2 py-1">Estado</th>
                    <th className="px-2 py-1">Entidad</th>
                    <th className="px-2 py-1">N.º</th>
                  </tr>
                </thead>
                <tbody>
                  {vista.solicitudes.map((s) => (
                    <tr key={`${s.noSolicitud}-${s.fecha}`} className="border-t border-[#DFE5ED] dark:border-white/10">
                      <td className="px-2 py-1.5 font-mono tabular-nums">{s.fecha}</td>
                      <td className="px-2 py-1.5 font-medium">{s.tramites}</td>
                      <td className="px-2 py-1.5">
                        <EstadoSolicitud estado={s.estado} />
                      </td>
                      <td className="px-2 py-1.5">{s.entidad}</td>
                      <td className="px-2 py-1.5 font-mono opacity-70">{s.noSolicitud}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}
        </section>
      </div>

      {vista.garantias.length > 0 ? (
        <section className="flex flex-col gap-2 rounded-xl border border-[#DFE5ED] p-3 dark:border-white/10">
          <h5 className="text-[10px] font-bold uppercase tracking-wider text-[#557EFF]">Garantías inscritas</h5>
          {vista.garantias.map((g) => (
            <div key={`${g.acreedor}-${g.fechaInscripcion}`} className="flex flex-wrap justify-between gap-3 border-b border-[#DFE5ED] pb-1.5 text-[11px] last:border-b-0 last:pb-0 dark:border-white/10">
              <span className="font-medium">{g.acreedor}</span>
              <span className="opacity-70">{g.documento}</span>
              <span className="font-mono tabular-nums">{g.fechaInscripcion}</span>
            </div>
          ))}
        </section>
      ) : null}
    </div>
  );
}

const ESTADO_SOLICITUD: Record<string, { bg: string; fg: string }> = {
  AUTORIZADA: { bg: "rgba(34,197,94,0.14)", fg: "#15803D" },
  APROBADA: { bg: "rgba(34,197,94,0.14)", fg: "#15803D" },
  REGISTRADA: { bg: "rgba(255,78,0,0.14)", fg: "#C2410C" },
  RECHAZADA: { bg: "rgba(153,27,27,0.14)", fg: "#991B1B" },
};

function EstadoSolicitud({ estado }: { estado: string }) {
  const tono = ESTADO_SOLICITUD[estado.toUpperCase()] ?? { bg: "rgba(148,163,184,0.16)", fg: "#475569" };
  return (
    <span className="rounded-full px-2 py-0.5 text-[10px] font-semibold" style={{ background: tono.bg, color: tono.fg }}>
      {estado}
    </span>
  );
}
