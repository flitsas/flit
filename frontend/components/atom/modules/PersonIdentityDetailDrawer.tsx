'use client';

import { useCallback, useEffect, useId, useRef, useState } from 'react';
import {
  AlertCircle,
  Calendar,
  ChevronDown,
  FileText,
  Hash,
  Mail,
  RefreshCw,
  ScanFace,
  ShieldCheck,
  User,
  X,
} from 'lucide-react';
import { tramitesClient } from '@/lib/api/tramites-client';
import { StatusBadge, type StatusTone } from '@/components/atom/StatusBadge';
import { IdentityValidationTrackingPanel } from '@/components/atom/IdentityValidationTrackingPanel';
import {
  AssociatedProceduresList,
  buildAssociatedProcedures,
} from '@/components/atom/modules/AssociatedProceduresList';
import { IdentityCaptureLinkBlock } from '@/components/atom/modules/IdentityCaptureLinkBlock';
import { IdentityInfoTile } from '@/components/atom/modules/IdentityInfoTile';
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
  expirado: { label: 'Expirado', tone: 'neutral' },
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
      className="fixed inset-0 z-50 flex justify-end"
      role="dialog"
      aria-modal="true"
      aria-labelledby="person-identity-process-title"
    >
      <button
        type="button"
        className="absolute inset-0 bg-slate-900/40 backdrop-blur-sm"
        aria-label="Cerrar panel"
        onClick={onClose}
      />
      <aside className="relative z-10 flex h-full w-full max-w-xl flex-col border-l bg-white shadow-2xl dark:bg-[#0B0F14]">
        <header className="flex shrink-0 items-center justify-between border-b px-5 py-4">
          <div className="flex min-w-0 items-center gap-2">
            <span
              className="grid h-8 w-8 shrink-0 place-items-center rounded-xl"
              style={{ background: FLIT.blueAlpha(0.12), color: FLIT.brand.blue }}
              aria-hidden
            >
              <ScanFace className="h-4 w-4" />
            </span>
            <div className="min-w-0">
              <h2
                id="person-identity-process-title"
                className="truncate text-sm font-bold text-[#162744] dark:text-white"
              >
                Proceso de validación
              </h2>
              {data && (
                <p className="text-[10px] opacity-60">
                  {data.total} validación{data.total === 1 ? '' : 'es'}
                  {data.allTerminal ? ' · historial terminal' : ' · actualizando…'}
                </p>
              )}
            </div>
          </div>
          <div className="flex items-center gap-1 shrink-0">
            <button
              type="button"
              onClick={() => void load()}
              className="grid h-8 w-8 place-items-center rounded-lg border hover:bg-[rgba(79,116,201,0.1)] focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-[#4F74C9]"
              aria-label="Actualizar historial"
            >
              <RefreshCw className={`h-4 w-4 opacity-70 ${loading ? 'animate-spin' : ''}`} aria-hidden="true" />
            </button>
            <button
              type="button"
              onClick={onClose}
              aria-label="Cerrar proceso"
              className="grid h-8 w-8 place-items-center rounded-lg border hover:bg-[rgba(79,116,201,0.1)] focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-[#4F74C9]"
            >
              <X className="h-4 w-4 opacity-70" aria-hidden="true" />
            </button>
          </div>
        </header>

        <div className="min-h-0 flex-1 space-y-4 overflow-y-auto px-5 py-4">
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
              {/* Cabecera personal — mismo diseño que develop (PrevalidacionDetailDrawer) */}
              <div className="flex flex-wrap items-center justify-between gap-2">
                {awaiting ? (
                  <div className="flex items-center gap-2">
                    <RefreshCw className="h-3.5 w-3.5 animate-spin" style={{ color: FLIT.brand.blue }} aria-hidden />
                    <p className="text-xs font-semibold" style={{ color: FLIT.brand.blue }}>
                      Esperando validación de {personName}
                    </p>
                  </div>
                ) : (
                  <p className="text-sm font-semibold text-[#162744] dark:text-white">{personName}</p>
                )}
                <StatusBadge
                  label={latestMeta.label}
                  tone={latestMeta.tone}
                  ariaLabel={`Estado de la última validación: ${latestMeta.label}`}
                />
              </div>

              <div className="grid gap-2 sm:grid-cols-2">
                <IdentityInfoTile icon={User} label="Persona" value={personName} />
                <IdentityInfoTile
                  icon={FileText}
                  label="Documento"
                  value={`${latest.documentType} ${latest.documentNumber}`}
                  mono
                />
                <IdentityInfoTile
                  icon={Mail}
                  label="Correo"
                  value={latest.email || '—'}
                  className="sm:col-span-2"
                />
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
      </aside>
    </div>
  );
}

