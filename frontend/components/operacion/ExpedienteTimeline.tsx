'use client';

import type { StatusHistory } from '@/lib/api/types/procedure-runtime';
import { estadoLabel } from '@/lib/tramites/estados';
import { formatFechaHora } from '@/lib/format/date';
import { WizardAccordion } from './WizardAccordion';

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
    return formatFechaHora(new Date(iso));
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

function sortHistory(rawHistory: StatusHistory[]): StatusHistory[] {
  return [...rawHistory].sort(
    (a, b) => new Date(a.changedAt).getTime() - new Date(b.changedAt).getTime(),
  );
}

function ExpedienteTimelineList({ statusHistory }: { statusHistory: StatusHistory[] }) {
  if (statusHistory.length === 0) {
    return <p className="text-xs opacity-70">Sin eventos registrados todavía.</p>;
  }

  return (
    <ol className="relative space-y-3 border-l pl-4" style={{ borderColor: '#DFE5ED' }}>
      {statusHistory.map((e, i) => (
        <li key={`${e.toStatus}-${e.changedAt}-${i}`} className="relative">
          <span
            className="absolute -left-[21px] top-1 h-2.5 w-2.5 rounded-full"
            style={{ background: i === statusHistory.length - 1 ? '#8CC63F' : '#557EFF' }}
            aria-hidden="true"
          />
          <p className="text-xs font-semibold">{hitoLabel(e)}</p>
          <p className="mt-0.5 text-xs opacity-70">{fmt(e.changedAt)}</p>
        </li>
      ))}
    </ol>
  );
}

/** Sección fija (legacy) — preferir `ExpedienteCronologicoAccordion` en el wizard. */
export default function ExpedienteTimeline({ statusHistory: rawHistory }: Props) {
  const statusHistory = sortHistory(rawHistory);
  return (
    <section aria-label="Línea de tiempo del expediente" className="rounded-2xl border bg-white p-4 dark:bg-[#162744]">
      <div className="mb-3">
        <h4 className="text-sm font-bold">Expediente</h4>
        <p className="text-xs opacity-70">Trazabilidad cronológica del trámite.</p>
      </div>
      <ExpedienteTimelineList statusHistory={statusHistory} />
    </section>
  );
}

/**
 * HU #12730 (D.5) — expediente cronológico como acordeón plegado con resumen en cabecera.
 */
export function ExpedienteCronologicoAccordion({ statusHistory: rawHistory }: Props) {
  const statusHistory = sortHistory(rawHistory);
  const last = statusHistory[statusHistory.length - 1];
  const subtitle =
    statusHistory.length === 0
      ? 'Sin eventos registrados'
      : `${statusHistory.length} evento${statusHistory.length === 1 ? '' : 's'} · último: ${hitoLabel(last)} · ${fmt(last.changedAt)}`;

  return (
    <WizardAccordion
      title="Expediente — Trazabilidad cronológica"
      subtitle={subtitle}
      defaultOpen={false}
      level="h3"
      regionLabel="Trazabilidad cronológica del trámite"
    >
      <ExpedienteTimelineList statusHistory={statusHistory} />
    </WizardAccordion>
  );
}
