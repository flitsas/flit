"use client";

import { useCallback, useEffect, useRef, useState } from "react";
import Link from "next/link";
import { ArrowLeft, FileText } from "lucide-react";
import { StatusBadge } from "@/components/atom/StatusBadge";
import { UiStateBoundary } from "@/components/admin/UiStateBoundary";
import { HitosTimeline } from "./HitosTimeline";
import { LogCompleto } from "./LogCompleto";
import { fetchLogQxHitos, type LogQxHitosResult, type LogQxStatus } from "@/lib/api/admin-log-qx";
import { codigoQx, estadoTramiteQx, formatEspera, formatFecha } from "@/lib/logqx/labels";
import type { StatusTone } from "@/components/atom/StatusBadge";

/**
 * Pantalla de trazabilidad de una radicación (HU #11789 + #11790, ruta `/log-qx/{submissionId}`).
 *
 * Vive APARTE del detalle del trámite a propósito (ADR-0051, D3): es una herramienta de diagnóstico
 * de FLIT gateada por `logqx.read`, y meterla como pestaña de `/tramites/{id}` obligaría a gatear
 * una pestaña dentro de una pantalla que ven otros roles.
 *
 * Dos pestañas porque son dos preguntas de tamaños distintos: «por qué está atascado» se responde
 * en cinco líneas (Hitos) y «qué le mandamos exactamente el 18/08» necesita el log entero.
 */

const ESTADO_RADICACION: Record<LogQxStatus, { label: string; tone: StatusTone }> = {
  pendiente: { label: "Pendiente", tone: "neutral" },
  registrado: { label: "En trámite", tone: "warning" },
  aprobado: { label: "Aprobado", tone: "success" },
  rechazado: { label: "Rechazado", tone: "danger" },
  fallido: { label: "Fallido", tone: "danger" },
};

type Pestana = "hitos" | "log";

