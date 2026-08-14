'use client';

import type { StatusHistory } from '@/lib/api/types/procedure-runtime';
import { estadoLabel } from '@/lib/tramites/estados';

// Línea de tiempo del expediente. Adaptado del ExpedienteTimeline de Johan a la
// capa de datos de FLIT: la cronología se construye desde el statusHistory[] que
// ya devuelve getInstance (el historial real N 03 de procedure_instance_status_history).
// Labels/colores desde la fuente única lib/tramites/estados.ts (6 estados de negocio).
// El backend ya entrega el historial ordenado (fecha/hora + Id); aquí se re-ordena
// ASCENDENTE de forma defensiva para que la trazabilidad siempre se lea del estado
// inicial al actual aunque el caller pase los datos desordenados.

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

/**
 * Rótulo de un hito, en el formato literal de la propuesta: «Rechazado desde Entregado (motivo)».
 * `toStatus` siempre; `fromStatus` y `reason` se añaden solo cuando el backend los trae.
 */
function hitoLabel(e: StatusHistory): string {
  const to = estadoLabel(e.toStatus);
  const from = e.fromStatus ? estadoLabel(e.fromStatus) : null;
  const reason = e.reason?.trim();
  return `${to}${from ? ` desde ${from}` : ''}${reason ? ` (${reason})` : ''}`;
}

export default function ExpedienteTimeline({ statusHistory: rawHistory }: Props) {
  // Sort estable: conserva el desempate por Id que ya aplicó el backend en empates de fecha.
  const statusHistory = [...rawHistory].sort(
    (a, b) => new Date(a.changedAt).getTime() - new Date(b.changedAt).getTime(),
  );
  return (
    <section aria-label="Línea de tiempo del expediente" className="rounded-2xl border bg-white p-4 dark:bg-[#162744]">
      <div className="mb-3">
        <h4 className="text-sm font-bold">Expediente</h4>
        <p className="text-xs opacity-70">Trazabilidad cronológica del trámite.</p>
      </div>

      {statusHistory.length === 0 ? (
        <p className="text-xs opacity-70">Sin eventos registrados todavía.</p>
      ) : (
        // Conector único de la referencia (`#DFE5ED`), no el gris genérico de `border-l`.
        <ol className="relative space-y-3 border-l pl-4" style={{ borderColor: '#DFE5ED' }}>
          {statusHistory.map((e, i) => (
            <li key={`${e.toStatus}-${e.changedAt}-${i}`} className="relative">
              {/* Último hito en verde de marca, los previos en azul — no el color del chip de
                  estado (siete tonos distintos): la referencia solo distingue "el hito vigente"
                  del resto de la cronología. */}
              <span
                className="absolute -left-[21px] top-1 h-2.5 w-2.5 rounded-full"
                style={{ background: i === statusHistory.length - 1 ? '#8CC63F' : '#557EFF' }}
                aria-hidden="true"
              />
              <p className="text-xs font-semibold">{hitoLabel(e)}</p>
              {/* Piso tipográfico (12px = text-xs) y piso de opacidad (0.7) del sistema. */}
              <p className="mt-0.5 text-xs opacity-70">{fmt(e.changedAt)}</p>
            </li>
          ))}
        </ol>
      )}
    </section>
  );
}
