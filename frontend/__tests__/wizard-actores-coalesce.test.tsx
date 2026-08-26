import { describe, expect, it } from 'vitest';
import type { WizardStep } from '@/lib/api/types/procedure-runtime';
import {
  coalesceActorSteps,
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
    const display = coalesceActorSteps(steps);
    expect(display.map((d) => d.key)).toEqual(['consulta', 'actores', 'documentos']);
    expect(display[1].sourceIndexes).toEqual([1, 2]);
    expect(display[1].label).toBe('Actores');
  });

  it('Actores solo complete si ambos lo están', () => {
    const steps = [
      step('vendedor', 'complete'),
      step('comprador', 'incomplete'),
    ];
    expect(coalesceActorSteps(steps)[0].status).toBe('incomplete');
    steps[1] = step('comprador', 'complete');
    expect(coalesceActorSteps(steps)[0].status).toBe('complete');
  });

  it('displayIndex marca activo en cualquiera de los source indexes', () => {
    const display = coalesceActorSteps([
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

/**
 * La fusión dejó de estar atada a las claves del traspaso: ahora agrupa cualquier tanda de pasos de
 * actores CONSECUTIVOS. Así el leasing (propietario + locatario) obtiene la misma pantalla sin una
 * segunda lista de claves que mantener, y un tipo nuevo la obtiene con solo parametrizarse.
 */
describe('wizard-actores-coalesce — tandas de actores en general', () => {
  const actor = (key: string, status: WizardStep['status'] = 'incomplete'): WizardStep => ({
    index: 0,
    key,
    label: key,
    status,
    reasons: [],
    sectionType: 'actor_form',
  });

  it('fusiona propietario+locatario del leasing en un solo paso «Actores»', () => {
    const steps = [
      step('consulta_vin', 'complete'),
      actor('comprador'),
      actor('locatario'),
      step('documentos', 'locked'),
    ];
    const display = coalesceActorSteps(steps);
    expect(display.map((d) => d.key)).toEqual(['consulta_vin', 'actores', 'documentos']);
    expect(display[1].label).toBe('Actores');
    expect(display[1].sourceIndexes).toEqual([1, 2]);
  });

  it('un paso de actores SOLO conserva su propio rótulo', () => {
    // Matrícula y los OTROS de un titular: no hay nada que comparar al lado.
    const steps = [step('consulta_vin', 'complete'), actor('comprador'), step('documentos')];
    const display = coalesceActorSteps(steps);
    expect(display.map((d) => d.key)).toEqual(['consulta_vin', 'comprador', 'documentos']);
  });

  it('avanza más allá de la tanda entera cuando toda ella quedó completa', () => {
    const steps = [
      step('consulta_vin', 'complete'),
      actor('comprador', 'complete'),
      actor('locatario', 'complete'),
      step('documentos', 'incomplete'),
    ];
    // Desde cualquiera de las dos partes se salta a documentos, no a la parte de al lado —que es la
    // otra mitad de la pantalla que el gestor acaba de llenar.
    expect(nextIndexAfterUnifiedActores(steps, 1)).toBe(3);
    expect(nextIndexAfterUnifiedActores(steps, 2)).toBe(3);
  });

  it('si falta una parte de la tanda, no la salta', () => {
    const steps = [
      step('consulta_vin', 'complete'),
      actor('comprador', 'complete'),
      actor('locatario', 'incomplete'),
      step('documentos', 'locked'),
    ];
    expect(nextIndexAfterUnifiedActores(steps, 1)).toBe(2);
  });
});
