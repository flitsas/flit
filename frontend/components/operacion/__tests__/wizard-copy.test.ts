// HU #10598 (R10) + HU #10605 (R19) — copy de gates de prenda y medida correctiva RNMC.
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
});

describe('wizard-copy — medida correctiva RNMC (HU #10605)', () => {
  it('traduce rnmc_medida_bloquea_envio como bloqueo de envío', () => {
    expect(blockerCopy('rnmc_medida_bloquea_envio')).toMatch(/paz y salvo RNMC/i);
  });

  it('traduce rnmc_medida_bloquea_envio como razón de paso', () => {
    expect(reasonCopy('rnmc_medida_bloquea_envio')).toMatch(/paz y salvo RNMC/i);
  });

  it('humaniza códigos desconocidos como fallback legible', () => {
    expect(blockerCopy('codigo_raro')).toBe('Codigo Raro');
  });
});
