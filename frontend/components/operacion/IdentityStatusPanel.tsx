'use client';

import { useCallback, useEffect, useState } from 'react';
import {
  AlertCircle,
  AlertTriangle,
  Bell,
  Clock,
  RefreshCw,
  ShieldCheck,
  XCircle,
} from 'lucide-react';
import { StatusBadge, type StatusTone } from '@/components/atom/StatusBadge';
import { tramitesClient } from '@/lib/api/tramites-client';
import type {
  BiometricParte,
  BiometricValidation,
  IdentityValidationAlert,
  IdentityValidationAlertKind,
  ProcedureActor,
  WizardModalidad,
} from '@/lib/api/types/procedure-runtime';

/**
 * HU #10875 (Feature #10863) — Vista consolidada de identidad por trámite con alertas.
 *
 * AC1: un solo panel con el estado de identidad de TODOS los actores del trámite (comprador y, en
 * traspaso, vendedor — mismo universo de partes que `ProcedureActor.rol`/`BiometricParte`).
 *
 * AC2: alertas/recordatorios in-app POR PULL (badges por actor + banner de resumen), SIN campana ni
 * notificación push — la decisión de alcance cerrada de HU #10873 descarta esa infraestructura. Se
 * consume `GET /instances/{id}/identity-validation/alerts`, que clasifica cada validación en
 * `rechazada` | `expirada` | `por_vencer` | `atascada` y marca `requiresResendReminder`.
 *
 * Complementa (no reemplaza) `BiometricStep`: ese paso ofrece la ACCIÓN de iniciar/reintentar una
 * parte dentro del paso "Identidad"/"FUR" del wizard; este panel da la VISIBILIDAD consolidada +
 * las alertas, visible sin importar en qué paso está el operador.
 */

const PARTE_LABEL: Record<BiometricParte, string> = {
  comprador: 'Comprador',
  vendedor: 'Vendedor',
};

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

interface ActorIdentityRow {
  parte: BiometricParte;
  label: string;
  actor: ProcedureActor | null;
  validation: BiometricValidation | null;
  alert: IdentityValidationAlert | null;
}

/**
 * AC1 — la fuente de "todos los actores" es SIEMPRE `getActors` (lo que el gestor ya guardó en el
 * paso de actores), no una lista asumida por modalidad: con el trámite recién creado y sin actores
 * capturados, el panel debe mostrar el estado VACÍO real. Cada actor se empareja con su validación
 * más reciente (misma regla parte↔validación que `BiometricStep`) y, si existe, con su alerta.
 */
function buildRows(
  modalidad: WizardModalidad,
  actors: ProcedureActor[],
  validations: BiometricValidation[],
  alerts: IdentityValidationAlert[],
): ActorIdentityRow[] {
  return actors.map((actor) => {
    const parte = actor.rol;
    const matches = validations.filter((v) =>
      modalidad === 'traspaso'
        ? v.partyRole === parte
        : v.partyRole === null || v.partyRole === 'comprador',
    );
    const validation = matches.length > 0 ? matches[matches.length - 1] : null;
    const alert = validation ? (alerts.find((al) => al.id === validation.id) ?? null) : null;
    return { parte, label: PARTE_LABEL[parte], actor, validation, alert };
  });
}

