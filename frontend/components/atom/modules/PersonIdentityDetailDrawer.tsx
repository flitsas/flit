'use client';

import { useCallback, useEffect, useId, useRef, useState } from 'react';
import { Copy } from "lucide-react";
import { Icono } from "@flit/ui/Icono";
import { tramitesClient } from '@/lib/api/tramites-client';
import { StatusBadge, type StatusTone } from '@/components/atom/StatusBadge';
import { ApprovalOriginChip, puedeVerOrigenAprobacion } from '@/components/atom/modules/ApprovalOriginChip';
import { IdentityValidationTrackingPanel } from '@/components/atom/IdentityValidationTrackingPanel';
import {
  AssociatedProceduresList,
  buildAssociatedProcedures,
} from '@/components/atom/modules/AssociatedProceduresList';
import {
  hasKyverumCaptureQr,
  IdentityCaptureLinkBlock,
} from '@/components/atom/modules/IdentityCaptureLinkBlock';
import { IdentityManualFlowActions } from '@/components/atom/modules/IdentityManualFlowActions';
import { MANUAL_ESTADO_META } from '@/lib/identity/manual-flow';
import { CertificadoIdentidadAccion } from '@/components/atom/modules/CertificadoIdentidadAccion';
import { etiquetaMotivoRechazoManual } from '@/lib/identidad/motivos-rechazo-manual';
import { ETIQUETA_SOLO_CONSULTA } from '@/lib/tramites/network-scope';
import { InlineAlert } from '@/components/atom/InlineAlert';
import { useReconcileIdentity } from '@/hooks/useReconcileIdentity';
import type {
  BiometricEstado,
  BiometricValidation,
  PersonBiometricValidationsResponse,
} from '@/lib/api/types/procedure-runtime';

import { formatFechaHora } from '@/lib/format/date';
import { buttonClass } from '@flit/ui/Button';
import {
  DIALOGO_FRANJA,
  DIALOGO_FRANJA_CERRAR,
  DIALOGO_FRANJA_CIRCULO,
  DIALOGO_FRANJA_TITULO,
  DIALOGO_PANEL,
} from '@/components/operacion/dialogo-propio';
import { MODAL_VELO_CLS } from "@flit/ui/Modal";
/**

 * Detalle multi-validación por persona (HU #11273 / CF-06 / ADR-0040).
 * Cabecera personal = mismo diseño que PrevalidacionDetailDrawer (develop).
 * Debajo: acordeón con un ítem por validación (Intentos, Enlace, Score, Trámites, Tracking).
 */

const ESTADO_META: Record<BiometricEstado, { label: string; tone: StatusTone }> = {
  enviado: { label: 'Enviado', tone: 'info' },
  en_proceso: { label: 'En proceso', tone: 'warning' },
  aprobado: { label: 'Aprobado', tone: 'success' },
  rechazado: { label: 'Rechazado', tone: 'danger' },
  expirado: { label: 'Expirado', tone: 'warning' },
  pendiente_envio: { label: 'Pendiente de envío', tone: 'info' },
  error_envio: { label: 'Error de envío', tone: 'danger' },
  ...MANUAL_ESTADO_META,
};

const POLL_MS = 5000;

function isTerminal(v: BiometricValidation): boolean {
  return v.status === 'aprobado' || v.status === 'rechazado' || v.status === 'expirado' || v.expired;
}

/**
 * Estados en vuelo que el proveedor aún puede resolver (los que se le consultan desde «Actualizar estado»).
 * `pendiente_envio` NO entra: aún no salió al proveedor (sin id de Verify) y el backend respondería 409.
 */
const ESTADOS_RECONCILIABLES: ReadonlySet<BiometricEstado> = new Set(['enviado', 'en_proceso']);

/** Validación Kyverum no final: candidata a consultar al proveedor. */
function esReconciliable(v: BiometricValidation): boolean {
  return v.provider === 'kyverum' && !isTerminal(v) && ESTADOS_RECONCILIABLES.has(v.status);
}

function formatFecha(iso: string | null | undefined): string {
  if (!iso) return '—';
  const d = new Date(iso);
  if (Number.isNaN(d.getTime())) return iso;
  return formatFechaHora(d);
}

