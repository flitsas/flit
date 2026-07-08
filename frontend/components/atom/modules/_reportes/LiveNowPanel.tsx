"use client";

// Panel "Ahora mismo" (Reportes 2.0, HU-C · pestaña Resumen): estado del tenant HOY
// (hora Bogotá) vía GET /live-overview con auto-refresh configurable, botón de pausa
// e indicador "actualizado hace Xs". Los chips por estado hacen drill-down.
import { Pause, Play, Radio } from "lucide-react";
import { UiStateBoundary } from "@/components/admin/UiStateBoundary";
import { statusColor, statusLabel } from "./categories";
import { CompanyNotice } from "./CompanyNotice";
import { formatDurationMs, formatInt } from "./format";
import {
  LIVE_REFRESH_DEFAULT_S,
  LIVE_REFRESH_MAX_S,
  LIVE_REFRESH_MIN_S,
  useLiveOverview,
} from "./useLiveOverview";

export interface LiveNowPanelProps {
  tenantId?: string;
  stuckDays?: number;
  /** SuperAdmin sin compañía: no llama a la API y muestra el aviso. */
  skip?: boolean;
  /** Drill-down por estado actual (abre el detalle de trámites). */
  onDrillDown?: (status: string) => void;
}

const INTERVAL_OPTIONS = [LIVE_REFRESH_MIN_S, LIVE_REFRESH_DEFAULT_S, LIVE_REFRESH_MAX_S];

export function LiveNowPanel({ tenantId, stuckDays, skip, onDrillDown }: LiveNowPanelProps) {
  const live = useLiveOverview({ tenantId, stuckDays }, { skip });

  return (
    <section className="rounded-2xl p-5 bg-white dark:bg-[#0B0F14] border flex flex-col gap-3" aria-labelledby="live-now-title">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <h2 id="live-now-title" className="text-sm font-bold flex items-center gap-2">
          <Radio className="h-4 w-4 animate-pulse" style={{ color: "#00DBD5" }} aria-hidden="true" />
          Ahora mismo
        </h2>
        {!skip && (
          <div className="flex items-center gap-2">
            {live.secondsAgo !== null && (
              <span className="text-[10px] opacity-60" data-testid="live-updated-ago" aria-live="polite">
                actualizado hace {live.secondsAgo}s
              </span>
            )}
            <label htmlFor="live-interval" className="sr-only">
              Intervalo de actualización en segundos
            </label>
            <select
              id="live-interval"
              className="h-7 rounded-lg border bg-white px-1.5 text-[10px] font-medium dark:bg-[#0B0F14]"
              value={live.intervalSec}
              onChange={(e) => live.setIntervalSec(Number(e.target.value))}
              title="Cada cuántos segundos se actualiza el panel (30–60 s)"
            >
              {INTERVAL_OPTIONS.map((s) => (
                <option key={s} value={s}>
                  {s} s
                </option>
              ))}
            </select>
            <button
              type="button"
              onClick={() => live.setPaused(!live.paused)}
              aria-label={live.paused ? "Reanudar actualización" : "Pausar actualización"}
              aria-pressed={live.paused}
              className="h-7 w-7 grid place-items-center rounded-lg border hover:bg-[#557EFF1A]"
            >
              {live.paused ? <Play className="h-3.5 w-3.5" /> : <Pause className="h-3.5 w-3.5" />}
            </button>
          </div>
        )}
      </div>

      {skip ? (
        <CompanyNotice message="Elige una compañía para ver su actividad en tiempo real." />
      ) : (
        <UiStateBoundary
          status={live.status}
          errorMessage={live.errorMessage}
          onRetry={live.retry}
          emptyMessage="Sin actividad registrada hoy."
          skeletonRows={2}
        >
          {live.data && (
            <div className="flex flex-col gap-3">
              <div className="grid grid-cols-2 sm:grid-cols-3 xl:grid-cols-6 gap-2">
                <LiveStat label="Creados hoy" value={live.data.today.creados} color="#557EFF" tooltip="Instancias de trámite creadas hoy (hora de Bogotá)." />
                <LiveStat label="Entregados hoy" value={live.data.today.entregados} color="#557EFF" tooltip="Transiciones a 'entregado' registradas hoy." />
                <LiveStat label="Aprobados hoy" value={live.data.today.aprobados} color="#8CC63F" tooltip="Transiciones a 'aprobado' registradas hoy." />
                <LiveStat label="Rechazados hoy" value={live.data.today.rechazados} color="#FF4E00" tooltip="Transiciones a 'rechazado' registradas hoy." />
                <LiveStat label="Atascados" value={live.data.stuckCount} color="#F9AC00" tooltip="Trámites activos sin cambio de estado hace más días que el umbral configurado (7 por defecto)." />
                <LiveStat label="Identidad pendiente" value={live.data.pendingIdentityValidations} color="#9B8AFB" tooltip="Validaciones biométricas de identidad pendientes o en proceso." />
              </div>

              {live.data.today.byStatus.length > 0 && (
                <div className="flex flex-wrap items-center gap-1.5">
                  <span className="text-[10px] opacity-60 font-medium">Activos por estado:</span>
                  {live.data.today.byStatus.map((s, i) => (
                    <button
                      key={s.status}
                      type="button"
                      onClick={onDrillDown ? () => onDrillDown(s.status) : undefined}
                      disabled={!onDrillDown}
                      className="flex items-center gap-1 text-[10px] rounded-full border px-2 py-0.5 hover:bg-[#557EFF14] disabled:cursor-default"
                      aria-label={`Ver trámites en estado ${statusLabel(s.status)}`}
                    >
                      <span className="h-2 w-2 rounded-full" style={{ background: statusColor(s.status, i) }} />
                      {statusLabel(s.status)} <b>{formatInt(s.count)}</b>
                    </button>
                  ))}
                </div>
              )}

              <p className="text-[10px] opacity-60">
                Integraciones (última hora): {formatInt(live.data.integrationsLastHour.calls)} llamadas ·{" "}
                {formatInt(live.data.integrationsLastHour.errors)} errores · promedio{" "}
                {formatDurationMs(live.data.integrationsLastHour.avgDurationMs)} · Última actividad:{" "}
                {live.data.lastActivityAt ? formatTime(live.data.lastActivityAt) : "sin actividad"}
              </p>
            </div>
          )}
        </UiStateBoundary>
      )}
    </section>
  );
}

function LiveStat({ label, value, color, tooltip }: { label: string; value: number; color: string; tooltip: string }) {
  return (
    <div className="rounded-xl border p-2.5" title={tooltip}>
      <p className="text-[10px] opacity-70 font-medium">{label}</p>
      <p className="text-lg font-bold mt-0.5" style={{ color }}>
        {formatInt(value)}
      </p>
    </div>
  );
}

function formatTime(iso: string): string {
  try {
    return new Date(iso).toLocaleTimeString("es-CO", { hour: "2-digit", minute: "2-digit" });
  } catch {
    return iso;
  }
}