export function IdentityStatusPanel({
  instanceId,
  modalidad,
}: {
  instanceId: string | null;
  /** Opcional: si no se pasa, el panel resuelve la modalidad solo (getWizardState). Así es
   *  autosuficiente y se puede montar fuera del wizard (p. ej. en el detalle del trámite). */
  modalidad?: WizardModalidad;
}) {
  const [actors, setActors] = useState<ProcedureActor[] | null>(null);
  const [validations, setValidations] = useState<BiometricValidation[] | null>(null);
  const [alerts, setAlerts] = useState<IdentityValidationAlert[] | null>(null);
  const [wizardModalidad, setWizardModalidad] = useState<WizardModalidad | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(false);

  const load = useCallback(async () => {
    if (!instanceId) return;
    setLoading(true);
    setError(null);
    try {
      const [wizardRes, actorsRes, biometricRes, alertsRes] = await Promise.all([
        tramitesClient.getWizardState(instanceId),
        tramitesClient.getActors(instanceId),
        tramitesClient.getBiometricState(instanceId),
        tramitesClient.getInstanceIdentityValidationAlerts(instanceId),
      ]);
      setWizardModalidad(wizardRes?.modalidad ?? null);
      setActors(actorsRes);
      setValidations(biometricRes.validations);
      setAlerts(alertsRes.alerts);
    } catch (err) {
      setError(
        err instanceof Error ? err.message : 'No se pudo cargar el estado de identidad.',
      );
    } finally {
      setLoading(false);
    }
  }, [instanceId]);

  // Carga inicial: setLoading/setError se disparan de inmediato para pintar el skeleton (mismo
  // patrón que el resto del repo).
  useEffect(() => {
    // eslint-disable-next-line react-hooks/set-state-in-effect
    void load();
  }, [load]);

  // Trámite aún sin crear (paso 1 diferido, HU #10883): no hay instancia que consultar.
  if (!instanceId) return null;

  // AC1/AC2 — 4 estados de UI: cargando (skeleton), error (role="alert" + reintentar), vacío
  // (sin actores todavía) y lleno (tarjetas por actor + banner de alertas cuando aplica).
  const hasData = actors !== null && validations !== null && alerts !== null;
  const initialLoading = loading && !hasData && error === null;
  // Modalidad efectiva: prop (si el padre la pasa) o la resuelta por getWizardState; traspaso por
  // defecto (empareja por party_role, el caso más común).
  const effModalidad = modalidad ?? wizardModalidad ?? 'traspaso';
  const rows = hasData ? buildRows(effModalidad, actors, validations, alerts) : [];
  const accionables = rows.filter((r) => r.alert?.alertKind);
  const reminders = rows.filter((r) => r.alert?.requiresResendReminder && !r.alert.alertKind);

  return (
    <section
      className="shrink-0 space-y-3 rounded-2xl border bg-white p-4 dark:bg-[#0B0F14]"
      aria-label="Estado de identidad de los actores del trámite"
    >
      <div className="flex items-center justify-between gap-3">
        <div className="flex items-center gap-2">
          <ShieldCheck className="h-4 w-4" style={{ color: '#557EFF' }} aria-hidden="true" />
          <h2 className="text-xs font-bold">Identidad de los actores</h2>
        </div>
        <button
          type="button"
          onClick={() => void load()}
          disabled={loading}
          className="flex items-center gap-1.5 text-[11px] font-semibold opacity-70 disabled:opacity-40 focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2"
          aria-label="Actualizar estado de identidad"
        >
          <RefreshCw className={`h-3 w-3 ${loading ? 'animate-spin' : ''}`} aria-hidden="true" />
          Actualizar
        </button>
      </div>

      {error && (
        <div
          className="flex items-start gap-2 rounded-xl border p-3 text-xs"
          style={{ borderColor: '#FF4E00', background: 'rgba(255,78,0,0.06)', color: '#FF4E00' }}
          role="alert"
          aria-live="polite"
        >
          <AlertCircle className="mt-0.5 h-4 w-4 shrink-0" aria-hidden="true" />
          <div className="space-y-1">
            <p className="font-semibold">No se pudo cargar el estado de identidad.</p>
            <p className="opacity-80">{error}</p>
            <button
              type="button"
              onClick={() => void load()}
              className="mt-1 rounded-lg px-2.5 py-1 text-[11px] font-semibold text-white focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2"
              style={{ background: '#FF4E00' }}
            >
              Reintentar
            </button>
          </div>
        </div>
      )}

      {initialLoading && <PanelSkeleton />}

      {!initialLoading && !error && hasData && rows.length === 0 && (
        <p className="text-[11px] opacity-60">
          No hay actores registrados en este trámite todavía.
        </p>
      )}

      {!initialLoading && !error && rows.length > 0 && (
        <>
          {(accionables.length > 0 || reminders.length > 0) && (
            <AlertsBanner accionables={accionables} reminders={reminders} />
          )}
          <ul className="grid gap-2 sm:grid-cols-2" aria-label="Actores del trámite">
            {rows.map((row) => (
              <ActorIdentityCard key={row.parte} row={row} />
            ))}
          </ul>
        </>
      )}
    </section>
  );
}