export interface PersonIdentityDetailDrawerProps {
  documentType: string;
  documentNumber: string;
  onClose: () => void;
  onStatusChanged?: () => void;
  /**
   * HU #12709 — persona de una compañía HIJA vista por la cabeza de red: el historial se lee por la ruta
   * `network/**` de esa compañía y el detalle es SOLO CONSULTA (sin enlace de captura, sin abrir los
   * trámites de la hija, bitácora por la ruta de red). Ausente = la vista propia de siempre.
   */
  networkTenantId?: string;
  /**
   * Historial de la validación PROPIA de un mandatario (fila «Mandatario»): se pide aparte y es solo consulta;
   * se gestiona desde la ficha del mandatario.
   */
  mandatario?: boolean;
  /** Super Admin: abre el detalle de una validación manual en su pestaña (el detalle actual se cierra aparte). */
  onVerEnManuales?: (validationId: string) => void;
  /**
   * El usuario GESTIONA el módulo de identidad (`validaciones.manage` o SuperAdmin): «Actualizar estado»
   * consulta a Verify las validaciones Kyverum no finales antes de recargar. Ausente = solo recarga.
   * En red o mandatario nunca consulta (solo consulta por contrato).
   */
  canManage?: boolean;
  /**
   * Compañía de la persona (fila del listado). La consulta al proveedor sale con ese `X-Tenant-Id`: un
   * SuperAdmin que mira otra compañía recibiría 404 con el suyo. Ausente = tenant activo del cliente.
   */
  tenantId?: string;
}

