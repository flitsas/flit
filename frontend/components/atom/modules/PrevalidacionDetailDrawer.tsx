'use client';

import { useCallback, useEffect, useRef, useState } from 'react';
import {
  AlertCircle,
  Calendar,
  Copy,
  ExternalLink,
  FileText,
  Hash,
  Mail,
  RefreshCw,
  ScanFace,
  ShieldCheck,
  User,
  X,
} from 'lucide-react';
import { QRCodeSVG } from 'qrcode.react';
import { tramitesClient } from '@/lib/api/tramites-client';
import { StatusBadge, type StatusTone } from '@/components/atom/StatusBadge';
import { IdentityValidationTrackingPanel } from '@/components/atom/IdentityValidationTrackingPanel';
import {
  AssociatedProceduresList,
  buildAssociatedProcedures,
} from '@/components/atom/modules/AssociatedProceduresList';
import type { BiometricEstado, BiometricValidation } from '@/lib/api/types/procedure-runtime';

/**
 * Panel lateral derecho de proceso de identidad (CF-06/CF-07), mismo patrón que Reportes /
 * OtSidePanel. Se abre desde "Proceso" en la fila (tabla compacta); no embebe tracking bajo cada registro.
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

export interface PrevalidacionDetailDrawerProps {
  validationId: string;
  onClose: () => void;
  onStatusChanged?: () => void;
  /** Título del panel (prevalidación vs validación del módulo transversal). */
  title?: string;
}

