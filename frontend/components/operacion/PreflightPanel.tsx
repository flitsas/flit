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
  /** Persistiendo la aceptación de riesgo: deshabilita el checkbox para evitar dobles clics. */
  saving?: boolean;
  // El disparo de la consulta puede vivir fuera del panel (p. ej. junto al campo
  // VIN en matrícula). En ese caso el panel es solo presentacional (semáforo).
  showRunButton?: boolean;
  // R3 (HU #10539) — en matrícula, cuando el preflight detecta que el VIN ya tiene
  // matrícula previa (check `vin_matricula`), el panel ofrece iniciar el traspaso del
  // vehículo. El wizard inyecta la navegación (sembrando placa/VIN); el panel es
  // presentacional. Ausente ⇒ no se ofrece el CTA (p. ej. en traspaso).
  onIniciarTraspaso?: () => void;
}

const STATUS_STYLE: Record<PreflightCheckStatus, { dot: string; text: string }> = {
  ok: { dot: '#8CC63F', text: '#8CC63F' },
  warn: { dot: '#F9AC00', text: '#F9AC00' },
  fail: { dot: '#FF4E00', text: '#FF4E00' },
  unknown: { dot: '#9AA5B1', text: '#9AA5B1' },
  error: { dot: '#FF4E00', text: '#FF4E00' },
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
  kyverum_runt: 'RUNT',
  // El origen último del dato es el SIMIT; Kyverum es solo la pasarela.
  kyverum_fines: 'SIMIT',
  // Cartera propia de FLIT: aquí la fuente sí es interna.
  flit_fines: 'Comparendos FLIT',
  // Checks derivados por la plataforma, no por un proveedor externo
  // (p. ej. `vin_matricula`, o una consulta omitida por configuración del OT).
  system: 'FLIT',
};

export function sourceLabel(source: string | null | undefined): string {
  if (!source) return '';
  return SOURCE_LABEL[source] ?? source.toUpperCase();
}

/**
 * RNMC (HU #10602/#10603) corre por actor persona natural: las claves quedan
 * `rnmc_comprador_*` / `rnmc_vendedor_*`. Devuelve el sufijo de rol para
 * distinguir ambos checks en el panel (el label del proveedor es idéntico).
 */
export function checkRoleSuffix(key: string): string {
  if (key.startsWith('rnmc_comprador')) return ' (comprador)';
  if (key.startsWith('rnmc_vendedor')) return ' (vendedor)';
  return '';
}

