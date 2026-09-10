'use client';

import { useCallback, useEffect, useId, useRef, useState } from 'react';
import {
  AlertCircle,
  ChevronDown,
  RefreshCw,
  ScanFace,
  X,
} from 'lucide-react';
import { tramitesClient } from '@/lib/api/tramites-client';
import { StatusBadge, type StatusTone } from '@/components/atom/StatusBadge';
import { IdentityValidationTrackingPanel } from '@/components/atom/IdentityValidationTrackingPanel';
import {
  AssociatedProceduresList,
  buildAssociatedProcedures,
} from '@/components/atom/modules/AssociatedProceduresList';
import {
  hasKyverumCaptureQr,
  IdentityCaptureLinkBlock,
} from '@/components/atom/modules/IdentityCaptureLinkBlock';
import { FLIT } from '@/lib/flit-design-tokens';
import type {
  BiometricEstado,
  BiometricValidation,
  PersonBiometricValidationsResponse,
} from '@/lib/api/types/procedure-runtime';

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
};

const POLL_MS = 5000;

function isTerminal(v: BiometricValidation): boolean {
  return v.status === 'aprobado' || v.status === 'rechazado' || v.status === 'expirado' || v.expired;
}

function formatFecha(iso: string | null | undefined): string {
  if (!iso) return '—';
  const d = new Date(iso);
  if (Number.isNaN(d.getTime())) return iso;
  return new Intl.DateTimeFormat('es-CO', { dateStyle: 'medium', timeStyle: 'short' }).format(d);
}

export interface PersonIdentityDetailDrawerProps {
  documentType: string;
  documentNumber: string;
  onClose: () => void;
  onStatusChanged?: () => void;
}

