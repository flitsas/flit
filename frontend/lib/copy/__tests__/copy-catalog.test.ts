// HU #12693 (Feature #12689, épica #12551) — fuente única de copy canónico OT ↔ Gestor.
import { describe, expect, it } from 'vitest';
import { ESTADO_LABELS, estadoLabel } from '@/lib/tramites/estados';
import {
  COPY,
  COPY_NA_KEYS,
  copyEstado,
  copyForOtAndGestor,
  copyLabel,
  isNaCopyKey,
  isPublishedCopyKey,
} from '../copy-catalog';

describe('catálogo de copy canónico (HU #12693)', () => {
  it('AC1 — OT y gestor leen el mismo símbolo para cada clave publicada', () => {
    const keys = Object.keys(COPY) as (keyof typeof COPY)[];
    expect(keys.length).toBeGreaterThan(0);

    for (const key of keys) {
      const { ot, gestor } = copyForOtAndGestor(key);
      expect(ot).toBe(COPY[key]);
      expect(gestor).toBe(COPY[key]);
      expect(ot).toBe(gestor);
      expect(copyLabel(key)).toBe(COPY[key]);
    }

    expect(copyLabel('A01')).toBe('Vendedor');
    expect(copyForOtAndGestor('A01')).toEqual({ ot: 'Vendedor', gestor: 'Vendedor' });
    expect(copyLabel('A05')).toBe('Organismo de tránsito');
    expect(copyLabel('A12')).toBe('Ver consolidado');
  });

  it('AC2 — una fila N/A o sin Ganador no se publica como texto de UI', () => {
    for (const key of COPY_NA_KEYS) {
      expect(isNaCopyKey(key)).toBe(true);
      expect(isPublishedCopyKey(key)).toBe(false);
      expect(copyLabel(key)).toBeUndefined();
      expect(Object.prototype.hasOwnProperty.call(COPY, key)).toBe(false);
    }

    expect(copyLabel('A11')).toBeUndefined();
    expect(copyLabel('A14')).toBeUndefined();
    expect(copyLabel('clave-inventada')).toBeUndefined();
    expect(copyLabel('')).toBeUndefined();
  });

  it('AC3 — estados de trámite reutilizan estadoLabel / ESTADO_LABELS; no duplican el catálogo B', () => {
    expect(copyEstado).toBe(estadoLabel);
    expect(copyEstado('borrador')).toBe('Borrador');
    expect(copyEstado('aprobado')).toBe('Aprobado');
    expect(copyEstado('rechazado')).toBe('Rechazado');
    expect(copyEstado('entregado')).toBe(ESTADO_LABELS.entregado);

    const catalogSource = JSON.stringify(COPY);
    expect(catalogSource).not.toMatch(/"Borrador"/);
    expect(catalogSource).not.toMatch(/"Preparado"/);
    expect(catalogSource).not.toMatch(/"Preasignación"/);
    expect(catalogSource).not.toMatch(/"En subsanación"/);
  });
});
