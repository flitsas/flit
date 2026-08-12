// HU #10598 (R10) — copy del gate de prenda. HU #10697 (R19) — RNMC informativo (ya no bloquea).
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
    expect(blockerCopy('prenda_decision_requerida')).toMatch(/decisión de prenda/i);
  });

  // El mismo código lo emite el override del organismo, que aplica a matrícula inicial: ahí no hay
  // gravamen alguno, así que el bloqueo no puede atribuirse a uno.
  it('no atribuye el bloqueo a gravámenes, que no siempre existen', () => {
    expect(blockerCopy('prenda_decision_requerida')).not.toMatch(/gravámenes/i);
  });
});

describe('wizard-copy — medida correctiva RNMC informativa (HU #10697)', () => {
  it('traduce rnmc_medida_pendiente como razón informativa (no bloqueo)', () => {
    expect(reasonCopy('rnmc_medida_pendiente')).toMatch(/informativa/i);
    expect(reasonCopy('rnmc_medida_pendiente')).toMatch(/no bloquea/i);
  });

  it('RNMC ya no es un bloqueo de envío: no hay copy de bloqueo específica', () => {
    // El backend ya no emite rnmc_medida_bloquea_envio; si llegara, cae al fallback humanizado
    // (no a una copy que prometa un bloqueo inexistente).
    expect(blockerCopy('rnmc_medida_bloquea_envio')).toBe('Rnmc Medida Bloquea Envio');
  });

  it('humaniza códigos desconocidos como fallback legible', () => {
    expect(blockerCopy('codigo_raro')).toBe('Codigo Raro');
  });
});