/** Estado de carga (4 estados de UI): placeholder accesible mientras llega la primera respuesta. */
function PanelSkeleton() {
  return (
    <div
      className="grid gap-2 sm:grid-cols-2"
      role="status"
      aria-live="polite"
      aria-busy="true"
    >
      <span className="sr-only">Cargando estado de identidad…</span>
      {[0, 1].map((i) => (
        <div
          key={i}
          className="h-16 animate-pulse rounded-xl bg-black/5 dark:bg-white/5"
          aria-hidden="true"
        />
      ))}
    </div>
  );
}

/**
 * AC2 — banner de resumen: agrupa las alertas accionables (rechazada/expirada/por_vencer/atascada) y
 * los recordatorios de reenvío que NO son ya una alerta (para no duplicar el mensaje de la misma parte).
 */
function AlertsBanner({
  accionables,
  reminders,
}: {
  accionables: ActorIdentityRow[];
  reminders: ActorIdentityRow[];
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
        {accionables.length > 0 && (
          <p className="font-semibold">
            {accionables.length} validación{accionables.length === 1 ? '' : 'es'} de identidad
            requiere{accionables.length === 1 ? '' : 'n'} atención:{' '}
            {accionables
              .map((r) => `${r.label} (${ALERT_META[r.alert!.alertKind!].label})`)
              .join(', ')}
            .
          </p>
        )}
        {reminders.length > 0 && (
          <p className="opacity-90">
            Recordatorio: reenvía el enlace de captura a {reminders.map((r) => r.label).join(', ')}.
          </p>
        )}
      </div>
    </div>
  );
}

function ActorIdentityCard({ row }: { row: ActorIdentityRow }) {
  const { label, actor, validation, alert } = row;
  const status = validation?.status ?? null;
  const statusLabel = status ? (ESTADO_LABEL[status] ?? status) : 'Sin iniciar';
  const statusTone: StatusTone = status ? (ESTADO_TONE[status] ?? 'neutral') : 'neutral';
  const alertMeta = alert?.alertKind ? ALERT_META[alert.alertKind] : null;
  const AlertIcon = alertMeta?.icon;
  const soloRecordatorio = !alertMeta && !!alert?.requiresResendReminder;
  const nombre = actor?.nombreCompleto || validation?.name || null;

  const ariaLabel =
    `${label}${nombre ? `: ${nombre}` : ''}, identidad: ${statusLabel}` +
    (alertMeta ? `, alerta: ${alertMeta.label}` : '') +
    (soloRecordatorio ? ', recordatorio de reenvío pendiente' : '');

  return (
    <li
      className="space-y-2 rounded-xl border p-3 text-xs"
      style={alertMeta ? { borderColor: `var(--badge-${alertMeta.tone}-border)` } : undefined}
      aria-label={ariaLabel}
    >
      <div className="flex items-center justify-between gap-2">
        <span className="font-bold">{label}</span>
        <StatusBadge label={statusLabel} tone={statusTone} />
      </div>
      <p className="truncate opacity-80" title={nombre ?? undefined}>
        {nombre ?? 'Sin datos de la parte todavía.'}
      </p>
      {alertMeta && AlertIcon && (
        <div
          className="flex items-center gap-1.5 rounded-lg px-2 py-1 text-[10.5px] font-semibold"
          style={{
            background: `var(--badge-${alertMeta.tone}-bg)`,
            color: `var(--badge-${alertMeta.tone}-fg)`,
          }}
        >
          <AlertIcon className="h-3 w-3 shrink-0" aria-hidden="true" />
          {alertMeta.label}
        </div>
      )}
      {soloRecordatorio && (
        <div
          className="flex items-center gap-1.5 rounded-lg px-2 py-1 text-[10.5px] font-semibold"
          style={{ background: 'var(--badge-info-bg)', color: 'var(--badge-info-fg)' }}
        >
          <Bell className="h-3 w-3 shrink-0" aria-hidden="true" />
          Recordatorio: reenviar enlace de captura
        </div>
      )}
    </li>
  );
}