export function TrazabilidadScreen({
  submissionId,
  volverHref,
}: {
  submissionId: string;
  /** Vuelta a la bandeja con sus filtros; los trae la URL de la que se llegó. */
  volverHref: string;
}) {
  const [actual, setActual] = useState(submissionId);
  const [data, setData] = useState<LogQxHitosResult | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [fetching, setFetching] = useState(false);
  const [pestana, setPestana] = useState<Pestana>("hitos");

  const reqIdRef = useRef(0);

  const load = useCallback(async (id: string) => {
    const reqId = ++reqIdRef.current;
    setFetching(true);
    try {
      const res = await fetchLogQxHitos(id);
      if (reqId !== reqIdRef.current) return;
      setData(res);
      setError(null);
    } catch (err) {
      if (reqId !== reqIdRef.current) return;
      setData(null);
      setError(err instanceof Error ? err.message : "No se pudo cargar la trazabilidad.");
    } finally {
      if (reqId === reqIdRef.current) setFetching(false);
    }
  }, []);

  useEffect(() => {
    // eslint-disable-next-line react-hooks/set-state-in-effect
    void load(actual);
  }, [actual, load]);

  const status: "loading" | "error" | "empty" | "ready" =
    fetching && data === null ? "loading" : error !== null ? "error" : data === null ? "empty" : "ready";

  const r = data?.radicacion;

  // La espera llega ya calculada del servidor: leer el reloj en render sería impuro y ataría la
  // cifra a la zona horaria del navegador (misma decisión que en la bandeja).
  const espera = formatEspera(r?.horasEsperando);

  return (
    <div className="app-bg min-h-screen px-6 pt-6 pb-10 text-[#162744] dark:text-white">
      <Link
        href={volverHref}
        className="mb-3 inline-flex items-center gap-1.5 text-[12.5px] font-medium opacity-70 hover:text-[#4F74C9] hover:opacity-100"
      >
        <ArrowLeft className="h-3.5 w-3.5" aria-hidden="true" />
        Volver a LOG QX <span className="opacity-60">(filtros conservados)</span>
      </Link>

      <UiStateBoundary
        status={status}
        skeletonRows={6}
        errorMessage={error ?? "No se pudo cargar la trazabilidad."}
        onRetry={() => void load(actual)}
        emptyMessage="No se encontró esta radicación."
      >
        {data && r && (
          <>
            {/* Cabecera fija: la identificación no se pierde al desplazar la línea de tiempo. */}
            <header className="sticky top-0 z-30 mb-4 rounded-2xl border border-[#DDE5F0] bg-white shadow-sm dark:border-white/10 dark:bg-[#0B0F14]">
              <div className="flex flex-wrap items-start gap-5 px-5 py-4">
                <div className="min-w-[300px] flex-1">
                  <h1 className="font-mono text-[19px] font-bold tracking-tight">
                    {r.referenceNumber}
                  </h1>
                  <div className="mt-2 flex flex-wrap items-center gap-2 text-[12.5px] opacity-75">
                    {r.plate && (
                      <span className="rounded border border-[#D9DEE8] bg-[#F4F6FA] px-1.5 py-0.5 font-mono text-xs font-bold dark:border-white/15 dark:bg-white/5">
                        {r.plate}
                      </span>
                    )}
                    <span>{r.procedureTypeName}</span>
                    <span className="opacity-50">·</span>
                    <span>{r.clientTenantName}</span>
                    <span className="opacity-50">·</span>
                    <span>Secretaría de {r.transitOfficeName}</span>
                    <StatusBadge
                      label={ESTADO_RADICACION[r.status]?.label ?? r.status}
                      tone={ESTADO_RADICACION[r.status]?.tone ?? "neutral"}
                    />
                  </div>
                </div>

                <dl className="flex flex-wrap gap-x-6 gap-y-2">
                  <Dato label="Documento QX" valor={r.documentoQx} mono />
                  <Dato label="Código registro" valor={codigoQx(r.qxRegisterCode)} />
                  <Dato label="Estado en QX" valor={estadoTramiteQx(r.qxProcedureCode)} />
                  <Dato label="Consultas" valor={r.pollCount.toLocaleString("es-CO")} mono />
                  {espera && <Dato label="Esperando" valor={espera} mono destacado />}
                </dl>

                <Link
                  href={`/tramites/${r.procedureInstanceId}`}
                  className="inline-flex items-center gap-1.5 self-center rounded-lg border border-[#DDE5F0] px-3.5 py-2 text-[12.5px] font-semibold text-[#4F74C9] hover:bg-[#4F74C9]/[0.08] dark:border-white/15"
                >
                  <FileText className="h-3.5 w-3.5" aria-hidden="true" /> Ver trámite
                </Link>
              </div>

              {/* Tira de intentos: solo cuando el trámite acumuló más de una radicación. */}
              {r.totalIntentos > 1 && (
                <div className="flex flex-wrap items-center gap-2 border-t border-[#DDE5F0] bg-[#F05A35]/[0.08] px-5 py-2.5 text-[12.5px] dark:border-white/10">
                  <span className="font-semibold text-[#D9521F]">
                    Este trámite tuvo {r.totalIntentos} radicaciones.
                  </span>
                  <span className="opacity-70">Viendo:</span>
                  {r.hermanas.map((h) => (
                    <button
                      key={h.id}
                      type="button"
                      aria-current={h.id === r.id}
                      onClick={() => {
                        setActual(h.id);
                        setPestana("hitos");
                      }}
                      className={`rounded-full border px-2.5 py-1 font-mono text-[11.5px] ${
                        h.id === r.id
                          ? "border-[#4F74C9] bg-[#4F74C9]/[0.1] font-bold text-[#4F74C9]"
                          : "border-[#D9DEE8] opacity-70 hover:opacity-100 dark:border-white/15"
                      }`}
                    >
                      Intento {h.intento}
                    </button>
                  ))}
                </div>
              )}

              <div className="flex gap-0.5 border-t border-[#DDE5F0] px-5 dark:border-white/10" role="tablist">
                <Tab actual={pestana} valor="hitos" onSelect={setPestana}>
                  Hitos
                </Tab>
                <Tab actual={pestana} valor="log" onSelect={setPestana}>
                  Log completo
                </Tab>
              </div>
            </header>

            <div className="rounded-2xl border border-[#DDE5F0] bg-white dark:border-white/10 dark:bg-[#0B0F14]">
              {pestana === "hitos" ? (
                <>
                  <div className="border-b border-[#DDE5F0] px-6 py-4 dark:border-white/10">
                    <p className="mb-1.5 text-[10px] font-bold uppercase tracking-[0.12em] text-[#4F74C9]">
                      Qué pasó
                    </p>
                    <p className="max-w-[76ch] text-[15px] leading-relaxed">{resumen(r, espera)}</p>
                  </div>
                  <HitosTimeline hitos={data.hitos} radicacion={r} />
                </>
              ) : (
                <LogCompleto submissionId={r.id} />
              )}
            </div>
          </>
        )}
      </UiStateBoundary>
    </div>
  );
}

