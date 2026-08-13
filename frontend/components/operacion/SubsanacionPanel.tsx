'use client';

import { useMemo, useState } from 'react';
import { AlertTriangle, Loader2, RotateCcw, X } from 'lucide-react';
import { tramitesClient } from '@/lib/api/tramites-client';
import type { StatusHistory } from '@/lib/api/types/procedure-runtime';
import {
  latestRejectionReason,
  latestSubsanacionEntry,
  parseSubsanacionObservation,
} from '@/lib/tramites/subsanacion';

interface SubsanacionPanelProps {
  instanceId: string | null;
  statusHistory: StatusHistory[];
  /** Carga del detalle de la instancia (fuente de `statusHistory`), a cargo del padre. */
  loading: boolean;
  /** Error al cargar el detalle de la instancia, a cargo del padre. */
  error: string | null;
  /** Re-radicado con éxito (AC2): el padre decide navegación/toast. */
  onReradicado: () => void;
  /**
   * Hay cambios locales sin guardar: Re-radicar permanece deshabilitado.
   */
  hasUnsavedChanges?: boolean;
  /**
   * Tras editar y Guardar y continuar al menos una vez (y sin dirty pendiente).
   * Sin esto Re-radicar no se habilita (hay que modificar algo primero).
   */
  canReradicar?: boolean;
  /**
   * Cancela el sub-estado de subsanación (flag) cuando no hace falta corregir.
   * Solo aplica a rechazado + subsanacionActiva.
   */
  onCancelSubsanacion?: () => Promise<void> | void;
  /** Si false, no se muestra Cancelar (p. ej. estado legado `subsanacion`). */
  showCancel?: boolean;
}

const FALLBACK_MOTIVO =
  'El organismo de tránsito devolvió el trámite para corrección. Revisa los datos y documentos antes de re-radicar.';

/**
 * HU #10874 — panel de subsanación: motivo + checklist + Re-radicar / Cancelar.
 * Feature #11066 — Re-radicar solo tras edición guardada; Cancelar sale del flag.
 */
