import { describe, expect, it } from 'vitest';
import type { WizardStep } from '@/lib/api/types/procedure-runtime';
import {
  coalesceTraspasoActorSteps,
  displayIndexForActive,
  nextIndexAfterUnifiedActores,
} from '@/components/operacion/wizard-actores-coalesce';

function step(
  key: string,
  status: WizardStep['status'] = 'incomplete',
  index = 0,
): WizardStep {
  return { index, key, label: key, status, reasons: [] };
}

describe('wizard-actores-coalesce', () => {
  it('fusiona vendedor+comprador en Actores', () => {
    const steps = [
      step('consulta', 'complete'),
      step('vendedor'),
      step('comprador'),
      step('documentos', 'locked'),
    ];
    const display = coalesceTraspasoActorSteps(steps);
    expect(display.map((d) => d.key)).toEqual(['consulta', 'actores', 'documentos']);
    expect(display[1].sourceIndexes).toEqual([1, 2]);
    expect(display[1].label).toBe('Actores');
  });

  it('Actores solo complete si ambos lo están', () => {
    const steps = [
      step('vendedor', 'complete'),
      step('comprador', 'incomplete'),
    ];
    expect(coalesceTraspasoActorSteps(steps)[0].status).toBe('incomplete');
    steps[1] = step('comprador', 'complete');
    expect(coalesceTraspasoActorSteps(steps)[0].status).toBe('complete');
  });

  it('displayIndex marca activo en cualquiera de los source indexes', () => {
    const display = coalesceTraspasoActorSteps([
      step('consulta', 'complete'),
      step('vendedor'),
      step('comprador'),
    ]);
    expect(displayIndexForActive(display, 1)).toBe(1);
    expect(displayIndexForActive(display, 2)).toBe(1);
  });

  it('nextIndexAfterUnifiedActores salta a documentos si ambos complete', () => {
    const steps = [
      step('consulta', 'complete'),
      step('vendedor', 'complete'),
      step('comprador', 'complete'),
      step('documentos'),
    ];
    expect(nextIndexAfterUnifiedActores(steps, 1)).toBe(3);
    expect(nextIndexAfterUnifiedActores(steps, 2)).toBe(3);
  });
});