function Tab({
  actual,
  valor,
  onSelect,
  children,
}: {
  actual: Pestana;
  valor: Pestana;
  onSelect: (p: Pestana) => void;
  children: React.ReactNode;
}) {
  const activa = actual === valor;
  return (
    <button
      type="button"
      role="tab"
      aria-selected={activa}
      onClick={() => onSelect(valor)}
      className={`-mb-px border-b-2 px-4 py-2.5 text-[13px] transition ${
        activa
          ? "border-[#4F74C9] font-semibold text-[#4F74C9]"
          : "border-transparent opacity-70 hover:opacity-100"
      }`}
    >
      {children}
    </button>
  );
}

function Dato({
  label,
  valor,
  mono,
  destacado,
}: {
  label: string;
  valor: string;
  mono?: boolean;
  destacado?: boolean;
}) {
  return (
    <div className="flex flex-col gap-0.5">
      <dt className="text-[10px] font-bold uppercase tracking-wider opacity-55">{label}</dt>
      <dd
        className={`m-0 text-[13px] ${mono ? "font-mono tabular-nums" : ""} ${
          destacado ? "font-bold text-[#D9521F]" : "font-medium"
        }`}
        title={valor}
      >
        {valor.length > 34 ? `…${valor.slice(-32)}` : valor}
      </dd>
    </div>
  );
}

/** El resumen que un agente de soporte le repite al cliente por teléfono. */
function resumen(r: LogQxHitosResult["radicacion"], espera: string | null): string {
  const radicado = `Radicado en Quipux el ${formatFecha(r.registeredAt ?? r.createdAt)} como ${r.documentoQx}`;

  switch (r.status) {
    case "pendiente":
      return `Está en cola para radicarse en Quipux${
        espera ? `, esperando desde hace ${espera}` : ""
      }. Todavía no se ha enviado a la Secretaría de ${r.transitOfficeName}.`;

    case "registrado":
      return `${radicado}. La Secretaría de ${r.transitOfficeName} todavía no lo resuelve${
        espera ? `: llevamos ${espera} esperando` : ""
      }, con ${r.pollCount.toLocaleString("es-CO")} consultas de estado realizadas${
        r.lastPolledAt ? `, la última el ${formatFecha(r.lastPolledAt)}` : ""
      }.`;

    case "aprobado":
      return `${radicado}, y la Secretaría de ${r.transitOfficeName} lo aprobó el ${formatFecha(
        r.completedAt,
      )}.`;

    case "rechazado":
      return `${radicado}. La Secretaría de ${r.transitOfficeName} lo rechazó el ${formatFecha(
        r.completedAt,
      )}${r.rejectionReason ? `. Motivo: ${r.rejectionReason}` : " sin dejar un motivo registrado"}.`;

    case "fallido":
      return `La radicación falló tras ${r.attempts} ${
        r.attempts === 1 ? "intento" : "intentos"
      }${r.rejectionReason ? `. ${r.rejectionReason}` : ""}. Este documento nunca llegó a la Secretaría de ${
        r.transitOfficeName
      }.`;

    default:
      return `Estado ${r.status} en la Secretaría de ${r.transitOfficeName}.`;
  }
}
