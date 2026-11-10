'use client';

import { Check, type LucideIcon } from 'lucide-react';
import { DETALLE_CARD } from './detalle-visual';

export interface DetallePaso {
  id: string;
  label: string;
  Icon: LucideIcon;
  /** Paso cumplido (círculo turquesa + check si no está seleccionado). */
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
          // Lenguaje de la propuesta (línea de avance): hecho en turquesa, el paso abierto en azul de
          // acción con halo, lo que falta en blanco con aro gris. Sin verde.
          const marcador = isSel
            ? {
                background: 'var(--di-action)',
                borderColor: 'var(--di-action)',
                color: 'var(--di-on-action)',
                boxShadow: '0 0 0 5px var(--di-accent-soft)',
              }
            : done
              ? { background: 'var(--color-flit-tech)', borderColor: 'var(--color-flit-tech)', color: 'var(--flit-tinta, #162744)' }
              : { background: 'var(--di-surface-elevated)', borderColor: 'var(--di-border-control)', color: 'var(--di-text-tertiary)' };
          const esUltimo = i === pasos.length - 1;
          const conectorHecho = paso.completo;

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
                className="flex shrink-0 flex-col items-center gap-2 rounded-xl px-2 py-1 transition hover:bg-[var(--di-accent-soft)] focus:outline-none focus-visible:ring-2 focus-visible:ring-flit-brand focus-visible:ring-offset-2"
              >
                <span
                  className="grid h-8 w-8 place-items-center rounded-full border-[3px] transition"
                  style={marcador}
                  aria-hidden="true"
                >
                  {done ? (
                    <Check className="h-3.5 w-3.5" strokeWidth={3} />
                  ) : (
                    <paso.Icon className="h-3.5 w-3.5" />
                  )}
                </span>
                <span
                  className={`whitespace-nowrap text-[12px] ${isSel ? 'font-bold' : 'font-semibold'}`}
                  style={{
                    color: isSel ? 'var(--di-action)' : done ? 'var(--di-heading)' : 'var(--di-text-secondary)',
                  }}
                >
                  {i + 1}. {paso.label}
                </span>
              </button>
              {!esUltimo ? (
                <div
                  className="mx-1 mt-[-22px] h-[3px] flex-1 rounded-full"
                  style={{ background: conectorHecho ? 'var(--color-flit-tech)' : 'var(--di-surface-sunken)' }}
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