export function SubsanacionPanel({
  instanceId,
  statusHistory,
  loading,
  error,
  onReradicado,
  hasUnsavedChanges = false,
  canReradicar = false,
  onCancelSubsanacion,
  showCancel = false,
}: SubsanacionPanelProps) {
  const entry = useMemo(() => latestSubsanacionEntry(statusHistory), [statusHistory]);
  const observation = useMemo(() => parseSubsanacionObservation(entry?.metadata), [entry]);
  const rejectionReason = useMemo(() => latestRejectionReason(statusHistory), [statusHistory]);
  const items = useMemo(() => observation?.items ?? [], [observation]);
  const hasChecklist = items.length > 0;
  const legacySubsanacionReason =
    entry?.toStatus === 'subsanacion' && entry.fromStatus !== 'rechazado'
      ? entry.reason?.trim()
      : null;
  const motivo =
    (hasChecklist ? observation?.motivo?.trim() : null) ||
    rejectionReason ||
    legacySubsanacionReason ||
    FALLBACK_MOTIVO;

  const [resolved, setResolved] = useState<ReadonlySet<number>>(() => new Set());
  const [submitting, setSubmitting] = useState(false);
  const [cancelling, setCancelling] = useState(false);
  const [submitError, setSubmitError] = useState<string | null>(null);

  const allResolved = !hasChecklist || items.every((_, i) => resolved.has(i));
  const reradicarEnabled =
    !!instanceId && allResolved && canReradicar && !hasUnsavedChanges && !submitting && !cancelling;

  const toggleItem = (index: number) => {
    setResolved((prev) => {
      const next = new Set(prev);
      if (next.has(index)) next.delete(index);
      else next.add(index);
      return next;
    });
  };

  const handleReradicar = async () => {
    if (!instanceId || submitting || !reradicarEnabled) return;
    setSubmitting(true);
    setSubmitError(null);
    try {
      await tramitesClient.submitInstance(instanceId);
      onReradicado();
    } catch (err) {
      setSubmitError(err instanceof Error ? err.message : 'No se pudo re-radicar el trámite.');
      setSubmitting(false);
    }
  };

  const handleCancel = async () => {
    if (!onCancelSubsanacion || cancelling || submitting) return;
    setCancelling(true);
    setSubmitError(null);
    try {
      await onCancelSubsanacion();
    } catch (err) {
      setSubmitError(
        err instanceof Error ? err.message : 'No se pudo cancelar la subsanación.',
      );
      setCancelling(false);
    }
  };

  if (loading) {
    return (
      <div
        className="rounded-xl p-3 text-xs border shrink-0 flex items-center gap-2"
        style={{ borderColor: '#F9AC00', background: 'rgba(249,172,0,0.06)' }}
        role="status"
        aria-live="polite"
        aria-busy="true"
      >
        <Loader2 className="h-4 w-4 shrink-0 animate-spin" style={{ color: '#F9AC00' }} aria-hidden="true" />
        <span>Cargando el detalle de la subsanación…</span>
      </div>
    );
  }

  if (error) {
    return (
      <div
        className="rounded-xl p-3 text-xs border shrink-0 flex items-start gap-2"
        style={{ borderColor: '#FF4E00', background: 'rgba(255,78,0,0.06)', color: '#FF4E00' }}
        role="alert"
        aria-live="polite"
      >
        <AlertTriangle className="h-4 w-4 shrink-0 mt-0.5" aria-hidden="true" />
        <span>No se pudo cargar el detalle de la subsanación: {error}</span>
      </div>
    );
  }

  return (
    <section
      className="rounded-2xl p-4 border shrink-0 flex flex-col gap-3"
      style={{ borderColor: '#F9AC00', background: 'rgba(249,172,0,0.06)' }}
      aria-labelledby="subsanacion-heading"
    >
      <div className="flex items-start gap-2">
        <AlertTriangle className="h-4 w-4 shrink-0 mt-0.5" style={{ color: '#F9AC00' }} aria-hidden="true" />
        <div className="min-w-0">
          <h3 id="subsanacion-heading" className="text-sm font-bold" style={{ color: '#B45309' }}>
            Trámite en subsanación
          </h3>
          <p className="mt-1 text-xs" style={{ color: '#162744' }}>
            {rejectionReason ? (
              <>
                El Organismo de Tránsito devolvió el trámite para corrección.{' '}
                <span className="font-semibold">Motivo del rechazo:</span> {rejectionReason}
              </>
            ) : (
              motivo
            )}
          </p>
        </div>
      </div>

      <div>
        <p className="text-xs font-semibold uppercase opacity-60 mb-2">
          Checklist de ítems a subsanar
        </p>
        {hasChecklist ? (
          <ul className="space-y-2" aria-label="Checklist de ítems a subsanar">
            {items.map((item, i) => {
              const checked = resolved.has(i);
              const inputId = `subsanacion-item-${i}`;
              return (
                <li
                  key={i}
                  className="flex items-start gap-2 rounded-lg border bg-white dark:bg-[#162744] p-2"
                >
                  <input
                    id={inputId}
                    type="checkbox"
                    checked={checked}
                    onChange={() => toggleItem(i)}
                    className="mt-0.5 h-4 w-4 shrink-0"
                    aria-describedby={item.detalle ? `${inputId}-detalle` : undefined}
                  />
                  <label htmlFor={inputId} className="text-xs flex-1 cursor-pointer">
                    {item.campo && <span className="font-semibold">{item.campo}: </span>}
                    <span id={`${inputId}-detalle`} className="opacity-80">
                      {item.detalle ?? 'Sin detalle adicional.'}
                    </span>
                  </label>
                </li>
              );
            })}
          </ul>
        ) : (
          <p className="text-xs opacity-70">
            El organismo de tránsito no registró un checklist detallado; corrige los datos según el
            motivo indicado arriba, usa Guardar y continuar y luego Re-radicar. Si no hace falta
            corregir, cancela la subsanación.
          </p>
        )}
      </div>

      {submitError && (
        <p role="alert" className="text-xs" style={{ color: '#FF4E00' }}>
          {submitError}
        </p>
      )}

      <div className="flex flex-wrap items-center gap-2">
        <button
          type="button"
          onClick={() => void handleReradicar()}
          disabled={!reradicarEnabled}
          className="inline-flex items-center gap-1.5 px-5 py-2 rounded-xl text-xs font-semibold text-white disabled:opacity-50"
          style={{ background: 'linear-gradient(135deg,#557EFF,#00DBD5)' }}
          title={
            hasUnsavedChanges
              ? 'Hay cambios sin guardar: usa Guardar y continuar antes de re-radicar'
              : !canReradicar
                ? 'Edita y guarda al menos un cambio con Guardar y continuar para habilitar Re-radicar'
                : hasChecklist && !allResolved
                  ? 'Marca todos los ítems del checklist como corregidos para re-radicar'
                  : 'Re-radica el trámite al organismo de tránsito'
          }
        >
          {submitting ? (
            <>
              <Loader2 className="h-3.5 w-3.5 animate-spin" aria-hidden="true" /> Re-radicando…
            </>
          ) : (
            <>
              <RotateCcw className="h-3.5 w-3.5" aria-hidden="true" /> Re-radicar
            </>
          )}
        </button>

        {showCancel && onCancelSubsanacion && (
          <button
            type="button"
            onClick={() => void handleCancel()}
            disabled={submitting || cancelling}
            className="inline-flex items-center gap-1.5 px-4 py-2 rounded-xl text-xs font-semibold border disabled:opacity-50"
            style={{ borderColor: '#162744', color: '#162744' }}
            title="Sale de la subsanación sin re-radicar (el trámite sigue rechazado)"
          >
            {cancelling ? (
              <>
                <Loader2 className="h-3.5 w-3.5 animate-spin" aria-hidden="true" /> Cancelando…
              </>
            ) : (
              <>
                <X className="h-3.5 w-3.5" aria-hidden="true" /> Cancelar
              </>
            )}
          </button>
        )}
      </div>

      {hasUnsavedChanges ? (
        <p className="text-xs opacity-70">
          Hay cambios sin guardar. Usa Guardar y continuar; después podrás re-radicar.
        </p>
      ) : !canReradicar ? (
        <p className="text-xs opacity-70">
          Edita lo necesario y pulsa Guardar y continuar para habilitar Re-radicar. Si no hace falta
          corregir, usa Cancelar.
        </p>
      ) : (
        <p className="text-xs opacity-70" role="status">
          Cambios guardados. Ya puedes re-radicar.
        </p>
      )}
    </section>
  );
}