export function PrevalidacionDetailDrawer({
  validationId,
  onClose,
  onStatusChanged,
  title = 'Proceso de validación',
}: PrevalidacionDetailDrawerProps) {
  const [detail, setDetail] = useState<BiometricValidation | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(false);
  const failedRef = useRef(false);
  const lastStatusRef = useRef<string | null>(null);
  const [trackingTick, setTrackingTick] = useState(0);

  const load = useCallback(async () => {
    setLoading(true);
    try {
      const res = await tramitesClient.getPrevalidacionDetail(validationId);
      setDetail(res);
      setError(null);
      failedRef.current = false;
      setTrackingTick((t) => t + 1);
      if (lastStatusRef.current && lastStatusRef.current !== res.status) {
        onStatusChanged?.();
      }
      lastStatusRef.current = res.status;
    } catch (err) {
      setError(err instanceof Error ? err.message : 'No se pudo cargar el proceso de la validación.');
      setDetail(null);
      failedRef.current = true;
    } finally {
      setLoading(false);
    }
  }, [validationId, onStatusChanged]);

  useEffect(() => {
    // eslint-disable-next-line react-hooks/set-state-in-effect -- reset al cambiar validationId + fetch async
    setDetail(null);
    setError(null);
    failedRef.current = false;
    lastStatusRef.current = null;
    void load();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [validationId]);

  useEffect(() => {
    if (!detail || isTerminal(detail) || failedRef.current) return;
    const tick = () => {
      if (document.hidden) return;
      void load();
    };
    const timer = setInterval(tick, POLL_MS);
    return () => clearInterval(timer);
  }, [detail, load]);

  useEffect(() => {
    function onKey(e: KeyboardEvent) {
      if (e.key === 'Escape') onClose();
    }
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  }, [onClose]);

  const meta = detail ? (ESTADO_META[detail.status] ?? ESTADO_META.enviado) : null;
  const showCaptura = Boolean(detail?.captureUrl && !isTerminal(detail));
  const awaiting =
    detail != null &&
    !isTerminal(detail) &&
    (detail.status === 'en_proceso' || detail.status === 'enviado' || detail.status === 'pendiente_envio');

  return (
    <div
      className="fixed inset-0 z-50 flex justify-end"
      role="dialog"
      aria-modal="true"
      aria-labelledby="identity-process-title"
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
              style={{ background: 'rgba(85,126,255,0.12)', color: '#557EFF' }}
              aria-hidden
            >
              <ScanFace className="h-4 w-4" />
            </span>
            <h2
              id="identity-process-title"
              className="truncate text-sm font-bold text-[#162744] dark:text-white"
            >
              {title}
            </h2>
          </div>
          <button
            type="button"
            onClick={onClose}
            aria-label="Cerrar proceso"
            className="grid h-8 w-8 place-items-center rounded-lg border hover:bg-[#557EFF1A] focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-[#557EFF]"
          >
            <X className="h-4 w-4 opacity-70" aria-hidden="true" />
          </button>
        </header>

        <div className="min-h-0 flex-1 space-y-4 overflow-y-auto px-5 py-4">
          {loading && !detail && !error && (
            <div role="status" aria-live="polite" aria-busy="true" className="space-y-3">
              <span className="sr-only">Cargando proceso…</span>
              <div className="h-4 w-2/3 animate-pulse rounded bg-black/10 dark:bg-white/10" />
              <div className="h-24 w-full animate-pulse rounded-xl bg-black/5 dark:bg-white/5" />
            </div>
          )}

          {error && !detail && (
            <div
              className="flex items-start gap-2 rounded-xl border p-3 text-xs"
              style={{ borderColor: '#FF4E00', background: 'rgba(255,78,0,0.06)', color: '#FF4E00' }}
              role="alert"
            >
              <AlertCircle className="mt-0.5 h-4 w-4 shrink-0" aria-hidden="true" />
              <div>
                <p className="font-semibold">No se pudo cargar el proceso.</p>
                <p className="opacity-80">{error}</p>
              </div>
            </div>
          )}

          {detail && meta && (
            <div className="space-y-4 text-xs">
              <div className="flex flex-wrap items-center justify-between gap-2">
                {awaiting ? (
                  <div className="flex items-center gap-2">
                    <RefreshCw className="h-3.5 w-3.5 animate-spin" style={{ color: '#557EFF' }} aria-hidden />
                    <p className="text-xs font-semibold" style={{ color: '#557EFF' }}>
                      Esperando validación de {detail.name}
                    </p>
                  </div>
                ) : (
                  <p className="text-sm font-semibold text-[#162744] dark:text-white">{detail.name}</p>
                )}
                <StatusBadge label={meta.label} tone={meta.tone} ariaLabel={`Estado: ${meta.label}`} />
              </div>

              {/* Campos con iconos — lectura rápida, sin tabla densa */}
              <div className="grid gap-2 sm:grid-cols-2">
                <InfoTile icon={User} label="Persona" value={detail.name} />
                <InfoTile
                  icon={FileText}
                  label="Documento"
                  value={`${detail.documentType} ${detail.documentNumber}`}
                  mono
                />
                <InfoTile icon={Mail} label="Correo" value={detail.email || '—'} />
                <InfoTile
                  icon={Hash}
                  label="Intentos Kyverum"
                  value={`${detail.intentos} / ${detail.maxIntentos}`}
                />
                <InfoTile icon={Calendar} label="Enlace vigente hasta" value={detail.expired ? 'Vencido' : formatFecha(detail.expiresAt)} />
                <InfoTile
                  icon={ShieldCheck}
                  label="Score"
                  value={detail.score != null ? String(detail.score) : '—'}
                />
              </div>

              {/* HU #11069 — trámites asociados a la VID (enlace, id, tipo). */}
              {(() => {
                const associated = buildAssociatedProcedures({
                  instanceId: detail.procedureInstanceId,
                  referenceNumber: detail.referenceNumber,
                  modalidad: detail.modalidad,
                  linkedProcedures: detail.linkedProcedures,
                });
                return associated.length > 0 ? (
                  <AssociatedProceduresList procedures={associated} collapsible />
                ) : null;
              })()}

              {detail.status === 'rechazado' && detail.rejectionReason && (
                <p
                  className="rounded-xl px-3 py-2 text-[11px]"
                  style={{ background: 'rgba(255,78,0,0.06)', color: '#FF4E00' }}
                >
                  Motivo del rechazo: {detail.rejectionReason}
                </p>
              )}

              {detail.ultimoIntentoMotivo && !isTerminal(detail) && (
                <div
                  className="rounded-xl p-3 text-[11px]"
                  style={{
                    background: 'rgba(178,106,0,0.08)',
                    border: '1px solid rgba(178,106,0,0.3)',
                    color: '#B26A00',
                  }}
                  role="status"
                  aria-live="polite"
                >
                  <span className="font-semibold">
                    Intento {detail.intentos} de {detail.maxIntentos} no pasó.
                  </span>{' '}
                  {detail.ultimoIntentoMotivo}{' '}
                  {detail.maxIntentos - detail.intentos > 0
                    ? `Quedan ${detail.maxIntentos - detail.intentos} intento(s) en el móvil.`
                    : 'No quedan intentos: el estado pasará a Rechazado al confirmarse.'}
                </div>
              )}

              {showCaptura && (
                <div className="space-y-2 rounded-xl border p-3">
                  <p className="text-[11px] font-semibold" style={{ color: '#557EFF' }}>
                    Enlace de captura
                  </p>
                  <div className="flex flex-col gap-3 sm:flex-row sm:items-center">
                    <div className="rounded-xl border bg-white p-2">
                      <QRCodeSVG value={detail.captureUrl!} size={112} aria-label="Código QR del enlace de captura" />
                    </div>
                    <div className="min-w-0 flex-1 space-y-1.5">
                      <a
                        href={detail.captureUrl!}
                        target="_blank"
                        rel="noopener noreferrer"
                        className="flex w-fit items-center gap-1.5 rounded-xl px-3 py-1.5 text-[11px] font-semibold text-white"
                        style={{ background: '#557EFF' }}
                      >
                        <ExternalLink className="h-3 w-3" aria-hidden />
                        Abrir captura Kyverum
                      </a>
                      <CopyLinkButton captureUrl={detail.captureUrl!} />
                    </div>
                  </div>
                </div>
              )}

              <div className="rounded-xl border p-3">
                <p className="mb-1 text-[11px] font-semibold text-[#162744] dark:text-white">
                  Tracking del proceso
                </p>
                <p className="mb-2 text-[10px] opacity-60">
                  Etapas, reintentos y fallos registrados por el sistema (se actualiza en vivo).
                </p>
                <IdentityValidationTrackingPanel
                  validationId={detail.id}
                  refreshKey={trackingTick}
                  defaultOpen
                />
              </div>
            </div>
          )}
        </div>
      </aside>
    </div>
  );
}

function InfoTile({
  icon: Icon,
  label,
  value,
  mono,
}: {
  icon: typeof User;
  label: string;
  value: string;
  mono?: boolean;
}) {
  return (
    <div className="flex items-start gap-2.5 rounded-xl border px-3 py-2.5">
      <span
        className="mt-0.5 grid h-7 w-7 shrink-0 place-items-center rounded-lg"
        style={{ background: '#DFE5ED', color: '#162744' }}
        aria-hidden
      >
        <Icon className="h-3.5 w-3.5" />
      </span>
      <div className="min-w-0">
        <p className="text-[10px] font-semibold uppercase tracking-wide opacity-55">{label}</p>
        <p className={`mt-0.5 truncate text-[12px] font-medium text-[#162744] dark:text-white ${mono ? 'font-mono' : ''}`}>
          {value}
        </p>
      </div>
    </div>
  );
}

function CopyLinkButton({ captureUrl }: { captureUrl: string }) {
  const [copied, setCopied] = useState(false);
  const copy = async () => {
    try {
      await navigator.clipboard?.writeText(captureUrl);
      setCopied(true);
      setTimeout(() => setCopied(false), 2000);
    } catch {
      /* clipboard no disponible */
    }
  };
  return (
    <>
      <button
        type="button"
        onClick={() => void copy()}
        className="flex items-center gap-1.5 rounded-xl border px-3 py-1.5 text-[11px] font-semibold focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2"
        style={{ borderColor: '#557EFF', color: '#557EFF' }}
        aria-label="Copiar enlace de captura"
      >
        <Copy className="h-3 w-3" aria-hidden />
        {copied ? 'Copiado' : 'Copiar enlace'}
      </button>
      <span className="sr-only" role="status" aria-live="polite">
        {copied ? 'Enlace copiado al portapapeles.' : ''}
      </span>
    </>
  );
}
