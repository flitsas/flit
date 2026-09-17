import { describe, expect, it } from 'vitest';
import {
  formFromSettings,
  formToUpdate,
  diffSettings,
  DEFAULT_FINES_QUERY_SOURCE,
} from '@/components/admin/companies/settingsForm';
import type { TenantSettings } from '@/lib/api/types';

/**
 * FEATURE 02 — HU #10724 (FRONTEND): selector de fuente de comparendos.
 * Verifica el mapeo del formulario (lectura, serialización al PUT y diff para "Guardar todo").
 */
function baseSettings(overrides: Partial<TenantSettings> = {}): TenantSettings {
  return {
    tenantId: 't-1',
    switchesMatricula: {
      allowInitialRegistration: false,
      allowMiscNewVehicles: true,
      onlyOwnVehicles: false,
    },
    baulFirmasActivo: false,
    preasignacionPlacaActiva: false,
    enrutamientoSMTP: 'FLIT_SMTP',
    notificationTarget: 'RADICADOR',
    metodosRecaudo: [],
    ...overrides,
  };
}

describe('FEATURE 02 — fuente de comparendos (settingsForm)', () => {
  it('formFromSettings toma finesQuerySource del backend', () => {
    const form = formFromSettings(baseSettings({ finesQuerySource: 'internal' }));
    expect(form.finesQuerySource).toBe('internal');
  });

  it('formFromSettings cae al default external cuando el backend no lo envía', () => {
    const form = formFromSettings(baseSettings());
    expect(form.finesQuerySource).toBe(DEFAULT_FINES_QUERY_SOURCE);
    expect(form.finesQuerySource).toBe('external');
  });

  it('formToUpdate serializa finesQuerySource en el payload del PUT', () => {
    const form = formFromSettings(baseSettings({ finesQuerySource: 'internal' }));
    expect(formToUpdate(form).finesQuerySource).toBe('internal');
  });

  it('diffSettings detecta el cambio de fuente de comparendos para "Guardar todo"', () => {
    const initial = formFromSettings(baseSettings({ finesQuerySource: 'external' }));
    const current = { ...initial, finesQuerySource: 'internal' as const };

    const groups = diffSettings(initial, current);
    const items = groups.flatMap((g) => g.items);

    expect(items.some((i) => i.label === 'Fuente de comparendos')).toBe(true);
  });

  it('diffSettings no reporta cambio cuando la fuente no varía', () => {
    const initial = formFromSettings(baseSettings({ finesQuerySource: 'external' }));
    const groups = diffSettings(initial, { ...initial });
    const items = groups.flatMap((g) => g.items);
    expect(items.some((i) => i.label === 'Fuente de comparendos')).toBe(false);
  });
});
