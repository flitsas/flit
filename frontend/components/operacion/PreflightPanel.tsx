'use client';

// Panel de pre-vuelo (semáforo de requisitos). Presentacional: el fetch/estado
// viven en el wizard/hook. La consulta real (RUNT/SIMIT) se cablea en #10201;
// por ahora el hook alimenta un snapshot stub.

import { useWizardReadOnly } from './WizardReadOnlyContext';
import type {
  PreflightCheckStatus,
  PreflightSnapshot,
} from '@/lib/api/types/procedure-runtime';

interface Props {
  snapshot: PreflightSnapshot | null;
  loading: boolean;
  onRun: () => void;
  riesgoAceptado: boolean;
  onToggleRiesgo: (v: boolean) => void;
  // El disparo de la consulta puede vivir fuera del panel (p. ej. junto al campo
  // VIN en matrícula). En ese caso el panel es solo presentacional (semáforo).
  showRunButton?: boolean;
}

const STATUS_STYLE: Record<PreflightCheckStatus, { dot: string; text: string }> = {
  ok: { dot: '#8CC63F', text: '#8CC63F' },
  warn: { dot: '#F9AC00', text: '#F9AC00' },
  fail: { dot: '#FF4E00', text: '#FF4E00' },
  unknown: { dot: '#9AA5B1', text: '#9AA5B1' },
};

const OVERALL: Record<string, { label: string; bg: string; color: string }> = {
  green: { label: 'Pre-vuelo en verde', bg: 'rgba(140,198,63,0.15)', color: '#8CC63F' },
  yellow: { label: 'Pre-vuelo con advertencias', bg: 'rgba(249,172,0,0.15)', color: '#F9AC00' },
  red: { label: 'Pre-vuelo con bloqueos', bg: 'rgba(255,78,0,0.15)', color: '#FF4E00' },
};

/**
 * Mapea el código de proveedor (interno) a la fuente colombiana que ve el
 * usuario. Nunca debe filtrarse el nombre del proveedor a la UI: el origen del
 * dato es el RUNT/SIMIT/RNMC, no la pasarela de consulta.
 */
const SOURCE_LABEL: Record<string, string> = {
  verifik: 'RUNT',
  verifik_vehicle: 'RUNT',
  verifik_simit: 'SIMIT',
  verifik_rnmc: 'RNMC',
  intempo: 'RUNT',
};

export function sourceLabel(source: string | null | undefined): string {
  if (!source) return '';
  return SOURCE_LABEL[source] ?? source.toUpperCase();
}

export function PreflightPanel({
  snapshot,
  loading,
  onRun,
  riesgoAceptado,
  onToggleRiesgo,
  showRunButton = true,
}: Props) {
  // En solo lectura nunca se ofrece el disparo de la consulta (Track C).
  const readOnly = useWizardReadOnly();
  const canRun = showRunButton && !readOnly;
  const hasResult = !!snapshot?.overall;
  const overall = snapshot?.overall;
  const checks = snapshot?.checks ?? [];
  const ov = overall ? OVERALL[overall] : null;

  return (
    <div
      className="rounded-2xl p-4 border bg-white dark:bg-[#0B0F14] mt-4"
      style={{ borderColor: '#DFE5ED' }}
    >
      <div className="mb-3 flex items-center justify-between gap-3">
        <div>
          <h4 className="text-sm font-bold">Pre-vuelo de requisitos</h4>
          <p className="text-[11px] opacity-60">
            RUNT · SIMIT — consulta antes de radicar el trámite
          </p>
        </div>
        <div className="flex items-center gap-2">
          {ov && (
            <span
              className="shrink-0 rounded-full px-3 py-1 text-[11px] font-bold"
              style={{ background: ov.bg, color: ov.color }}
              role="status"
              aria-live="polite"
            >
              {ov.label}
            </span>
          )}
          {canRun && (
            <button
              type="button"
              onClick={onRun}
              disabled={loading}
              className="rounded-xl px-5 py-2.5 text-xs font-semibold text-white disabled:cursor-not-allowed disabled:opacity-50"
              style={{ background: 'linear-gradient(135deg,#557EFF,#00DBD5)' }}
              aria-label="Consultar RUNT y SIMIT"
            >
              {loading ? 'Consultando…' : hasResult ? 'Actualizar' : 'Consultar RUNT'}
            </button>
          )}
        </div>
      </div>

      {!hasResult && !loading && (
        <p className="text-[11px] opacity-60">
          Ejecuta la consulta para ver el semáforo de requisitos del vehículo.
        </p>
      )}

      {hasResult && (
        <ul className="space-y-1.5" aria-label="Resultados del pre-vuelo">
          {checks.map((c) => {
            const s = STATUS_STYLE[c.status];
            return (
              <li
                key={c.key}
                className="flex items-start gap-2.5 rounded-xl border p-2.5"
                style={{ borderColor: '#DFE5ED' }}
              >
                <span
                  className="mt-1 h-2.5 w-2.5 shrink-0 rounded-full"
                  style={{ background: s.dot }}
                  aria-hidden="true"
                />
                <div className="min-w-0 flex-1">
                  <div className="flex flex-wrap items-center gap-1.5">
                    <span className="text-xs font-semibold">{c.label}</span>
                    <span
                      className="text-[10px] uppercase font-bold"
                      style={{ color: s.text }}
                    >
                      {c.status}
                    </span>
                    <span
                      className="rounded px-1.5 py-0.5 text-[9px] font-semibold uppercase"
                      style={{ background: 'rgba(85,126,255,0.10)', color: '#557EFF' }}
                    >
                      {sourceLabel(c.source)}
                    </span>
                  </div>
                  <p className="mt-0.5 text-[11px] opacity-70">{c.message}</p>
                  {c.action && (
                    <span
                      className="mt-1 inline-block text-[11px] font-semibold"
                      style={{ color: '#557EFF' }}
                    >
                      {c.action.label} →
                    </span>
                  )}
                </div>
              </li>
            );
          })}
        </ul>
      )}

      {overall === 'red' && (
        <label
          className="mt-3 flex items-start gap-2.5 rounded-xl p-3"
          style={{ background: 'rgba(255,78,0,0.08)', border: '1px solid rgba(255,78,0,0.30)' }}
        >
          <input
            type="checkbox"
            checked={riesgoAceptado}
            onChange={(e) => onToggleRiesgo(e.target.checked)}
            disabled={readOnly}
            className="mt-0.5 h-4 w-4 shrink-0 accent-[#FF4E00] disabled:opacity-60"
          />
          <span className="text-xs font-medium" style={{ color: '#FF4E00' }}>
            Asumo el riesgo de rechazo en el organismo de tránsito y deseo
            continuar con el trámite.
          </span>
        </label>
      )}
    </div>
  );
}