export function PersonIdentityDetailDrawer({
  documentType,
  documentNumber,
  onClose,
  onStatusChanged,
}: PersonIdentityDetailDrawerProps) {
  const [data, setData] = useState<PersonBiometricValidationsResponse | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(false);
  const [trackingTick, setTrackingTick] = useState(0);
  const failedRef = useRef(false);
  const allTerminalRef = useRef(false);
  const prevStatusesRef = useRef<string>('');

  const load = useCallback(async () => {
    setLoading(true);
    try {
      const res = await tramitesClient.listPersonBiometricValidations(
        documentType,
        documentNumber,
        { page: 1, pageSize: 50 },
      );
      failedRef.current = false;
      setError(null);
      setData(res);
      allTerminalRef.current = res.allTerminal;
      setTrackingTick((t) => t + 1);

      const statusKey = res.validations.map((v) => `${v.id}:${v.status}`).join('|');
      if (prevStatusesRef.current && prevStatusesRef.current !== statusKey) {
        onStatusChanged?.();
      }
      prevStatusesRef.current = statusKey;
    } catch (err) {
      failedRef.current = true;
      setError(err instanceof Error ? err.message : 'No se pudo cargar el historial.');
    } finally {
      setLoading(false);
    }
  }, [documentType, documentNumber, onStatusChanged]);

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
      if (failedRef.current || allTerminalRef.current) return;
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

  return (
    <div
      className="fixed inset-0 z-50 flex items-center justify-center p-4"
      role="dialog"
      aria-modal="true"
      aria-labelledby="person-identity-process-title"
    >
      <button
        type="button"
        className="absolute inset-0"
        style={{ background: 'rgba(22, 39, 68, 0.45)', backdropFilter: 'blur(6px)' }}
        aria-label="Cerrar panel"
        onClick={onClose}
      />
      <div
        className="relative z-10 flex max-h-[90vh] w-full max-w-5xl flex-col overflow-hidden rounded-[18px] border bg-white shadow-2xl dark:bg-[#0B0F14]"
        style={{ borderColor: FLIT.border.soft }}
      >
        <header className="flex shrink-0 items-center justify-between gap-3 px-6 py-4">
          <h2
            id="person-identity-process-title"
            className="min-w-0 truncate text-base font-bold"
            style={{ color: FLIT.brand.blue }}
          >
            Historial y tracking de identidad
          </h2>
          <div className="flex items-center gap-2 shrink-0">
            <button
              type="button"
              onClick={() => void load()}
              className="inline-flex items-center gap-1.5 rounded-full px-4 py-2 text-[12px] font-semibold text-white focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2"
              style={{ background: FLIT.brand.blue, outlineColor: FLIT.brand.blue }}
              aria-label="Actualizar estado"
            >
              <RefreshCw className={`h-3.5 w-3.5 ${loading ? 'animate-spin' : ''}`} aria-hidden="true" />
              Actualizar estado
            </button>
            <button
              type="button"
              onClick={onClose}
              aria-label="Cerrar proceso"
              className="grid h-8 w-8 place-items-center rounded-lg hover:bg-[rgba(79,116,201,0.1)] focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-[#4F74C9]"
            >
              <X className="h-4 w-4 opacity-70" aria-hidden="true" />
            </button>
          </div>
        </header>

        <div className="min-h-0 flex-1 space-y-4 overflow-y-auto px-6 pb-5">
          {loading && !data && !error && (
            <div role="status" aria-live="polite" aria-busy="true" className="space-y-3">
              <span className="sr-only">Cargando proceso…</span>
              <div className="h-4 w-2/3 animate-pulse rounded bg-black/10 dark:bg-white/10" />
              <div className="h-24 w-full animate-pulse rounded-xl bg-black/5 dark:bg-white/5" />
              <div className="h-32 w-full animate-pulse rounded-xl bg-black/5 dark:bg-white/5" />
            </div>
          )}

          {error && !data && (
            <div
              className="flex items-start gap-2 rounded-xl border p-3 text-xs"
              style={{ borderColor: FLIT.state.danger, background: FLIT.dangerAlpha(0.06), color: FLIT.state.danger }}
              role="alert"
            >
              <AlertCircle className="mt-0.5 h-4 w-4 shrink-0" aria-hidden="true" />
              <div>
                <p className="font-semibold">No se pudo cargar el historial.</p>
                <p className="opacity-80">{error}</p>
              </div>
            </div>
          )}

          {!loading && data && data.validations.length === 0 && (
            <div className="text-center py-10 text-xs opacity-70">
              <ScanFace className="mx-auto h-8 w-8 opacity-30" aria-hidden="true" />
              <p className="mt-2">Sin validaciones para este documento.</p>
            </div>
          )}

          {data && latest && latestMeta && (
            <div className="space-y-4 text-xs">
              <div
                className="rounded-xl border bg-white px-4 py-3"
                style={{ borderColor: FLIT.border.soft }}
              >
                <div className="flex flex-wrap items-center gap-2">
                  {awaiting ? (
                    <div className="flex min-w-0 items-center gap-2">
                      <RefreshCw className="h-3.5 w-3.5 animate-spin" style={{ color: FLIT.brand.blue }} aria-hidden />
                      <p className="text-xs font-semibold" style={{ color: FLIT.brand.blue }}>
                        Esperando validación de {personName}
                      </p>
                    </div>
                  ) : (
                    <p className="text-[15px] font-bold uppercase tracking-wide text-[#162744] dark:text-white">
                      {personName}
                    </p>
                  )}
                  <span
                    className="rounded-full px-2.5 py-0.5 font-mono text-[11px] font-semibold"
                    style={{ background: FLIT.blueAlpha(0.12), color: FLIT.brand.blue }}
                  >
                    {latest.documentType} {latest.documentNumber}
                  </span>
                  <StatusBadge
                    label={latestMeta.label}
                    tone={latestMeta.tone}
                    ariaLabel={`Estado de la última validación: ${latestMeta.label}`}
                  />
                </div>
                <p className="mt-1 text-[12px]" style={{ color: FLIT.text.secondary }}>
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
}: {
  validation: BiometricValidation;
  index: number;
  total: number;
  defaultOpen: boolean;
  trackingTick: number;
}) {
  const [open, setOpen] = useState(defaultOpen);
  const panelId = useId();
  const showCaptura = hasKyverumCaptureQr(v.captureUrl);
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
    <div className="rounded-xl border overflow-hidden" role="listitem" style={{ borderColor: FLIT.border.soft }}>
      <button
        type="button"
        className="flex w-full items-center justify-between gap-3 px-4 py-3 text-left hover:bg-[rgba(79,116,201,0.05)] focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-[-2px] focus-visible:outline-[#4F74C9]"
        aria-expanded={open}
        aria-controls={panelId}
        onClick={() => setOpen((o) => !o)}
      >
        <span className="text-[13px] font-semibold text-[#162744] dark:text-white">{title}</span>
        <ChevronDown
          className={`h-4 w-4 shrink-0 transition-transform ${open ? 'rotate-180' : ''}`}
          style={{ color: FLIT.brand.blue }}
          aria-hidden
        />
      </button>

      {open && (
        <div id={panelId} className="space-y-3 border-t px-3 py-3" style={{ borderColor: FLIT.border.soft }}>
          <div
            className="grid gap-3 rounded-xl border px-3 py-3 sm:grid-cols-3"
            style={{ borderColor: FLIT.border.soft }}
          >
            <SessionStat
              label="Asociado a trámite"
              value={v.referenceNumber ?? '—'}
              accent={Boolean(v.referenceNumber)}
            />
            <SessionStat label="Fecha de registro" value={formatFecha(v.createdAt)} />
            <SessionStat label="Intentos Kyverum" value={`${v.intentos} / ${v.maxIntentos}`} />
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
          </div>

          {associated.length > 0 && (
            <AssociatedProceduresList procedures={associated} collapsible />
          )}

          {v.status === 'rechazado' && v.rejectionReason && (
            <p
              className="rounded-xl px-3 py-2 text-[11px]"
              style={{ background: FLIT.dangerAlpha(0.06), color: FLIT.state.danger }}
            >
              Motivo del rechazo: {v.rejectionReason}
            </p>
          )}

          {v.ultimoIntentoMotivo && !isTerminal(v) && (
            <div
              className="rounded-xl p-3 text-[11px]"
              style={{
                background: FLIT.warningAlpha(0.08),
                border: `1px solid ${FLIT.warningAlpha(0.3)}`,
                color: FLIT.state.warning,
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

          {showCaptura && <IdentityCaptureLinkBlock captureUrl={v.captureUrl!} />}

          <div className="rounded-xl border p-3" style={{ borderColor: FLIT.border.soft }}>
            <p className="mb-1 text-[13px] font-semibold text-[#162744] dark:text-white">
              Tracking de auditoría y proceso (tiempo real)
            </p>
            <p className="mb-2 text-[11px]" style={{ color: FLIT.text.secondary }}>
              Historial cronológico de eventos, cifrado y respuestas del proveedor.
            </p>
            <IdentityValidationTrackingPanel
              validationId={v.id}
              refreshKey={trackingTick}
              defaultOpen
              embebido
              detailLayout
            />
          </div>
        </div>
      )}
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
      <p className="text-[10px] font-semibold uppercase tracking-wide opacity-55">{label}</p>
      {badgeTone ? (
        <div className="mt-1">
          <StatusBadge label={value} tone={badgeTone} ariaLabel={`${label}: ${value}`} />
        </div>
      ) : (
        <p
          className={`mt-0.5 truncate text-[12px] font-semibold ${accent ? 'font-mono' : ''}`}
          style={{ color: accent ? FLIT.brand.blue : FLIT.text.primary }}
        >
          {value}
        </p>
      )}
    </div>
  );
}
