'use client';

import { Clock } from 'lucide-react';
import type { StatusHistory } from '@/lib/api/types/procedure-runtime';
import { estadoLabel } from '@/lib/tramites/estados';
import { DETALLE_BLUE, DETALLE_CARD, DETALLE_NAVY, DETALLE_BORDER } from './detalle-visual';

function fmt(iso: string): string {
  try {
    return new Date(iso).toLocaleString('es-CO', { dateStyle: 'medium', timeStyle: 'short' });
  } catch {
    return iso;
  }
}

function hitoTexto(e: StatusHistory): string {
  const to = estadoLabel(e.toStatus);
  const from = e.fromStatus ? estadoLabel(e.fromStatus) : null;
  const reason = e.reason?.trim();
  return `${to}${from ? ` desde ${from}` : ''}${reason ? ` (${reason})` : ''}`;
}

/**
 * Columna «Historial de auditoría» del paso FUR (mockup Paso5 — timeline vertical, no TimelineTrack).
 */
export function DetalleHistorialAuditoria({
  statusHistory,
  referenceNumber,
}: {
  statusHistory: StatusHistory[];
  referenceNumber: string;
}) {
  const sorted = [...statusHistory].sort(
    (a, b) => new Date(a.changedAt).getTime() - new Date(b.changedAt).getTime(),
  );

  return (
    <section className={`${DETALLE_CARD} h-full`} aria-label="Historial de auditoría">
      <h4
        className="mb-3 flex items-center gap-2 text-sm font-semibold"
        style={{ color: DETALLE_NAVY }}
      >
        <Clock className="h-4 w-4" aria-hidden="true" />
        Historial de auditoría
      </h4>
      {sorted.length === 0 ? (
        <p className="text-xs opacity-70">Sin eventos registrados todavía.</p>
      ) : (
        <ol className="relative pl-4">
          <span
            className="absolute bottom-1 left-1 top-1 w-px"
            style={{ background: DETALLE_BORDER }}
            aria-hidden="true"
          />
          {sorted.map((e, i) => (
            <li key={`${e.toStatus}-${e.changedAt}-${i}`} className="relative pb-4 last:pb-0">
              <span
                className="absolute -left-3 top-1 h-2 w-2 rounded-full"
                style={{ background: DETALLE_BLUE }}
                aria-hidden="true"
              />
              <p className="text-xs font-medium">{hitoTexto(e)}</p>
              <p className="text-[10px] opacity-60">{fmt(e.changedAt)}</p>
            </li>
          ))}
        </ol>
      )}
      <p className="mt-2 font-mono text-[10px] opacity-60">Radicado: {referenceNumber}</p>
    </section>
  );
}
