'use client';

import { useEffect, useMemo, useState } from 'react';
import { AlertTriangle, Loader2 } from 'lucide-react';
import type { StatusHistory } from '@/lib/api/types/procedure-runtime';
import {
  latestRejectionReason,
  latestSubsanacionEntry,
  parseSubsanacionObservation,
} from '@/lib/tramites/subsanacion';

interface SubsanacionPanelProps {
  statusHistory: StatusHistory[];
  /** Carga del detalle de la instancia (fuente de `statusHistory`), a cargo del padre. */
  loading: boolean;
  /** Error al cargar el detalle de la instancia, a cargo del padre. */
  error: string | null;
  /**
   * Checklist completo (o inexistente). El botón "Re-radicar" vive en el PIE del asistente, junto
   * a Guardar y continuar, así que el panel solo informa: el checklist es suyo, la acción no.
   */
  onChecklistResueltoChange?: (resuelto: boolean) => void;
  /**
   * Hay cambios locales sin guardar. El panel no gobierna Re-radicar, pero sí explica por qué
   * todavía no se puede.
   */
  hasUnsavedChanges?: boolean;
  /**
   * Tras editar y Guardar y continuar al menos una vez (y sin dirty pendiente).
   * Sin esto Re-radicar no se habilita (hay que modificar algo primero).
   */
  canReradicar?: boolean;
}

const FALLBACK_MOTIVO =
  'El organismo de tránsito devolvió el trámite para corrección. Revisa los datos y documentos antes de re-radicar.';

/**
 * HU #10874 — panel de subsanación: motivo + checklist de ítems a subsanar.
 * Feature #11066 — Cancelar sale del flag.
 *
 * "Re-radicar" NO vive aquí: es la acción terminal del asistente y por eso está en el pie, con
 * Guardar y continuar. El panel le reporta al wizard si el checklist está resuelto y nada más.
 */
export function SubsanacionPanel({
  statusHistory,
  loading,
  error,
  onChecklistResueltoChange,
  hasUnsavedChanges = false,
  canReradicar = false,
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

  const allResolved = !hasChecklist || items.every((_, i) => resolved.has(i));

  // Sin checklist `allResolved` nace en true, así que el pie recibe la señal desde el primer
  // render: el gate de Re-radicar es entonces solo la edición guardada.
  useEffect(() => {
    onChecklistResueltoChange?.(allResolved);
  }, [allResolved, onChecklistResueltoChange]);

  const toggleItem = (index: number) => {
    setResolved((prev) => {
      const next = new Set(prev);
      if (next.has(index)) next.delete(index);
      else next.add(index);
      return next;
    });
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
        <Loader2 className="h-4 w-4 shrink-0 animate-spin" style={{ color: 'var(--badge-warning-fg)' }} aria-hidden="true" />
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
        <AlertTriangle className="h-4 w-4 shrink-0 mt-0.5" style={{ color: 'var(--badge-warning-fg)' }} aria-hidden="true" />
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
            corregir, usa «Cancelar subsanación» arriba a la derecha.
          </p>
        )}
      </div>

      {hasUnsavedChanges ? (
        <p className="text-xs opacity-70">
          Hay cambios sin guardar. Usa Guardar y continuar; después podrás re-radicar desde el pie
          del asistente.
        </p>
      ) : !canReradicar ? (
        <p className="text-xs opacity-70">
          Edita lo necesario y pulsa Guardar y continuar: Re-radicar te espera en el pie del
          asistente. Si no hace falta corregir, usa «Cancelar subsanación» arriba a la derecha.
        </p>
      ) : (
        <p className="text-xs opacity-70" role="status">
          Cambios guardados. Ya puedes re-radicar desde el pie del asistente.
        </p>
      )}
    </section>
  );
}