export function PersonIdentityDetailDrawer({
  documentType,
  documentNumber,
  onClose,
  onStatusChanged,
  networkTenantId,
  mandatario = false,
  onVerEnManuales,
  canManage = false,
  tenantId,
}: PersonIdentityDetailDrawerProps) {
  const esRed = Boolean(networkTenantId);
  const soloConsulta = esRed || mandatario;
  const puedeConsultarProveedor = canManage && !soloConsulta;
  const reconcile = useReconcileIdentity();
  /** Consulta al proveedor en vuelo: corta el doble clic del mismo tick y pausa el polling. */
  const inFlightRef = useRef(false);
  const [data, setData] = useState<PersonBiometricValidationsResponse | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(false);
  const [trackingTick, setTrackingTick] = useState(0);
  const failedRef = useRef(false);
  const allTerminalRef = useRef(false);
  const prevStatusesRef = useRef<string>('');

  /** Recarga el detalle. Devuelve `true` si avisó `onStatusChanged` (algún estado cambió). */
  const load = useCallback(async (): Promise<boolean> => {
    setLoading(true);
    try {
      const res = networkTenantId
        ? await tramitesClient.listNetworkPersonIdentityValidations(networkTenantId, documentType, documentNumber, {
            page: 1,
            pageSize: 50,
            mandatario,
          })
        : await tramitesClient.listPersonBiometricValidations(documentType, documentNumber, {
            page: 1,
            pageSize: 50,
            mandatario,
          });
      failedRef.current = false;
      setError(null);
      setData(res);
      allTerminalRef.current = res.allTerminal;
      setTrackingTick((t) => t + 1);

      const statusKey = res.validations.map((v) => `${v.id}:${v.status}`).join('|');
      const changed = Boolean(prevStatusesRef.current) && prevStatusesRef.current !== statusKey;
      if (changed) onStatusChanged?.();
      prevStatusesRef.current = statusKey;
      return changed;
    } catch (err) {
      failedRef.current = true;
      setError(err instanceof Error ? err.message : 'No se pudo cargar el historial.');
      return false;
    } finally {
      setLoading(false);
    }
  }, [documentType, documentNumber, onStatusChanged, networkTenantId, mandatario]);

  useEffect(() => {
    // eslint-disable-next-line react-hooks/set-state-in-effect -- reset al cambiar documento + fetch async
    setData(null);
    setError(null);
    failedRef.current = false;
    allTerminalRef.current = false;
    prevStatusesRef.current = '';
    void load();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [documentType, documentNumber]);

  useEffect(() => {
    if (!data || data.allTerminal || failedRef.current) return;
    const id = window.setInterval(() => {
      if (document.visibilityState !== 'visible') return;
      if (failedRef.current || allTerminalRef.current || inFlightRef.current) return;
      void load();
    }, POLL_MS);
    return () => window.clearInterval(id);
  }, [data, load]);

  useEffect(() => {
    function onKey(e: KeyboardEvent) {
      if (e.key === 'Escape') onClose();
    }
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  }, [onClose]);

  /** Más reciente → más antigua. Si falta createdAt (API antigua), se respeta el orden del backend. */
  const validationsDesc = data
    ? (() => {
        const list = [...data.validations];
        const hasCreated = list.some((v) => Boolean(v.createdAt));
        if (!hasCreated) return list;
        return list.sort((a, b) => {
          const ta = a.createdAt ? Date.parse(a.createdAt) : 0;
          const tb = b.createdAt ? Date.parse(b.createdAt) : 0;
          if (tb !== ta) return tb - ta;
          return b.id.localeCompare(a.id);
        });
      })()
    : [];
  const latest = validationsDesc[0] ?? null;
  const latestMeta = latest ? (ESTADO_META[latest.status] ?? ESTADO_META.enviado) : null;
  const awaiting =
    latest != null &&
    !isTerminal(latest) &&
    (latest.status === 'en_proceso' ||
      latest.status === 'enviado' ||
      latest.status === 'pendiente_envio');
  const personName = data?.name ?? latest?.name ?? 'Persona';
  const reconciliables = puedeConsultarProveedor ? validationsDesc.filter(esReconciliable) : [];
  const consultaProveedor = reconciliables.length > 0;
  const ocupado = reconcile.submitting;

  /**
   * «Actualizar estado»: con permiso de gestionar (y fuera de red/mandatario) consulta a Verify las
   * validaciones Kyverum no finales, en secuencia, y luego recarga; si alguna cambió, avisa al listado.
   * Sin candidatas o sin permiso, solo recarga, como siempre.
   */
  const handleActualizar = async () => {
    if (inFlightRef.current) return;
    inFlightRef.current = true;
    try {
      if (!consultaProveedor) {
        reconcile.clearMessage();
        await load();
        return;
      }
      const summary = await reconcile.runMany(
        reconciliables.map((v) => v.id),
        (id) => tramitesClient.reconcileBiometricValidation(id, tenantId),
      );
      // `load()` rearma el polling de 5 s (failedRef = false) si la recarga sale bien.
      const avisado = await load();
      if (summary.updated > 0 && !avisado) onStatusChanged?.();
    } finally {
      inFlightRef.current = false;
    }
  };

  return (
    <div
      className="fixed inset-0 z-50 flex items-center justify-center p-4"
      role="dialog"
      aria-modal="true"
      aria-labelledby="person-identity-process-title"
    >
      <button
        type="button"
        className={`absolute inset-0 ${MODAL_VELO_CLS}`}
        aria-label="Cerrar panel"
        onClick={onClose}
      />
      <div
        className={`${DIALOGO_PANEL} z-10 flex max-h-[90vh] w-full max-w-5xl flex-col`}
      >
        <header className={`${DIALOGO_FRANJA} mb-4 justify-between`}>
          <span aria-hidden="true" className={DIALOGO_FRANJA_CIRCULO} />
          <h2
            id="person-identity-process-title"
            className={`${DIALOGO_FRANJA_TITULO} truncate`}
          >
            Historial y tracking de identidad
          </h2>
          {mandatario ? (
            <span className="relative z-[1] inline-flex rounded-full bg-[var(--di-surface-elevated)]">
              <StatusBadge label="Mandatario" tone="info" ariaLabel="Validación propia del mandatario" />
            </span>
          ) : null}
          {soloConsulta ? (
            <span className="relative z-[1] inline-flex rounded-full bg-[var(--di-surface-elevated)]">
              <StatusBadge label={ETIQUETA_SOLO_CONSULTA} tone="neutral" ariaLabel="Detalle en solo consulta" />
            </span>
          ) : null}
          <div className="relative z-[1] flex items-center gap-2 shrink-0">
            <button
              type="button"
              onClick={() => void handleActualizar()}
              disabled={ocupado}
              aria-busy={ocupado}
              className={buttonClass({ size: 'sm' })}
              aria-label={consultaProveedor ? 'Actualizar estado consultando al proveedor' : 'Actualizar estado'}
            >
              <Icono nombre="reintento" className={`h-3.5 w-3.5 ${loading || ocupado ? 'animate-spin' : ''}`} />
              Actualizar estado
            </button>
            <button
              type="button"
              onClick={onClose}
              aria-label="Cerrar proceso"
              className={DIALOGO_FRANJA_CERRAR}
            >
              <Icono nombre="cerrar" className="h-5 w-5" />
            </button>
          </div>
        </header>

        <div className="min-h-0 flex-1 space-y-4 overflow-y-auto px-6 pb-5">
          {loading && !data && !error && (
            <div role="status" aria-live="polite" aria-busy="true" className="space-y-3">
              <span className="sr-only">Cargando proceso…</span>
              <div className="h-4 w-2/3 animate-pulse rounded bg-secondary" />
              <div className="h-24 w-full animate-pulse rounded-xl bg-secondary" />
              <div className="h-32 w-full animate-pulse rounded-xl bg-secondary" />
            </div>
          )}

          {error && !data && (
            <div
              className="flex items-start gap-2 rounded-xl border p-3 text-xs"
              style={{ borderColor: 'var(--destructive)', background: 'var(--di-danger-soft)', color: 'var(--destructive)' }}
              role="alert"
            >
              <Icono nombre="alerta" className="mt-0.5 h-4 w-4 shrink-0" />
              <div>
                <p className="font-semibold">No se pudo cargar el historial.</p>
                <p className="opacity-80">{error}</p>
              </div>
            </div>
          )}

          {reconcile.message && (
            <InlineAlert tone={reconcile.message.tone}>{reconcile.message.text}</InlineAlert>
          )}

          {/* L3 — la recarga falló con datos ya pintados: el refresco automático se cortó (failedRef). */}
          {error && data && (
            <InlineAlert tone="warning" title="No se pudo actualizar el historial.">
              {error} El refresco automático se detuvo; pulsa «Actualizar estado» para reintentar.
            </InlineAlert>
          )}

          {!loading && data && data.validations.length === 0 && (
            <div className="text-center py-10 text-xs opacity-70">
              <Icono nombre="biometria" className="mx-auto h-8 w-8 opacity-30" />
              <p className="mt-2">Sin validaciones para este documento.</p>
            </div>
          )}

          {data && latest && latestMeta && (
            <div className="space-y-4 text-xs">
              <div
                className="rounded-xl border bg-card px-4 py-3"
                style={{ borderColor: 'var(--border)' }}
              >
                <div className="flex flex-wrap items-center gap-2">
                  {awaiting ? (
                    <div className="flex min-w-0 items-center gap-2">
                      <Icono nombre="reintento" className="h-3.5 w-3.5 animate-spin" style={{ color: 'var(--di-action)' }} />
                      <p className="text-xs font-semibold" style={{ color: 'var(--di-action)' }}>
                        Esperando validación de {personName}
                      </p>
                    </div>
                  ) : (
                    <p className="text-[15px] font-bold uppercase tracking-wide text-foreground">
                      {personName}
                    </p>
                  )}
                  <span
                    className="rounded-full px-2.5 py-0.5 font-mono text-[11px] font-semibold"
                    style={{ background: 'var(--di-info-soft)', color: 'var(--di-action)' }}
                  >
                    {latest.documentType} {latest.documentNumber}
                  </span>
                  <StatusBadge
                    label={latestMeta.label}
                    tone={latestMeta.tone}
                    ariaLabel={`Estado de la última validación: ${latestMeta.label}`}
                    wrap
                  />
                </div>
                <p className="mt-1 text-[12px]" style={{ color: 'var(--muted-foreground)' }}>
                  {latest.email || '—'}
                </p>
              </div>

              {/* Acordeón: un ítem por validación (más reciente → más antigua) */}
              <div className="space-y-2" role="list" aria-label="Historial de validaciones de identidad, de la más reciente a la más antigua">
                {validationsDesc.map((v, idx) => (
                  <ValidationAccordionItem
                    key={v.id}
                    validation={v}
                    index={idx}
                    total={validationsDesc.length}
                    defaultOpen={idx === 0}
                    trackingTick={trackingTick}
                    soloConsulta={soloConsulta}
                    red={esRed}
                    onManualChanged={() => {
                      void load();
                      // La lista de abajo y el contador de «Validaciones manuales» se actualizan al instante.
                      onStatusChanged?.();
                    }}
                    onVerEnManuales={onVerEnManuales}
                  />
                ))}
              </div>
            </div>
          )}
        </div>
      </div>
    </div>
  );
}

function ValidationAccordionItem({
  validation: v,
  index,
  defaultOpen,
  trackingTick,
  soloConsulta = false,
  red = false,
  onManualChanged,
  onVerEnManuales,
}: {
  validation: BiometricValidation;
  /** HU #13288 — recarga el detalle tras activar el flujo manual o regenerar el enlace. */
  onManualChanged?: () => void;
  onVerEnManuales?: (validationId: string) => void;
  /** HU #12709 — persona de una compañía hija vista por la cabeza: sin captura ni enlaces a trámites. */
  soloConsulta?: boolean;
  /** Persona de una compañía hija: la bitácora se lee por la ruta de red (el mandatario es solo consulta, no red). */
  red?: boolean;
  index: number;
  total: number;
  defaultOpen: boolean;
  trackingTick: number;
}) {
  const [open, setOpen] = useState(defaultOpen);
  const panelId = useId();
  const showCaptura = !soloConsulta && hasKyverumCaptureQr(v.captureUrl);
  // Rechazada del flujo manual: el motivo que eligió el Super Admin (etiqueta homologada); Kyverum/mock, el texto de siempre.
  const motivoManual =
    v.status === 'rechazado' && v.provider === 'manual' ? etiquetaMotivoRechazoManual(v.rejectionReasonCode) : null;
  const motivoRechazo = motivoManual ?? v.rejectionReason;
  const title = index === 0 ? 'Sesión más reciente' : 'Sesión anterior / Histórica';
  const enlaceTone: StatusTone = v.expired ? 'warning' : 'success';
  const enlaceEstado = v.expired ? 'Vencido' : 'Vigente';

  const associated = buildAssociatedProcedures({
    instanceId: v.procedureInstanceId,
    referenceNumber: v.referenceNumber,
    modalidad: v.modalidad,
    linkedProcedures: v.linkedProcedures,
  });

  return (
    <div className="rounded-xl border overflow-hidden" role="listitem" style={{ borderColor: 'var(--border)' }}>
      <button
        type="button"
        className="flex w-full items-center justify-between gap-3 px-4 py-3 text-left hover:bg-[var(--badge-info-bg)] focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-[-2px] focus-visible:outline-flit-brand"
        aria-expanded={open}
        aria-controls={panelId}
        onClick={() => setOpen((o) => !o)}
      >
        <span className="text-[13px] font-semibold text-foreground">{title}</span>
        <Icono nombre="abajo" className={`h-4 w-4 shrink-0 transition-transform ${open ? 'rotate-180' : ''}`} style={{ color: 'var(--di-action)' }} />
      </button>

      {open && (
        <div id={panelId} className="space-y-3 border-t px-3 py-3" style={{ borderColor: 'var(--border)' }}>
          <div
            className="grid gap-3 rounded-xl border px-3 py-3 sm:grid-cols-3"
            style={{ borderColor: 'var(--border)' }}
          >
            <SessionStat
              label="Asociado a trámite"
              value={v.referenceNumber ?? '—'}
              accent={Boolean(v.referenceNumber)}
            />
            <SessionStat label="Fecha de registro" value={formatFecha(v.createdAt)} />
            {/* El flujo manual no tiene contador de intentos del proveedor (los reenvíos están en la bitácora). */}
            {v.provider !== 'manual' && (
              <SessionStat
                label={v.provider === 'mock' ? 'Intentos' : 'Intentos Kyverum'}
                value={`${v.intentos} / ${v.maxIntentos}`}
              />
            )}
            <SessionStat
              label="Estado del enlace"
              value={enlaceEstado}
              badgeTone={enlaceTone}
            />
            <SessionStat
              label="Score biométrico"
              value={v.score != null ? String(v.score) : '—'}
            />
            <SessionStat label="Fecha aprobación" value={formatFecha(v.validatedAt)} />
            <VerifyIdStat verificationId={v.kyverumVerificationId} />
            {puedeVerOrigenAprobacion(v.approvalOrigin) && v.approvalOrigin ? (
              <div className="min-w-0">
                <p className="text-xs font-semibold uppercase tracking-wide opacity-70">Origen de la aprobación</p>
                <div className="mt-1">
                  <ApprovalOriginChip origin={v.approvalOrigin} />
                </div>
              </div>
            ) : null}
          </div>

          {!soloConsulta && onManualChanged && (
            <IdentityManualFlowActions validation={v} onChanged={onManualChanged} onVerEnManuales={onVerEnManuales} />
          )}

          {/* HU #14303 — carta de la validación manual aprobada; con trámite por instancia, sin trámite por validación. */}
          {!soloConsulta && v.provider === 'manual' ? (
            <CertificadoIdentidadAccion validation={v} instanceId={v.procedureInstanceId} />
          ) : null}

          {associated.length > 0 && (
            <AssociatedProceduresList procedures={associated} collapsible linkable={!soloConsulta} />
          )}

          {v.status === 'rechazado' && motivoRechazo && (
            <p
              className="rounded-xl px-3 py-2 text-[11px]"
              style={{ background: 'var(--di-danger-soft)', color: 'var(--destructive)' }}
            >
              Motivo del rechazo: {motivoRechazo}
            </p>
          )}

          {v.ultimoIntentoMotivo && !isTerminal(v) && (
            <div
              className="rounded-xl p-3 text-[11px]"
              style={{
                background: 'var(--di-warning-soft)',
                border: '1px solid color-mix(in srgb, var(--di-warning) 30%, transparent)',
                color: 'var(--di-warning)',
              }}
              role="status"
              aria-live="polite"
            >
              <span className="font-semibold">
                Intento {v.intentos} de {v.maxIntentos} no pasó.
              </span>{' '}
              {v.ultimoIntentoMotivo}{' '}
              {v.maxIntentos - v.intentos > 0
                ? `Quedan ${v.maxIntentos - v.intentos} intento(s) en el móvil.`
                : 'No quedan intentos: el estado pasará a Rechazado al confirmarse.'}
            </div>
          )}

          {showCaptura && <IdentityCaptureLinkBlock captureUrl={v.captureUrl!} provider={v.provider} />}

          <div className="rounded-xl border p-3" style={{ borderColor: 'var(--border)' }}>
            <p className="mb-1 text-[13px] font-semibold text-foreground">
              Tracking de auditoría y proceso (tiempo real)
            </p>
            <p className="mb-2 text-[11px]" style={{ color: 'var(--muted-foreground)' }}>
              Historial cronológico de eventos, cifrado y respuestas del proveedor.
            </p>
            <IdentityValidationTrackingPanel
              validationId={v.id}
              refreshKey={trackingTick}
              defaultOpen
              embebido
              detailLayout
              network={red}
            />
          </div>
        </div>
      )}
    </div>
  );
}

/**
 * Ajustes 2026-11-10 — id de la verificación en Kyverum Verify, para soporte con el proveedor. Solo lectura:
 * se ve también en solo consulta (red / mandatario). Sin id → «—» y sin botón.
 */
function VerifyIdStat({ verificationId }: { verificationId?: string | null }) {
  const [copied, setCopied] = useState(false);
  const id = verificationId?.trim() ?? '';

  useEffect(() => {
    if (!copied) return;
    const t = setTimeout(() => setCopied(false), 2000);
    return () => clearTimeout(t);
  }, [copied]);

  const copy = async () => {
    try {
      await navigator.clipboard?.writeText(id);
      setCopied(true);
    } catch {
      /* clipboard no disponible: el valor sigue siendo seleccionable */
    }
  };

  return (
    <div className="min-w-0">
      <p className="text-xs font-semibold uppercase tracking-wide opacity-70">ID de Verify</p>
      {id ? (
        <div className="mt-0.5 flex items-start gap-1.5">
          <span
            className="min-w-0 select-all break-all font-mono text-[12px] font-semibold"
            style={{ color: 'var(--di-action)' }}
          >
            {id}
          </span>
          <button
            type="button"
            onClick={() => void copy()}
            aria-label="Copiar ID de Verify"
            title="Copiar ID de Verify"
            className="grid h-6 w-6 shrink-0 place-items-center rounded-full border hover:bg-[var(--badge-info-bg)] focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-flit-brand"
            style={{ borderColor: 'var(--di-action)', color: 'var(--di-action)' }}
          >
            {copied ? <Icono nombre="check" className="h-3 w-3" /> : <Copy className="h-3 w-3" aria-hidden />}
          </button>
        </div>
      ) : (
        <p className="mt-0.5 text-[12px] font-semibold" style={{ color: 'var(--foreground)' }}>
          —
        </p>
      )}
      {id ? (
        <span role="status" aria-live="polite" className="text-[11px] font-semibold" style={{ color: 'var(--foreground)' }}>
          {copied ? 'Copiado' : ''}
        </span>
      ) : null}
    </div>
  );
}

function SessionStat({
  label,
  value,
  accent = false,
  badgeTone,
}: {
  label: string;
  value: string;
  accent?: boolean;
  badgeTone?: StatusTone;
}) {
  return (
    <div className="min-w-0">
      <p className="text-xs font-semibold uppercase tracking-wide opacity-70">{label}</p>
      {badgeTone ? (
        <div className="mt-1">
          <StatusBadge label={value} tone={badgeTone} ariaLabel={`${label}: ${value}`} />
        </div>
      ) : (
        <p
          className={`mt-0.5 truncate text-[12px] font-semibold ${accent ? 'font-mono' : ''}`}
          style={{ color: accent ? 'var(--di-action)' : 'var(--foreground)' }}
        >
          {value}
        </p>
      )}
    </div>
  );
}
