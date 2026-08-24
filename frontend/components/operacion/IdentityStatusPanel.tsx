'use client';

import { esFamiliaTraspaso } from './wizardCapabilities';
import type { ProcedureFamily } from '@/lib/api/types/procedure-parametrization';
import { useCallback, useEffect, useRef, useState } from 'react';
import { AlertCircle, AlertTriangle, Clock, Info, XCircle } from 'lucide-react';
import { StatusBadge, type StatusTone } from '@/components/atom/StatusBadge';
import { INLINE_ALERT_TONES } from '@/components/atom/InlineAlert';
import { tramitesClient } from '@/lib/api/tramites-client';
import { presentarMotivoNoEnvio } from './envio-validacion-motivos';
import type {
  BiometricParte,
  BiometricValidation,
  EnvioValidacionMotivo,
  IdentityValidationAlert,
  IdentityValidationAlertKind,
  ProcedureActor,
  WizardModalidad,
} from '@/lib/api/types/procedure-runtime';

/**
 * HU #10875 + CF-08 (Feature #11004, HU #11009).
 *
 * Mismo patrón que `EstadoTimelinePanel`: disclosure colapsado por defecto
 * ("▸ Historial de identidad"), carga al abrir, lista cada proceso (aprobación /
 * rechazo) con fecha. En vivo mientras el panel está abierto y hay procesos
 * no terminales. Sin botón Actualizar.
 */

const PARTE_LABEL: Record<BiometricParte, string> = {
  comprador: 'Comprador',
  vendedor: 'Vendedor',
};

function parteLabel(role: string | null | undefined, fallback?: string | null): string {
  if (role === 'comprador' || role === 'vendedor') return PARTE_LABEL[role];
  return fallback?.trim() || 'Actor';
}

const ALERT_META: Record<
  IdentityValidationAlertKind,
  { label: string; tone: StatusTone; icon: typeof AlertCircle }
> = {
  rechazada: { label: 'Rechazada', tone: 'danger', icon: XCircle },
  expirada: { label: 'Expirada', tone: 'danger', icon: AlertCircle },
  por_vencer: { label: 'Por vencer', tone: 'warning', icon: Clock },
  atascada: { label: 'Atascada', tone: 'warning', icon: AlertTriangle },
};

const ESTADO_LABEL: Record<string, string> = {
  enviado: 'Enviado',
  en_proceso: 'En proceso',
  aprobado: 'Aprobado',
  rechazado: 'Rechazado',
  expirado: 'Expirado',
  pendiente_envio: 'Pendiente de envío',
  error_envio: 'Error de envío',
};

const ESTADO_TONE: Record<string, StatusTone> = {
  enviado: 'info',
  en_proceso: 'warning',
  aprobado: 'success',
  rechazado: 'danger',
  expirado: 'neutral',
  pendiente_envio: 'info',
  error_envio: 'danger',
};

const POLL_MS = 5_000;

function isOpenValidation(v: BiometricValidation): boolean {
  return !(v.status === 'aprobado' || v.status === 'rechazado' || v.status === 'expirado' || v.expired);
}

function isOutcomeValidation(v: BiometricValidation): boolean {
  return v.status === 'aprobado' || v.status === 'rechazado';
}

interface OutcomeRow {
  validation: BiometricValidation;
  actorLabel: string;
  actorName: string | null;
  vigente: boolean;
}

/**
 * HU21 — orden de presentación: saliente antes que entrante, igual que el resumen de firmas del
 * paso FUR (HU #11019), el expediente y el listado. Antes se respetaba el orden de llegada de los
 * actores, que deja al comprador primero.
 */
const PARTE_ORDEN: Record<string, number> = { vendedor: 0, comprador: 1 };

function ordenarPartes(actors: ProcedureActor[]): ProcedureActor[] {
  return [...actors].sort(
    (a, b) => (PARTE_ORDEN[a.rol] ?? 99) - (PARTE_ORDEN[b.rol] ?? 99),
  );
}

