'use client';

import { useMemo } from 'react';
import { Check, Lock } from 'lucide-react';
import type { WizardStep, WizardStepStatus } from '@/lib/api/types/procedure-runtime';
import { reasonCopy, stepLabelCopy } from './wizard-copy';
import { canNavigateToStep } from './wizard-navigation';
import {
  coalesceTraspasoActorSteps,
  displayIndexForActive,
  sourceIndexForDisplayClick,
  type DisplayWizardStep,
} from './wizard-actores-coalesce';

/** Icono/marcador por status del paso (✓ / número activo / outline / 🔒). */
function StepMarker({
  status,
  index,
  active = false,
}: {
  status: WizardStepStatus;
  index: number;
  active?: boolean;
}) {
  if (status === 'complete') {
    return (
      <span
        className="grid h-8 w-8 shrink-0 place-items-center rounded-full text-xs font-bold"
        style={{ background: '#8CC63F', color: '#fff' }}
        aria-hidden="true"
      >
        <Check className="h-4 w-4" strokeWidth={2.5} />
      </span>
    );
  }
  // Pendiente y bloqueado comparten la forma de la propuesta: círculo blanco con borde. El candado
  // distingue el bloqueado sin recurrir a un relleno gris que rompe la línea de círculos.
  if (status === 'locked') {
    return (
      <span
        className="grid h-8 w-8 shrink-0 place-items-center rounded-full bg-white text-xs font-bold dark:bg-[#0B0F14]"
        style={{ border: '2px solid #DFE5ED', color: '#9AA5B1' }}
        aria-hidden="true"
      >
        <Lock className="h-3.5 w-3.5" />
      </span>
    );
  }
  // Activo: círculo blanco con borde y número en azul, más un halo. El relleno azul sólido competía
  // con el verde de los completos y hacía que el paso en curso pareciera otro estado terminado.
  if (active) {
    return (
      <span
        className="grid h-8 w-8 shrink-0 place-items-center rounded-full bg-white text-xs font-bold dark:bg-[#0B0F14]"
        style={{
          border: '2px solid #557EFF',
          color: '#557EFF',
          boxShadow: '0 0 0 4px rgba(85,126,255,0.15)',
        }}
        aria-hidden="true"
      >
        {index + 1}
      </span>
    );
  }
  return (
    <span
      className="grid h-8 w-8 shrink-0 place-items-center rounded-full border-2 bg-white text-xs font-bold dark:bg-[#0B0F14]"
      style={{ borderColor: '#C5CDD8', color: '#9AA5B1' }}
      aria-hidden="true"
    >
      {index + 1}
    </span>
  );
}

export type WizardStepTrackerProps = {
  steps: WizardStep[];
  activeIndex: number;
  onGoToStep: (index: number) => void;
  /** Solo lectura / vista: misma cascada de navegación que el wizard. */
  viewOnly?: boolean;
  /**
   * Traspaso: fusiona vendedor+comprador en un solo ítem visual "Actores".
   * Matrícula no aplica (queda 1:1).
   */
  coalesceActores?: boolean;
  /**
   * Modalidad del trámite. Solo decide cómo se NOMBRAN los pasos: la propuesta llama distinto a
   * las mismas claves en cada asistente ("Requisitos" vs "Documentos", "Resumen" vs "FUR y
   * Expediente"). No altera la estructura, que sigue viniendo del servidor.
   */
  modalidad?: 'matricula_inicial' | 'traspaso';
};

/**
 * Asistente de seguimiento horizontal (patrón TimelineProcess / wizard FLIT).
 * Va dentro del chrome sticky del wizard (título + pasos). Etiquetas siempre
 * visibles; verde = completo, azul = paso activo. Sin card blanca contenedora.
 */
export function WizardStepTracker({
  steps,
  activeIndex,
  onGoToStep,
  viewOnly = false,
  coalesceActores = false,
  modalidad,
}: WizardStepTrackerProps) {
  const displaySteps: DisplayWizardStep[] = useMemo(
    () => (coalesceActores ? coalesceTraspasoActorSteps(steps) : steps.map((s, i) => ({ ...s, sourceIndexes: [i] }))),
    [steps, coalesceActores],
  );
  const displayActive = displayIndexForActive(displaySteps, activeIndex);

  if (displaySteps.length === 0) return null;

  return (
    <nav aria-label="Asistente de seguimiento" className="w-full py-1">
      <ol className="flex w-full min-w-0 items-start gap-0">
        {displaySteps.map((s, i) => {
          const isActive = i === displayActive;
          const clickable = s.sourceIndexes.some((idx) => canNavigateToStep(steps, idx, viewOnly));
          // Las líneas marcan RECORRIDO, no completitud: en la propuesta van en azul hasta el paso
          // en curso. Coloreadas por `status === 'complete'` un paso saltado dejaba el hilo gris en
          // medio del recorrido y parecía que el asistente se había roto.
          const prevComplete = i <= displayActive;
          const lineAfterGreen = i < displayActive;
          // Nombre en la nomenclatura del diseño; cae al del servidor si la clave no está mapeada.
          const label = stepLabelCopy(s.key, s.label, modalidad);
          return (
            <li
              key={s.key}
              className="relative flex min-w-0 flex-1 flex-col items-center"
              aria-current={isActive ? 'step' : undefined}
            >
              {i > 0 && (
                <span
                  className="pointer-events-none absolute left-0 right-1/2 top-4 h-0.5 -translate-y-1/2"
                  style={{ background: prevComplete ? '#557EFF' : '#DFE5ED' }}
                  aria-hidden="true"
                />
              )}
              {i < displaySteps.length - 1 && (
                <span
                  className="pointer-events-none absolute left-1/2 right-0 top-4 h-0.5 -translate-y-1/2"
                  style={{ background: lineAfterGreen ? '#557EFF' : '#DFE5ED' }}
                  aria-hidden="true"
                />
              )}
              <button
                type="button"
                onClick={() => onGoToStep(sourceIndexForDisplayClick(s))}
                disabled={!clickable}
                // El foco estaba roto: la clase venía escrita como
                // `focus-visible:ring-[#557EFF]/focus-visible:ring-offset-2` (sin separar), que
                // Tailwind no reconoce — el paso activo no mostraba anillo alguno al tabular.
                className="relative z-10 flex w-full flex-col items-center gap-2 rounded-lg px-1 text-center outline-none disabled:cursor-not-allowed focus-visible:ring-2 focus-visible:ring-[#557EFF] focus-visible:ring-offset-2"
                aria-label={`Paso ${i + 1}: ${label} (${s.status})`}
              >
                <StepMarker status={s.status} index={i} active={isActive} />
                <span className="min-w-0 w-full">
                  <span
                    className={`block truncate text-xs leading-snug ${
                      isActive ? 'font-bold' : 'font-medium'
                    }`}
                    style={
                      isActive
                        ? { color: '#557EFF' }
                        : s.status === 'complete'
                          ? { color: '#59677D' }
                          : { color: '#9AA5B1' }
                    }
                    title={label}
                  >
                    {label}
                  </span>
                  {isActive && s.status === 'incomplete' && s.reasons.length > 0 && (
                    <span className="mt-1 block space-y-0.5">
                      {s.reasons.map((r) => (
                        <span
                          key={r}
                          className="block truncate text-xs"
                          style={{ color: '#F9AC00' }}
                          title={reasonCopy(r)}
                        >
                          • {reasonCopy(r)}
                        </span>
                      ))}
                    </span>
                  )}
                </span>
              </button>
            </li>
          );
        })}
      </ol>
    </nav>
  );
}
