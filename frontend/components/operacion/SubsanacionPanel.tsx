'use client';

import { useMemo, useState } from 'react';
import { AlertTriangle, Loader2, RotateCcw } from 'lucide-react';
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
}

const FALLBACK_MOTIVO =
  'El organismo de tránsito devolvió el trámite para corrección. Revisa los datos y documentos antes de re-radicar.';

/**
 * HU #10874 — panel de subsanación: muestra el motivo y el checklist de ítems a subsanar
 * (AC1) y ofrece "Re-radicar" (AC2) cuando el operador terminó de corregir. Vive dentro del
 * wizard (TramiteWizard) mientras el trámite está en estado `subsanacion`: en ese estado los
 * campos siguen siendo editables (igual que en `borrador`, HU #10870), así que este panel es un
 * complemento informativo/de acción, no un modo de solo lectura.
 *
 * 4 estados de UI: cargando (fetch del detalle en curso, a cargo del padre) · error (fetch
 * fallido) · vacío (sin checklist estructurado — degrada al motivo plano) · lleno (checklist con
 * ítems). Ver GAP documentado en `lib/tramites/subsanacion.ts`: el backend aún no expone
 * `metadata` en `GET /instances/{id}`, así que "vacío" es hoy el caso esperado en producción.
 */
export function SubsanacionPanel({
  instanceId,
  statusHistory,
  loading,
  error,
  onReradicado,
}: SubsanacionPanelProps) {
  const entry = useMemo(() => latestSubsanacionEntry(statusHistory), [statusHistory]);
  const observation = useMemo(() => parseSubsanacionObservation(entry?.metadata), [entry]);
  // HU #10870 — cuando la subsanación la inicia el operador desde `rechazado`, la guía de QUÉ
  // corregir es el motivo del rechazo del Organismo de Tránsito (transición entregado→rechazado),
  // no una observación estructurada (que ya no existe en este flujo).
  const rejectionReason = useMemo(() => latestRejectionReason(statusHistory), [statusHistory]);
  const motivo =
    observation?.motivo?.trim() || rejectionReason || entry?.reason?.trim() || FALLBACK_MOTIVO;
  const items = useMemo(() => observation?.items ?? [], [observation]);
  const hasChecklist = items.length > 0;

  const [resolved, setResolved] = useState<ReadonlySet<number>>(() => new Set());
  const [submitting, setSubmitting] = useState(false);
  const [submitError, setSubmitError] = useState<string | null>(null);

  // AC2 — "todas las observaciones resueltas": si no hay checklist estructurado no hay nada que
  // marcar, así que no se bloquea el Re-radicar (el operador ya corrigió según el motivo).
  const allResolved = !hasChecklist || items.every((_, i) => resolved.has(i));

  const toggleItem = (index: number) => {
    setResolved((prev) => {
      const next = new Set(prev);
      if (next.has(index)) next.delete(index);
      else next.add(index);
      return next;
    });
  };

  const handleReradicar = async () => {
    if (!instanceId || submitting) return;
    setSubmitting(true);
    setSubmitError(null);
    try {
      // AC2 — Re-radicar = POST /instances/{id}/submit: desde `subsanacion` re-radica directo a
      // `entregado` (SubmitProcedureInstanceHandler), re-evaluando solo los gates afectados.
      await tramitesClient.submitInstance(instanceId);
      onReradicado();
    } catch (err) {
      // Errores de gate (422/409) del submit: el backend ya devuelve `detail` legible en español
      // (ver ProcedureInstanceEndpoints.MapPost "/submit"); se muestra tal cual.
      setSubmitError(err instanceof Error ? err.message : 'No se pudo re-radicar el trámite.');
      setSubmitting(false);
    }
  };

  // Estado: cargando.
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

  // Estado: error al cargar el detalle de la instancia (motivo/checklist).
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

  // Estados vacío (sin checklist estructurado) y lleno (con ítems) se resuelven dentro del mismo
  // panel: el motivo SIEMPRE se muestra (con fallback); el checklist alterna su contenido.
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
        <p className="text-[10px] font-semibold uppercase opacity-60 mb-2">
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
                  className="flex items-start gap-2 rounded-lg border bg-white dark:bg-[#0B0F14] p-2"
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
            motivo indicado arriba y pulsa Re-radicar cuando termines.
          </p>
        )}
      </div>

      {submitError && (
        <p role="alert" className="text-xs" style={{ color: '#FF4E00' }}>
          {submitError}
        </p>
      )}

      <div>
        <button
          type="button"
          onClick={() => void handleReradicar()}
          disabled={!instanceId || !allResolved || submitting}
          className="inline-flex items-center gap-1.5 px-5 py-2 rounded-xl text-xs font-semibold text-white disabled:opacity-50"
          style={{ background: 'linear-gradient(135deg,#557EFF,#00DBD5)' }}
          title={
            hasChecklist && !allResolved
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
      </div>
    </section>
  );
}