export function PreflightPanel({
  snapshot,
  loading,
  onRun,
  riesgoAceptado,
  onToggleRiesgo,
  saving = false,
  showRunButton = true,
  onIniciarTraspaso,
}: Props) {
  // En solo lectura nunca se ofrece el disparo de la consulta (Track C).
  const readOnly = useWizardReadOnly();
  const canRun = showRunButton && !readOnly;
  const hasResult = !!snapshot?.overall;
  const overall = snapshot?.overall;
  const checks = snapshot?.checks ?? [];
  const ov = overall ? OVERALL[overall] : null;
  // Un check "error" = consulta no verificable (proveedor caído/timeout): bloqueo DURO. NO se
  // ofrece "aceptar riesgo" (no es subsanable); el gestor debe reintentar la consulta.
  const hasProviderError = checks.some((c) => c.status === 'error');
  // R3 (HU #10539) — señal server-driven de "VIN ya matriculado": el backend agrega el check
  // `vin_matricula` (warn, con secretaría + fecha del registro previo). Cuando está presente y el
  // wizard proveyó la navegación, se ofrece iniciar el traspaso del vehículo en vez de una matrícula.
  const vinConflicto = checks.find((c) => c.key === 'vin_matricula');
  // Se saca de la lista genérica de checks: su mensaje se muestra —de forma accionable— en la
  // tarjeta CTA de abajo, para no duplicarlo. El resto del semáforo se pinta normal.
  const visibleChecks = checks.filter((c) => c.key !== 'vin_matricula');
  // Hallazgos no bloqueantes (multas, SOAT/RTM, consultas omitidas por el OT): se resumen para que
  // el gestor los vea de un vistazo. Se toman de `visibleChecks` para no repetir `vin_matricula`,
  // que ya tiene su propia tarjeta accionable arriba.
  const warnChecks = visibleChecks.filter((c) => c.status === 'warn');

  return (
    <div
      className="rounded-2xl p-4 border bg-white dark:bg-[#0B0F14] mt-4"
    >
      <div className="mb-3 flex items-center justify-between gap-3">
        <div>
          <h4 className="text-sm font-bold">Pre-vuelo de requisitos</h4>
          <p className="text-[11px] opacity-60">
            RUNT · SIMIT · RNMC — consulta antes de radicar el trámite
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
          {visibleChecks.map((c) => {
            const s = STATUS_STYLE[c.status];
            return (
              <li
                key={c.key}
                className="flex items-start gap-2.5 rounded-xl border p-2.5"
              >
                <span
                  className="mt-1 h-2.5 w-2.5 shrink-0 rounded-full"
                  style={{ background: s.dot }}
                  aria-hidden="true"
                />
                <div className="min-w-0 flex-1">
                  <div className="flex flex-wrap items-center gap-1.5">
                    <span className="text-xs font-semibold">
                      {c.label}
                      {checkRoleSuffix(c.key)}
                    </span>
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

      {vinConflicto && (
        <div
          className="mt-3 rounded-xl p-3"
          style={{ background: 'rgba(85,126,255,0.06)', border: '1px solid rgba(85,126,255,0.30)' }}
          role="status"
          aria-live="polite"
        >
          <p className="text-xs font-bold" style={{ color: '#557EFF' }}>
            Este VIN ya está matriculado
          </p>
          <p className="mt-0.5 text-[11px] opacity-70">{vinConflicto.message}</p>
          {onIniciarTraspaso && !readOnly && (
            <button
              type="button"
              onClick={onIniciarTraspaso}
              className="mt-2 rounded-xl px-4 py-2 text-xs font-semibold text-white"
              style={{ background: 'linear-gradient(135deg,#557EFF,#00DBD5)' }}
            >
              Iniciar traspaso de este vehículo →
            </button>
          )}
        </div>
      )}

      {hasProviderError && (
        <div
          className="mt-3 flex items-start gap-2.5 rounded-xl p-3"
          style={{ background: 'rgba(255,78,0,0.08)', border: '1px solid rgba(255,78,0,0.30)' }}
          role="alert"
        >
          <span className="text-xs font-medium" style={{ color: '#FF4E00' }}>
            No fue posible verificar la información en este momento. Vuelve a
            ejecutar la consulta antes de continuar; no es posible avanzar sin
            estos datos.
          </span>
        </div>
      )}

      {/* Amarillo = hay observaciones, no bloqueos: se informan y se sigue. Por eso `status` y no
          `alert`, y por eso NO se ofrece aceptar riesgo (no hay nada que levantar). */}
      {overall === 'yellow' && warnChecks.length > 0 && (
        <div
          className="mt-3 rounded-xl p-3"
          style={{ background: 'rgba(249,172,0,0.08)', border: '1px solid rgba(249,172,0,0.30)' }}
          role="status"
          aria-live="polite"
        >
          <p className="text-xs font-bold" style={{ color: '#F9AC00' }}>
            Advertencias del pre-vuelo
          </p>
          <ul className="mt-1.5 space-y-1">
            {warnChecks.map((c) => (
              <li key={c.key} className="text-[11px]">
                <span className="font-semibold">
                  {c.label}
                  {checkRoleSuffix(c.key)}
                </span>
                {c.message && <span className="opacity-70"> — {c.message}</span>}
              </li>
            ))}
          </ul>
          <p className="mt-2 text-[11px] opacity-70">
            Puedes continuar con el trámite; el organismo de tránsito verá estas
            observaciones.
          </p>
        </div>
      )}

      {overall === 'red' && !hasProviderError && (
        <label
          className="mt-3 flex items-start gap-2.5 rounded-xl p-3"
          style={{ background: 'rgba(255,78,0,0.08)', border: '1px solid rgba(255,78,0,0.30)' }}
        >
          <input
            type="checkbox"
            checked={riesgoAceptado}
            onChange={(e) => onToggleRiesgo(e.target.checked)}
            disabled={readOnly || saving}
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