function buildOutcomeRows(
  familia: ProcedureFamily | WizardModalidad,
  actors: ProcedureActor[],
  validations: BiometricValidation[],
): OutcomeRow[] {
  const rows: OutcomeRow[] = [];
  // ADR-0050 — el estado del asistente trae la FAMILIA en el campo `modalidad`, así que comparar
  // contra `'traspaso'` nunca acertaba: las validaciones del vendedor se atribuían al comprador.
  const dosPartes = esFamiliaTraspaso(familia);
  for (const actor of ordenarPartes(actors)) {
    const parte = actor.rol;
    const matches = validations.filter((v) =>
      dosPartes
        ? v.partyRole === parte
        : v.partyRole === null || v.partyRole === 'comprador',
    );
    const vigenteId = matches.length > 0 ? matches[matches.length - 1]!.id : null;
    for (const v of matches.filter(isOutcomeValidation)) {
      rows.push({
        validation: v,
        actorLabel: PARTE_LABEL[parte],
        actorName: actor.nombreCompleto || v.name || null,
        vigente: v.id === vigenteId,
      });
    }
  }
  return rows;
}

export function IdentityStatusPanel({
  instanceId,
  modalidad,
}: {
  instanceId: string | null;
  modalidad?: WizardModalidad;
}) {
  const [open, setOpen] = useState(false);
  const [actors, setActors] = useState<ProcedureActor[] | null>(null);
  const [validations, setValidations] = useState<BiometricValidation[] | null>(null);
  const [alerts, setAlerts] = useState<IdentityValidationAlert[] | null>(null);
  // HU #11666 — motivos tipificados de no envío del estado biométrico (derivados al vuelo por el
  // backend). Se refrescan con cada carga, igual que las validaciones.
  const [motivos, setMotivos] = useState<EnvioValidacionMotivo[]>([]);
  const [wizardFamilia, setWizardFamilia] = useState<ProcedureFamily | WizardModalidad | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(false);
  const fetchingRef = useRef(false);
  const loadedOnceRef = useRef(false);

  const load = useCallback(async (opts?: { background?: boolean }) => {
    if (!instanceId || fetchingRef.current) return;
    fetchingRef.current = true;
    if (!opts?.background) setLoading(true);
    setError(null);
    try {
      const [wizardRes, actorsRes, biometricRes, alertsRes] = await Promise.all([
        tramitesClient.getWizardState(instanceId),
        tramitesClient.getActors(instanceId),
        tramitesClient.getBiometricState(instanceId),
        tramitesClient.getInstanceIdentityValidationAlerts(instanceId),
      ]);
      setWizardFamilia(wizardRes?.modalidad ?? null);
      setActors(actorsRes);
      setValidations(biometricRes.validations);
      setMotivos(biometricRes.motivosNoEnvio ?? []);
      setAlerts(alertsRes.alerts);
      loadedOnceRef.current = true;
    } catch (err) {
      setError(
        err instanceof Error ? err.message : 'No se pudo cargar el historial de identidad.',
      );
    } finally {
      fetchingRef.current = false;
      if (!opts?.background) setLoading(false);
    }
  }, [instanceId]);

  // Carga perezosa al abrir (mismo espíritu que EstadoTimelinePanel).
  useEffect(() => {
    if (!open || !instanceId) return;
    if (!loadedOnceRef.current) {
      void load();
    }
  }, [open, instanceId, load]);

  const needsLive = open && validations != null && validations.some(isOpenValidation);

  useEffect(() => {
    if (!instanceId || !needsLive) return;
    const tick = () => {
      if (document.hidden) return;
      void load({ background: true });
    };
    const timer = setInterval(tick, POLL_MS);
    return () => clearInterval(timer);
  }, [instanceId, needsLive, load]);

  if (!instanceId) return null;

  const hasData = actors !== null && validations !== null && alerts !== null;
  const initialLoading = open && loading && !hasData && error === null;
  const effFamilia = modalidad ?? wizardFamilia ?? 'TRASPASO';
  const outcomes = hasData ? buildOutcomeRows(effFamilia, actors, validations) : [];
  const accionables =
    hasData
      ? alerts.filter((a) => a.alertKind != null)
      : [];
  const reminders =
    hasData
      ? alerts.filter((a) => a.requiresResendReminder && !a.alertKind)
      : [];

  return (
    <section
      style={{
        margin: '16px auto 0',
        maxWidth: 960,
        padding: '0 16px',
      }}
      aria-label="Historial de identidad del trámite"
    >
      <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', gap: 12, flexWrap: 'wrap' }}>
        <button
          type="button"
          onClick={() => setOpen((v) => !v)}
          aria-expanded={open}
          style={{
            background: 'transparent',
            border: 'none',
            color: '#475569',
            fontSize: 13,
            fontWeight: 600,
            cursor: 'pointer',
            padding: 0,
          }}
        >
          {open ? '▾' : '▸'} Historial de identidad
        </button>
        {open && needsLive && (
          <span
            className="inline-flex items-center gap-1 rounded-full px-2 py-0.5 text-xs font-semibold"
            style={{ background: 'rgba(85,126,255,0.12)', color: '#557EFF' }}
            role="status"
            aria-live="polite"
          >
            <span className="h-1.5 w-1.5 animate-pulse rounded-full bg-current" aria-hidden />
            En vivo
          </span>
        )}
      </div>

      {open ? (
        <div
          style={{
            marginTop: 12,
            background: '#fff',
            border: '1px solid #e2e8f0',
            borderRadius: 12,
            padding: 16,
          }}
          className="dark:bg-[#162744]"
        >
          {error && (
            <div
              className="flex items-start gap-2 rounded-xl border p-3 text-xs"
              style={{ borderColor: '#FF4E00', background: 'rgba(255,78,0,0.06)', color: '#FF4E00' }}
              role="alert"
              aria-live="polite"
            >
              <AlertCircle className="mt-0.5 h-4 w-4 shrink-0" aria-hidden="true" />
              <div className="space-y-1">
                <p className="font-semibold">No se pudo cargar el historial de identidad.</p>
                <p className="opacity-80">{error}</p>
                <button
                  type="button"
                  onClick={() => void load()}
                  className="mt-1 rounded-lg px-2.5 py-1 text-xs font-semibold text-white"
                  style={{ background: '#FF4E00' }}
                >
                  Reintentar
                </button>
              </div>
            </div>
          )}

          {initialLoading && (
            <div role="status" aria-live="polite" aria-busy="true" className="space-y-2">
              <span className="sr-only">Cargando historial de identidad…</span>
              {[0, 1, 2].map((i) => (
                <div key={i} className="h-10 animate-pulse rounded-lg bg-black/5 dark:bg-white/5" aria-hidden />
              ))}
            </div>
          )}

          {!initialLoading && !error && hasData && actors.length === 0 && (
            <p className="text-xs opacity-60">
              No hay actores registrados en este trámite todavía.
            </p>
          )}

          {!initialLoading && !error && hasData && actors.length > 0 && (
            <div className="space-y-3 text-xs">
              {(accionables.length > 0 || reminders.length > 0) && (
                <AlertsBanner alerts={accionables} reminders={reminders} />
              )}

              {/* HU #11666 — por qué no se envió la validación de identidad de una parte. Se lista
                  junto a las alertas porque responde la misma pregunta del gestor («¿por qué este
                  trámite no avanza?»), pero cada motivo conserva su naturaleza: los informativos
                  no son alertas. */}
              {motivos.map((m) => (
                <MotivoNoEnvioRow key={`${m.parte}-${m.codigo}`} motivo={m} />
              ))}

              {outcomes.length === 0 ? (
                <p className="text-xs opacity-60">
                  Aún no hay aprobaciones ni rechazos registrados.
                </p>
              ) : (
                <ol className="space-y-2" aria-label="Aprobaciones y rechazos de identidad">
                  {outcomes.map((row, index) => {
                    const v = row.validation;
                    const label = ESTADO_LABEL[v.status] ?? v.status;
                    const tone = ESTADO_TONE[v.status] ?? 'neutral';
                    const when =
                      v.status === 'aprobado' ? v.validatedAt : v.validatedAt ?? v.expiresAt;
                    return (
                      <li
                        key={v.id}
                        className="flex flex-wrap items-start justify-between gap-2 border-b border-slate-100 pb-2 last:border-0 last:pb-0 dark:border-white/10"
                      >
                        <div className="min-w-0 space-y-0.5">
                          <div className="flex flex-wrap items-center gap-1.5">
                            <span className="font-semibold opacity-70">{index + 1}.</span>
                            <StatusBadge label={label} tone={tone} />
                            <span className="font-semibold text-[#162744] dark:text-white">
                              {row.actorLabel}
                            </span>
                            {row.vigente && (
                              <span
                                className="rounded-full px-1.5 py-0.5 text-xs font-semibold"
                                style={{ background: 'rgba(85,126,255,0.12)', color: '#557EFF' }}
                              >
                                Vigente
                              </span>
                            )}
                          </div>
                          {row.actorName && (
                            <p className="truncate text-xs opacity-70" title={row.actorName}>
                              {row.actorName}
                            </p>
                          )}
                          {v.status === 'rechazado' && v.rejectionReason && (
                            <p className="truncate text-xs opacity-60" title={v.rejectionReason}>
                              {v.rejectionReason}
                            </p>
                          )}
                        </div>
                        <time
                          className="shrink-0 text-xs font-medium opacity-65"
                          dateTime={when ?? undefined}
                        >
                          {formatFecha(when)}
                        </time>
                      </li>
                    );
                  })}
                </ol>
              )}
            </div>
          )}
        </div>
      ) : null}
    </section>
  );
}

