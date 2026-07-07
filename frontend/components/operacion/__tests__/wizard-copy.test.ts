// HU #10598 (R10) — copy del gate de prenda del traspaso.
import { describe, expect, it } from 'vitest';
import { blockerCopy, reasonCopy } from '../wizard-copy';

describe('wizard-copy — gate de prenda (HU #10598)', () => {
  it('traduce prenda_decision_requerida como razón de paso', () => {
    expect(reasonCopy('prenda_decision_requerida')).toMatch(/decisión de prenda/i);
  });

  it('traduce prenda_documento_requerido como bloqueo', () => {
    expect(blockerCopy('prenda_documento_requerido')).toMatch(/documento de soporte/i);
  });

  it('traduce prenda_decision_requerida como bloqueo de radicación', () => {
    expect(blockerCopy('prenda_decision_requerida')).toMatch(/gravámenes/i);
  });

  it('humaniza códigos desconocidos como fallback legible', () => {
    expect(blockerCopy('codigo_raro')).toBe('Codigo Raro');
  });
});
