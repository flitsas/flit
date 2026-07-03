'use client';

import type { StatusHistory } from '@/lib/api/types/procedure-runtime';
import { estadoChipStyle, estadoLabel } from '@/lib/tramites/estados';

// Línea de tiempo del expediente. Adaptado del ExpedienteTimeline de Johan a la
// capa de datos de FLIT: la cronología se construye desde el statusHistory[] que
// ya devuelve getInstance (sin endpoint de eventos, QR ni PDF — fuera de alcance
// hasta que exista el backend correspondiente). N 03 — labels/colores desde la
// fuente única lib/tramites/estados.ts (6 estados de negocio).

interface Props {
  statusHistory: StatusHistory[];
}

function fmt(iso: string): string {
  try {
    return new Date(iso).toLocaleString('es-CO', { dateStyle: 'medium', timeStyle: 'short' });
  } catch {
    return iso;
  }
}

export default function ExpedienteTimeline({ statusHistory }: Props) {
  return (
    <section aria-label="Línea de tiempo del expediente" className="rounded-xl border p-4" style={{ borderColor: '#DFE5ED' }}>
      <div className="mb-3">
        <h4 className="text-sm font-bold">Expediente</h4>
        <p className="text-xs opacity-70">Trazabilidad cronológica del trámite.</p>
      </div>

      {statusHistory.length === 0 ? (
        <p className="text-xs opacity-60">Sin eventos registrados todavía.</p>
      ) : (
        <ol className="relative space-y-3 border-l pl-4" style={{ borderColor: '#DFE5ED' }}>
          {statusHistory.map((e, i) => (
            <li key={`${e.toStatus}-${e.changedAt}-${i}`} className="relative">
              <span
                className="absolute -left-[21px] top-1 h-2.5 w-2.5 rounded-full"
                style={{ background: estadoChipStyle(e.toStatus).color }}
                aria-hidden="true"
              />
              <p className="text-xs font-semibold">{estadoLabel(e.toStatus)}</p>
              <p className="text-[11px] opacity-60">{fmt(e.changedAt)}</p>
              {e.reason && <p className="mt-0.5 text-[11px] opacity-70">{e.reason}</p>}
            </li>
          ))}
        </ol>
      )}
    </section>
  );
}
