"use client";

import { useEffect, useState } from "react";
import { InlineAlert } from "@/components/atom/InlineAlert";
import { useToast } from "@/components/admin/Toast";
import {
  ConsultNowConflictError,
  consultRuntNow,
  getRuntConfirmationAttempt,
  getRuntConfirmationAttemptRaw,
  RUNT_VERDICT_LABEL,
  type RuntConfirmationAttemptDetail,
  type RuntConfirmationAttemptRow,
} from "@/lib/api/admin-runt-confirmation";
import { formatFechaHora } from "@/lib/format/date";
import { proveedorLabel, QUERY_KIND_LABEL } from "./historial-columns";

const FLAG_LABEL: Record<string, string> = {
  discrepancia: "Discrepancia",
  no_verificable: "No verificable",
  tope: "Tope de intentos alcanzado",
};

/**
 * Detalle expandido de un intento (HU #12311 AC3/AC4): motivo legible, versión de la regla,
 * proveedor, quién lo pidió, crudo del proveedor en un panel con scroll propio (sin descargar) y
 * «Consultar ahora» con confirmación inline —nada de `window.confirm`— cuando el trámite sigue
 * aprobado y sin confirmar.
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
  const [detalle, setDetalle] = useState<RuntConfirmationAttemptDetail | null>(null);
  const [detalleError, setDetalleError] = useState(false);
  const [raw, setRaw] = useState<string | null>(null);
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

  const verCrudo = async () => {
    if (raw !== null) {
      setRaw(null);
      return;
    }
    setRawEstado("loading");
    try {
      const json = await getRuntConfirmationAttemptRaw(row.id);
      setRaw(JSON.stringify(json, null, 2));
      setRawEstado("idle");
    } catch {
      setRawEstado("error");
    }
  };

  const consultarAhora = async () => {
    setConsultando(true);
    setConflicto(null);
    try {
      const { attempt } = await consultRuntNow(row.procedureInstanceId);
      toast.show(
        attempt ? `Consulta realizada: ${RUNT_VERDICT_LABEL[attempt.verdict] ?? attempt.verdict}` : "Consulta realizada",
        "success",
      );
      setConfirmando(false);
      onNuevoIntento();
    } catch (err) {
      if (err instanceof ConsultNowConflictError) {
        setConflicto(err.message);
      } else {
        toast.show(err instanceof Error ? err.message : "No se pudo consultar ahora.", "error");
      }
      setConfirmando(false);
    } finally {
      setConsultando(false);
    }
  };

  const puedeConsultar = detalle !== null && detalle.procedureStatus === "aprobado" && detalle.runtConfirmedAt === null;

  return (
    <div id={`intento-detalle-${row.id}`} className="flex flex-col gap-3 rounded-2xl border border-[#DFE5ED] bg-[#F6F8FB] p-4 text-xs dark:border-white/10 dark:bg-white/5" data-testid="intento-detalle">
      <p className="text-sm leading-snug text-[#162744] dark:text-white">
        <span className="font-semibold">Motivo: </span>
        {row.reasonText}
      </p>

      <dl className="grid grid-cols-2 gap-x-4 gap-y-2 sm:grid-cols-3 lg:grid-cols-5">
        <Dato label="Resultado" value={RUNT_VERDICT_LABEL[row.verdict] ?? row.verdict} />
        <Dato label="Versión de la regla" value={row.ruleVersion} mono />
        <Dato label="Proveedor" value={proveedorLabel(row.providerKey)} />
        <Dato label="Consulta" value={QUERY_KIND_LABEL[row.queryKind] ?? row.queryKind} />
        <Dato label="Origen" value={row.runId === null ? "Re-evaluación" : row.requestedBy ? "Manual (Consultar ahora)" : "Corrida programada"} />
        {row.requestedBy ? <Dato label="Pedido por" value={row.requestedBy} mono /> : null}
        {row.flagApplied ? <Dato label="Marca dejada" value={FLAG_LABEL[row.flagApplied] ?? row.flagApplied} /> : null}
        {detalle ? (
          <>
            <Dato label="Estado del trámite" value={detalle.procedureStatus ?? "—"} />
            <Dato label="Confirmado en RUNT" value={detalle.runtConfirmedAt ? formatFechaHora(detalle.runtConfirmedAt) : "No"} />
            <Dato label="Intentos con veredicto" value={String(detalle.runtAttempts)} />
          </>
        ) : null}
      </dl>

      {detalleError ? (
        <InlineAlert tone="warning" compact>
          No se pudo cargar el estado actual del trámite; el motivo y el crudo siguen disponibles.
        </InlineAlert>
      ) : null}

      <div className="flex flex-wrap items-center gap-2">
        <button
          type="button"
          onClick={() => void verCrudo()}
          disabled={!row.hasRaw || rawEstado === "loading"}
          aria-expanded={raw !== null}
          className="rounded-full border border-[#DFE5ED] px-3 py-1.5 text-[11px] font-semibold text-[#162744] focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[#557EFF] disabled:opacity-50 dark:border-white/10 dark:text-white"
        >
          {rawEstado === "loading" ? "Cargando crudo…" : raw !== null ? "Ocultar JSON crudo" : "Ver JSON crudo del proveedor"}
        </button>
        <button
          type="button"
          onClick={onVerTramite}
          className="rounded-full border border-[#DFE5ED] px-3 py-1.5 text-[11px] font-semibold text-[#162744] focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[#557EFF] dark:border-white/10 dark:text-white"
        >
          Ver todos los intentos del trámite
        </button>

        {puedeConsultar && !confirmando ? (
          <button
            type="button"
            onClick={() => setConfirmando(true)}
            data-testid="consultar-ahora"
            className="ml-auto rounded-full bg-gradient-to-r from-[#22D3C5] to-[#557EFF] px-3 py-1.5 text-[11px] font-semibold text-white focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[#557EFF] focus-visible:ring-offset-2"
          >
            Consultar ahora
          </button>
        ) : null}
        {confirmando ? (
          <span className="ml-auto flex items-center gap-2" role="group" aria-label="Confirmar consulta manual">
            <span className="text-[11px] text-[#162744] dark:text-white">Consulta inmediata al proveedor configurado. ¿Continuar?</span>
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
              className="rounded-full border border-[#DFE5ED] px-3 py-1.5 text-[11px] font-semibold text-[#162744] disabled:opacity-50 dark:border-white/10 dark:text-white"
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

      {rawEstado === "error" ? (
        <InlineAlert tone="error" compact>
          No se pudo cargar la respuesta cruda del proveedor.
        </InlineAlert>
      ) : null}

      {raw !== null ? (
        <pre
          className="max-h-80 overflow-auto rounded-xl border border-[#DFE5ED] bg-white p-3 font-mono text-[11px] leading-snug text-[#162744] dark:border-white/10 dark:bg-[#0B0F14] dark:text-white"
          tabIndex={0}
          aria-label="JSON crudo del proveedor"
          data-testid="intento-raw"
        >
          {raw}
        </pre>
      ) : null}
    </div>
  );
}

function Dato({ label, value, mono = false }: { label: string; value: string; mono?: boolean }) {
  return (
    <div className="flex flex-col">
      <dt className="text-[10px] font-semibold uppercase tracking-wide text-[#59677D] dark:text-white/55">{label}</dt>
      <dd className={`break-all text-xs text-[#162744] dark:text-white ${mono ? "font-mono" : ""}`}>{value}</dd>
    </div>
  );
}
