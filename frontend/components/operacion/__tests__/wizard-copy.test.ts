// HU #10605 — copy del gate de envío por medida correctiva RNMC (R19).
import { describe, expect, it } from 'vitest';
import { blockerCopy, reasonCopy } from '../wizard-copy';

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