/**
 * HU #11666 — una línea por motivo de no envío, con la misma división que la tarjeta del paso de
 * identidad: bloqueo (se anuncia, hay algo que corregir) frente a información (ausencia legítima,
 * tono neutro y sin sugerir corrección). El texto sale del mismo mapa de copy que la tarjeta, para
 * que el gestor no lea dos explicaciones distintas del mismo código.
 */
function MotivoNoEnvioRow({ motivo }: { motivo: EnvioValidacionMotivo }) {
  const parte = parteLabel(motivo.parte);
  const copy = presentarMotivoNoEnvio(motivo.codigo, parte);
  const bloqueo = !motivo.informativo && copy.naturaleza === 'bloqueo';
  const { color, background, border } = INLINE_ALERT_TONES[bloqueo ? 'error' : 'info'];
  const Icono = bloqueo ? AlertTriangle : Info;

  return (
    <div
      className="flex items-start gap-2 rounded-xl border p-3 text-xs"
      style={{ borderColor: border, background, color }}
      role={bloqueo ? 'alert' : 'status'}
      aria-live="polite"
    >
      <Icono className="mt-0.5 h-4 w-4 shrink-0" aria-hidden="true" />
      <div className="space-y-0.5">
        <p className="font-semibold">
          {parte}: {copy.titulo}
        </p>
        <p>{copy.detalle}</p>
      </div>
    </div>
  );
}

