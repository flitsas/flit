'use client';

import { useCallback, useEffect, useRef, useState } from 'react';
import Link from 'next/link';
import {
  AlertCircle,
  ExternalLink,
  RefreshCw,
  ScanFace,
  X,
} from 'lucide-react';
import { tramitesClient } from '@/lib/api/tramites-client';
import { StatusBadge, type StatusTone } from '@/components/atom/StatusBadge';
import { IdentityValidationTrackingPanel } from '@/components/atom/IdentityValidationTrackingPanel';
import type {
  BiometricEstado,
  BiometricValidation,
  PersonBiometricValidationsResponse,
} from '@/lib/api/types/procedure-runtime';

/**
 * Detalle multi-validación por persona (HU #11273 / CF-06 / ADR-0040): una petición carga el
 * historial (tope 50); polling cada 5 s hasta que `allTerminal` sea true.
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

  return (
    <div
      className="fixed inset-0 z-50 flex justify-end"
      role="dialog"
      aria-modal="true"
      aria-label="Historial de validaciones de la persona"
    >
      <button
        type="button"
        className="absolute inset-0 bg-black/40"
        aria-label="Cerrar"
        onClick={onClose}
      />
      <aside
        className="relative z-10 flex h-full w-full max-w-lg flex-col bg-white shadow-xl dark:bg-[#0B0F14]"
        style={{ color: '#162744' }}
      >
        <header className="flex items-start justify-between gap-3 border-b px-4 py-3 shrink-0">
          <div className="min-w-0">
            <p className="text-[10px] font-semibold uppercase opacity-50">Identidad por persona</p>
            <h2 className="text-sm font-semibold truncate">
              {data?.name ?? 'Persona'} · {documentType} {documentNumber}
            </h2>
            {data && (
              <p className="text-[11px] opacity-60 mt-0.5">
                {data.total} validación{data.total === 1 ? '' : 'es'}
                {data.allTerminal ? ' · historial terminal' : ' · actualizando…'}
              </p>
            )}
          </div>
          <div className="flex items-center gap-1 shrink-0">
            <button
              type="button"
              onClick={() => void load()}
              className="rounded-lg p-2 hover:bg-black/5 focus-visible:outline focus-visible:outline-2"
              aria-label="Actualizar historial"
            >
              <RefreshCw className={`h-4 w-4 ${loading ? 'animate-spin' : ''}`} aria-hidden="true" />
            </button>
            <button
              type="button"
              onClick={onClose}
              className="rounded-lg p-2 hover:bg-black/5 focus-visible:outline focus-visible:outline-2"
              aria-label="Cerrar panel"
            >
              <X className="h-4 w-4" aria-hidden="true" />
            </button>
          </div>
        </header>

        <div className="flex-1 overflow-y-auto px-4 py-3 space-y-3">
          {loading && !data && (
            <div className="space-y-2" aria-busy="true">
              <span className="sr-only">Cargando historial…</span>
              {[0, 1, 2].map((i) => (
                <div key={i} className="h-24 animate-pulse rounded-xl bg-black/5" aria-hidden="true" />
              ))}
            </div>
          )}

          {error && (
            <div
              className="rounded-xl border p-3 text-xs flex gap-2"
              style={{ borderColor: '#FF4E00', color: '#FF4E00' }}
              role="alert"
            >
              <AlertCircle className="h-4 w-4 shrink-0" aria-hidden="true" />
              <div>
                <p className="font-semibold">No se pudo cargar el historial.</p>
                <p className="opacity-80 mt-1">{error}</p>
              </div>
            </div>
          )}

          {!loading && data && data.validations.length === 0 && (
            <div className="text-center py-10 text-xs opacity-70">
              <ScanFace className="mx-auto h-8 w-8 opacity-30" aria-hidden="true" />
              <p className="mt-2">Sin validaciones para este documento.</p>
            </div>
          )}

          {data?.validations.map((v, idx) => (
            <ValidationSegment key={v.id} validation={v} index={idx} />
          ))}
        </div>
      </aside>
    </div>
  );
}

function ValidationSegment({
  validation: v,
  index,
}: {
  validation: BiometricValidation;
  index: number;
}) {
  const meta = ESTADO_META[v.status] ?? ESTADO_META.enviado;
  const showTracking =
    v.status === 'enviado' ||
    v.status === 'en_proceso' ||
    v.status === 'pendiente_envio' ||
    v.status === 'error_envio';

  return (
    <section
      className="rounded-xl border p-3 space-y-2"
      aria-label={`Validación ${index + 1}: ${meta.label}`}
    >
      <div className="flex items-start justify-between gap-2">
        <div className="min-w-0">
          <p className="text-[10px] font-semibold uppercase opacity-50">
            {index === 0 ? 'Más reciente' : `Intento #${index + 1}`}
          </p>
          <div className="mt-1 flex flex-wrap items-center gap-2">
            <StatusBadge label={meta.label} tone={meta.tone} ariaLabel={`Estado: ${meta.label}`} />
            {v.referenceNumber ? (
              <span className="font-mono text-[11px] font-semibold" style={{ color: '#557EFF' }}>
                {v.referenceNumber}
              </span>
            ) : (
              <span className="text-[10px] font-semibold opacity-50">Prevalidación</span>
            )}
          </div>
        </div>
        {v.procedureInstanceId && (
          <Link
            href={`/tramites/${v.procedureInstanceId}`}
            className="shrink-0 rounded-lg p-1.5 hover:bg-black/5"
            aria-label={`Abrir trámite ${v.referenceNumber ?? ''}`}
          >
            <ExternalLink className="h-3.5 w-3.5" style={{ color: '#557EFF' }} aria-hidden="true" />
          </Link>
        )}
      </div>
      <dl className="grid grid-cols-2 gap-x-3 gap-y-1 text-[11px]">
        <div>
          <dt className="opacity-50">Proveedor</dt>
          <dd className="capitalize">{v.provider}</dd>
        </div>
        <div>
          <dt className="opacity-50">Aprobación</dt>
          <dd>{formatFecha(v.validatedAt)}</dd>
        </div>
        <div>
          <dt className="opacity-50">Enlace vence</dt>
          <dd>{formatFecha(v.expiresAt)}</dd>
        </div>
        <div>
          <dt className="opacity-50">Intentos</dt>
          <dd>
            {v.intentos}/{v.maxIntentos}
          </dd>
        </div>
      </dl>
      {v.rejectionReason && (
        <p className="text-[11px]" style={{ color: '#FF4E00' }}>
          {v.rejectionReason}
        </p>
      )}
      {showTracking && <IdentityValidationTrackingPanel validationId={v.id} />}
    </section>
  );
}
