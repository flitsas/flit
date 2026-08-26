'use client';

import { Check, type LucideIcon } from 'lucide-react';
import { DETALLE_BLUE, DETALLE_CARD, DETALLE_GREEN, DETALLE_GREY, DETALLE_NAVY, DETALLE_BORDER } from './detalle-visual';

export interface DetallePaso {
  id: string;
  label: string;
  Icon: LucideIcon;
  /** Paso cumplido (círculo lima + check si no está seleccionado). */
  completo: boolean;
}

/**
 * Stepper del detalle — círculos 36px con icono/check (spec flit-detalle-tramite).
 * Distinto del StepMarker del wizard (28px + halo).
 */
export function DetalleStepper({
  pasos,
  pasoActivoId,
  onSelect,
}: {
  pasos: DetallePaso[];
  pasoActivoId: string;
  onSelect: (id: string) => void;
}) {
  return (
    <div className={`${DETALLE_CARD} mt-3 overflow-x-auto p-4`}>
      <div className="flex min-w-[760px] items-start" role="tablist" aria-label="Pasos del trámite">
        {pasos.map((paso, i) => {
          const isSel = paso.id === pasoActivoId;
          const done = paso.completo && !isSel;
          const bg = isSel ? DETALLE_BLUE : done ? DETALLE_GREEN : DETALLE_BORDER;
          const esUltimo = i === pasos.length - 1;
          const conectorVerde = paso.completo;

          return (
            <div key={paso.id} className="flex flex-1 items-center last:flex-none">
              <button
                type="button"
                role="tab"
                id={`detalle-tab-${paso.id}`}
                aria-selected={isSel}
                aria-controls={`detalle-panel-${paso.id}`}
                aria-label={`Paso ${i + 1}: ${paso.label}`}
                onClick={() => onSelect(paso.id)}
                className="flex shrink-0 flex-col items-center gap-1.5 rounded-xl px-2 py-1 transition focus:outline-none focus-visible:ring-2 focus-visible:ring-[#557EFF] focus-visible:ring-offset-2"
                style={isSel ? { boxShadow: `0 0 0 1.5px ${DETALLE_BLUE}` } : undefined}
              >
                <span
                  className="grid h-9 w-9 place-items-center rounded-full transition"
                  style={{ background: bg, color: bg === DETALLE_BORDER ? DETALLE_GREY : '#fff' }}
                  aria-hidden="true"
                >
                  {done ? (
                    <Check className="h-4 w-4" strokeWidth={2.5} />
                  ) : (
                    <paso.Icon className="h-4 w-4" />
                  )}
                </span>
                <span
                  className="whitespace-nowrap text-[10px] font-medium"
                  style={{
                    color: isSel ? DETALLE_BLUE : done ? DETALLE_NAVY : DETALLE_GREY,
                    opacity: isSel || done ? 1 : 0.85,
                  }}
                >
                  {i + 1}. {paso.label}
                </span>
              </button>
              {!esUltimo ? (
                <div
                  className="mx-1 mt-[-18px] h-0.5 flex-1 rounded-full"
                  style={{ background: conectorVerde ? DETALLE_GREEN : DETALLE_BORDER }}
                  aria-hidden="true"
                />
              ) : null}
            </div>
          );
        })}
      </div>
    </div>
  );
}