function AlertsBanner({
  alerts,
  reminders,
}: {
  alerts: IdentityValidationAlert[];
  reminders: IdentityValidationAlert[];
}) {
  return (
    <div
      className="flex items-start gap-2 rounded-xl border p-3 text-xs"
      style={{
        borderColor: 'var(--badge-warning-border)',
        background: 'var(--badge-warning-bg)',
        color: 'var(--badge-warning-fg)',
      }}
      role="alert"
      aria-live="polite"
    >
      <AlertTriangle className="mt-0.5 h-4 w-4 shrink-0" aria-hidden="true" />
      <div className="space-y-1">
        {alerts.length > 0 && (
          <p className="font-semibold">
            {alerts.length} validación{alerts.length === 1 ? '' : 'es'} requiere
            {alerts.length === 1 ? '' : 'n'} atención:{' '}
            {alerts
              .map((a) => {
                const parte = parteLabel(a.partyRole, a.name);
                const kind = a.alertKind ? ALERT_META[a.alertKind].label : 'Atención';
                return `${parte} (${kind})`;
              })
              .join(', ')}
            .
          </p>
        )}
        {reminders.length > 0 && (
          <p className="opacity-90">
            Recordatorio: reenvía el enlace de captura
            {reminders.some((r) => r.partyRole)
              ? ` a ${reminders.map((r) => parteLabel(r.partyRole, r.name)).join(', ')}`
              : ''}
            .
          </p>
        )}
      </div>
    </div>
  );
}

function formatFecha(iso: string | null | undefined): string {
  if (!iso) return '—';
  const d = new Date(iso);
  if (Number.isNaN(d.getTime())) return iso;
  return new Intl.DateTimeFormat('es-CO', { dateStyle: 'medium', timeStyle: 'short' }).format(d);
}
