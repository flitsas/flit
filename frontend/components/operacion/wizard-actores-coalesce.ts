import type { WizardStep, WizardStepStatus } from '@/lib/api/types/procedure-runtime';
import { resolveStepBody } from './sectionRendererRegistry';

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

/** ¿El paso captura partes? Lo decide el `section_type` parametrizado, no su clave. */
function esPasoDeActores(step: WizardStep | undefined): boolean {
  return !!step && resolveStepBody(step) === 'actores';
}

/**
 * Longitud de la tanda de pasos de actores CONSECUTIVOS que empieza en `desde`.
 * Devuelve 1 cuando el paso está solo (o no es de actores).
 */
function largoDeLaTanda(steps: WizardStep[], desde: number): number {
  if (!esPasoDeActores(steps[desde])) return 1;
  let fin = desde;
  while (esPasoDeActores(steps[fin + 1])) fin += 1;
  return fin - desde + 1;
}

/** Índice donde empieza la tanda de actores que contiene a `index`; el propio índice si no aplica. */
function inicioDeLaTanda(steps: WizardStep[], index: number): number {
  if (!esPasoDeActores(steps[index])) return index;
  let inicio = index;
  while (inicio > 0 && esPasoDeActores(steps[inicio - 1])) inicio -= 1;
  return inicio;
}

/**
 * Fusiona en un solo paso visual «Actores» los pasos de actores CONSECUTIVOS.
 *
 * <p>El catálogo modela una parte por paso —así el motor puede exigirlas y completarlas una a una—
 * pero el gestor las captura juntas en una pantalla, porque las compara entre sí. Nació para
 * vendedor+comprador del traspaso; se generalizó a cualquier tanda para que el leasing
 * (propietario + locatario) obtenga lo mismo sin una segunda lista de claves que mantener.</p>
 *
 * <p>Un paso de actores SOLO —matrícula, o un trámite de OTROS con un único titular— se deja tal
 * cual, con su propio rótulo.</p>
 */
export function coalesceActorSteps(steps: WizardStep[]): DisplayWizardStep[] {
  const out: DisplayWizardStep[] = [];
  for (let i = 0; i < steps.length; i++) {
    const largo = largoDeLaTanda(steps, i);
    if (largo < 2) {
      out.push({ ...steps[i], sourceIndexes: [i] });
      continue;
    }

    const tanda = steps.slice(i, i + largo);
    out.push({
      index: steps[i].index,
      key: 'actores',
      label: 'Actores',
      status: tanda.map((s) => s.status).reduce(mergeStatus),
      reasons: tanda.flatMap((s) => s.reasons),
      sourceIndexes: tanda.map((_, k) => i + k),
    });
    i += largo - 1;
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
 * Tras guardar los actores: si TODA la tanda quedó completa, salta al paso siguiente a ella (no al
 * de al lado, que sería la otra parte de la misma pantalla que el gestor acaba de llenar).
 */
export function nextIndexAfterUnifiedActores(
  steps: WizardStep[],
  activeIndex: number,
): number {
  const siguiente = Math.min(activeIndex + 1, steps.length - 1);
  const inicio = inicioDeLaTanda(steps, activeIndex);
  const largo = largoDeLaTanda(steps, inicio);
  if (largo < 2) return siguiente;

  const tanda = steps.slice(inicio, inicio + largo);
  if (!tanda.every((s) => s.status === 'complete')) return siguiente;

  return Math.min(inicio + largo, steps.length - 1);
}

/** El paso comparte pantalla con otra parte (tanda de actores de dos o más). */
export function esPasoDeActoresUnificado(steps: WizardStep[], index: number): boolean {
  return largoDeLaTanda(steps, inicioDeLaTanda(steps, index)) > 1;
}