function ValidationAccordionItem({
  validation: v,
  index,
  total,
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
  const meta = ESTADO_META[v.status] ?? ESTADO_META.enviado;
  const showCaptura = Boolean(v.captureUrl && !isTerminal(v));
  const title =
    index === 0
      ? 'Más reciente'
      : index === total - 1
        ? 'Más antigua'
        : `Validación #${index + 1}`;
  const subtitle = v.referenceNumber
    ? v.referenceNumber
    : v.procedureInstanceId
      ? 'Con trámite'
      : 'Prevalidación';

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
        className="flex w-full items-center gap-3 px-3 py-2.5 text-left hover:bg-[rgba(79,116,201,0.05)] focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-[-2px] focus-visible:outline-[#4F74C9]"
        aria-expanded={open}
        aria-controls={panelId}
        onClick={() => setOpen((o) => !o)}
      >
        <ChevronDown
          className={`h-4 w-4 shrink-0 transition-transform ${open ? 'rotate-0' : '-rotate-90'}`}
          style={{ color: FLIT.brand.blue }}
          aria-hidden
        />
        <div className="min-w-0 flex-1">
          <div className="flex flex-wrap items-center gap-2">
            <span className="text-[11px] font-semibold text-[#162744] dark:text-white">{title}</span>
            <StatusBadge label={meta.label} tone={meta.tone} ariaLabel={`Estado: ${meta.label}`} />
          </div>
          <p className="mt-0.5 truncate text-[10px] opacity-60">
            <span className="font-mono font-semibold" style={{ color: FLIT.brand.blue }}>
              {subtitle}
            </span>
            {' · Registro '}
            {formatFecha(v.createdAt)}
          </p>
        </div>
      </button>

      {open && (
        <div id={panelId} className="space-y-3 border-t px-3 py-3" style={{ borderColor: FLIT.border.soft }}>
          <div className="grid gap-2 sm:grid-cols-2">
            <IdentityInfoTile
              icon={Hash}
              label="Intentos Kyverum"
              value={`${v.intentos} / ${v.maxIntentos}`}
            />
            <IdentityInfoTile
              icon={Calendar}
              label="Enlace vigente hasta"
              value={v.expired ? 'Vencido' : formatFecha(v.expiresAt)}
            />
            <IdentityInfoTile
              icon={ShieldCheck}
              label="Score"
              value={v.score != null ? String(v.score) : '—'}
            />
            <IdentityInfoTile icon={Calendar} label="Aprobación" value={formatFecha(v.validatedAt)} />
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

          <div className="rounded-xl border p-3">
            <p className="mb-1 text-[11px] font-semibold text-[#162744] dark:text-white">
              Tracking del proceso
            </p>
            <p className="mb-2 text-[10px] opacity-60">
              Etapas, reintentos y fallos registrados por el sistema (se actualiza en vivo).
            </p>
            <IdentityValidationTrackingPanel
              validationId={v.id}
              refreshKey={trackingTick}
              defaultOpen
            />
          </div>
        </div>
      )}
    </div>
  );
}
