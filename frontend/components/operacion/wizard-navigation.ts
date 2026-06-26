import type { WizardStep } from '@/lib/api/types/procedure-runtime';

/**
 * Navegación en cascada del wizard. El backend ya marca los pasos no alcanzables
 * como `locked`, pero el frontend impone además la regla de la FRONTERA: solo se
 * puede navegar a un paso ya completado o al primer paso incompleto (la frontera).
 * Así se evita saltar a un paso futuro (p.ej. Identidad) sin haber completado el
 * anterior (p.ej. Comprador), incluso si el backend lo devolviera como `incomplete`.
 */

/**
 * Índice del primer paso aún no completado (la "frontera" del flujo). Si todos los
 * pasos están completos, devuelve el último índice (todos navegables de todos modos).
 */
export function frontierIndex(steps: WizardStep[]): number {
  const i = steps.findIndex((s) => s.status !== 'complete');
  return i === -1 ? Math.max(0, steps.length - 1) : i;
}

/**
 * Pasos DIFERIDOS (HU #10349/#10350): identidad y FUR quedan `incomplete` aun con los datos
 * completos, porque dependen de la validación/firma async. A diferencia de un paso de datos
 * incompleto, NO deben bloquear la navegación hacia adelante: el gestor recorre hasta el último paso
 * (FUR) para finalizar el borrador o radicar. Por eso un diferido incompleto no es "frontera dura".
 */
const DEFERRED_STEP_KEYS = new Set(['identidad', 'fur']);

/** Un paso incompleto que SÍ corta la cascada (un paso de datos sin terminar, no un diferido). */
function isBlockingIncomplete(step: WizardStep): boolean {
  return step.status === 'incomplete' && !DEFERRED_STEP_KEYS.has(step.key);
}

/**
 * ¿Se puede navegar al paso `index`? El backend marca como `locked` lo no alcanzable; el frontend
 * además exige que no haya un paso de DATOS incompleto por delante (cascada). Los pasos diferidos
 * (identidad/FUR) incompletos NO cortan la cascada, así el gestor puede llegar al último paso a
 * finalizar/radicar aunque la identidad siga pendiente.
 *
 * En modo solo lectura (`viewOnly`, Track C) no hay cascada que respetar: el usuario solo recorre lo
 * ya resuelto, así que únicamente son navegables los pasos `complete` (en un trámite enviado, todos).
 */
export function canNavigateToStep(
  steps: WizardStep[],
  index: number,
  viewOnly = false,
): boolean {
  const step = steps[index];
  if (!step) return false;
  if (step.status === 'locked') return false;
  if (viewOnly) return step.status === 'complete';
  // Navegable si ningún paso de datos previo quedó incompleto (los diferidos no cuentan).
  for (let i = 0; i < index; i++) {
    if (steps[i].status === 'locked' || isBlockingIncomplete(steps[i])) return false;
  }
  return true;
}
