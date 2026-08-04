'use client';

import { Check, Lock } from 'lucide-react';
import type { WizardStep, WizardStepStatus } from '@/lib/api/types/procedure-runtime';
import { reasonCopy } from './wizard-copy';
import { canNavigateToStep } from './wizard-navigation';

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
        className="grid h-8 w-8 shrink-0 place-items-center rounded-full text-[11px] font-bold"
        style={{ background: '#8CC63F', color: '#fff' }}
        aria-hidden="true"
      >
        <Check className="h-4 w-4" strokeWidth={2.5} />
      </span>
    );
  }
  if (status === 'locked') {
    return (
      <span
        className="grid h-8 w-8 shrink-0 place-items-center rounded-full text-[11px] font-bold"
        style={{ background: '#EEF1F5', color: '#9AA5B1' }}
        aria-hidden="true"
      >
        <Lock className="h-3.5 w-3.5" />
      </span>
    );
  }
  if (active) {
    return (
      <span
        className="grid h-8 w-8 shrink-0 place-items-center rounded-full text-[11px] font-bold"
        style={{ background: '#557EFF', color: '#fff' }}
        aria-hidden="true"
      >
        {index + 1}
      </span>
    );
  }
  return (
    <span
      className="grid h-8 w-8 shrink-0 place-items-center rounded-full border-2 bg-white text-[11px] font-bold dark:bg-[#0B0F14]"
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
}: WizardStepTrackerProps) {
  if (steps.length === 0) return null;

  return (
    <nav aria-label="Asistente de seguimiento" className="w-full py-1">
      <ol className="flex w-full min-w-0 items-start gap-0">
        {steps.map((s, i) => {
          const isActive = i === activeIndex;
          const clickable = canNavigateToStep(steps, i, viewOnly);
          const prevComplete = i > 0 && steps[i - 1]?.status === 'complete';
          const lineAfterGreen = s.status === 'complete';
          return (
            <li
              key={s.key}
              className="relative flex min-w-0 flex-1 flex-col items-center"
              aria-current={isActive ? 'step' : undefined}
            >
              {i > 0 && (
                <span
                  className="pointer-events-none absolute left-0 right-1/2 top-4 h-0.5 -translate-y-1/2"
                  style={{ background: prevComplete ? '#8CC63F' : '#DFE5ED' }}
                  aria-hidden="true"
                />
              )}
              {i < steps.length - 1 && (
                <span
                  className="pointer-events-none absolute left-1/2 right-0 top-4 h-0.5 -translate-y-1/2"
                  style={{ background: lineAfterGreen ? '#8CC63F' : '#DFE5ED' }}
                  aria-hidden="true"
                />
              )}
              <button
                type="button"
                onClick={() => onGoToStep(i)}
                disabled={!clickable}
                className="relative z-10 flex w-full flex-col items-center gap-2 px-1 text-center outline-none disabled:cursor-not-allowed focus-visible:ring-2 focus-visible:ring-[#557EFF]/focus-visible:ring-offset-2"
                aria-label={`Paso ${i + 1}: ${s.label} (${s.status})`}
              >
                <StepMarker status={s.status} index={i} active={isActive} />
                <span className="min-w-0 w-full">
                  <span
                    className={`block truncate text-[11px] leading-snug ${
                      isActive ? 'font-bold' : 'font-medium'
                    }`}
                    style={
                      isActive
                        ? { color: '#557EFF' }
                        : s.status === 'complete'
                          ? { color: '#59677D' }
                          : { color: '#9AA5B1' }
                    }
                    title={s.label}
                  >
                    {s.label}
                  </span>
                  {isActive && s.status === 'incomplete' && s.reasons.length > 0 && (
                    <span className="mt-1 block space-y-0.5">
                      {s.reasons.map((r) => (
                        <span
                          key={r}
                          className="block truncate text-[10px]"
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
