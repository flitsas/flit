import type { WizardStep, WizardStepStatus } from '@/lib/api/types/procedure-runtime';

/** Pasos de actores del traspaso que se muestran unificados en UI. */
export const TRASPASO_ACTOR_STEP_KEYS = new Set(['vendedor', 'comprador']);

export type DisplayWizardStep = WizardStep & {
  /** Índices reales en `steps` del server que representa este ítem visual. */
  sourceIndexes: number[];
};

function mergeStatus(a: WizardStepStatus, b: WizardStepStatus): WizardStepStatus {
  if (a === 'locked' && b === 'locked') return 'locked';
  if (a === 'complete' && b === 'complete') return 'complete';
  if (a === 'locked' || b === 'locked') return 'incomplete';
  return 'incomplete';
}

/**
 * Traspaso: fusiona `vendedor` + `comprador` en un solo paso visual "Actores".
 * Matrícula y el resto de pasos se dejan igual (1:1 con el server).
 */
export function coalesceTraspasoActorSteps(steps: WizardStep[]): DisplayWizardStep[] {
  const out: DisplayWizardStep[] = [];
  for (let i = 0; i < steps.length; i++) {
    const step = steps[i];
    if (step.key === 'vendedor' && steps[i + 1]?.key === 'comprador') {
      const next = steps[i + 1];
      out.push({
        index: step.index,
        key: 'actores',
        label: 'Actores',
        status: mergeStatus(step.status, next.status),
        reasons: [...step.reasons, ...next.reasons],
        sourceIndexes: [i, i + 1],
      });
      i += 1;
      continue;
    }
    out.push({ ...step, sourceIndexes: [i] });
  }
  return out;
}

/** Índice visual activo a partir del índice real del wizard. */
export function displayIndexForActive(
  displaySteps: DisplayWizardStep[],
  activeIndex: number,
): number {
  const idx = displaySteps.findIndex((d) => d.sourceIndexes.includes(activeIndex));
  return idx >= 0 ? idx : 0;
}

/** Al hacer clic en un paso visual, navega al primer source index navegable. */
export function sourceIndexForDisplayClick(displayStep: DisplayWizardStep): number {
  return displayStep.sourceIndexes[0] ?? 0;
}

/**
 * Tras guardar actores en traspaso: si vendedor y comprador quedaron complete,
 * salta al paso siguiente a ambos (documentos).
 */
export function nextIndexAfterUnifiedActores(
  steps: WizardStep[],
  activeIndex: number,
): number {
  const vendedorIdx = steps.findIndex((s) => s.key === 'vendedor');
  const compradorIdx = steps.findIndex((s) => s.key === 'comprador');
  if (vendedorIdx < 0 || compradorIdx < 0) {
    return Math.min(activeIndex + 1, steps.length - 1);
  }
  const bothDone =
    steps[vendedorIdx]?.status === 'complete' && steps[compradorIdx]?.status === 'complete';
  if (
    bothDone &&
    (activeIndex === vendedorIdx || activeIndex === compradorIdx)
  ) {
    return Math.min(Math.max(vendedorIdx, compradorIdx) + 1, steps.length - 1);
  }
  return Math.min(activeIndex + 1, steps.length - 1);
}

export function isTraspasoActorStepKey(key: string | undefined): boolean {
  return !!key && TRASPASO_ACTOR_STEP_KEYS.has(key);
}
