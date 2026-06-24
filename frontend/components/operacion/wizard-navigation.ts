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
 * ¿Se puede navegar al paso `index`? Solo si está completo o es exactamente la
 * frontera (el primer paso incompleto). Cualquier paso más allá de la frontera
 * queda fuera de alcance.
 *
 * En modo solo lectura (`viewOnly`, Track C) no hay frontera que respetar: el
 * usuario solo recorre lo ya resuelto, así que únicamente son navegables los
 * pasos `complete` (en un trámite enviado, típicamente todos).
 */
export function canNavigateToStep(
  steps: WizardStep[],
  index: number,
  viewOnly = false,
): boolean {
  const step = steps[index];
  if (!step) return false;
  if (viewOnly) return step.status === 'complete';
  return step.status === 'complete' || index === frontierIndex(steps);
}
