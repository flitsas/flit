import { describe, expect, it } from 'vitest';
import {
  isTenantOwnDocument,
  normalizeNitDigits,
  OWNER_NOT_TENANT_MESSAGE,
} from '@/lib/tramites/vehicleOwnership';

/**
 * FEATURE 02 — HU #10726: validación en cliente de "solo vehículos propios" en el paso de consulta
 * del traspaso. El propietario debe ser la compañía jurídica del tenant (documento NIT == NIT del
 * tenant) para poder consultar el RUNT; en otro caso se bloquea con OWNER_NOT_TENANT_MESSAGE.
 */
describe('FEATURE 02 — propiedad del vehículo (consulta traspaso)', () => {
  const TENANT_NIT = '900123456-7';

  it('permite cuando el propietario es el NIT del tenant', () => {
    expect(isTenantOwnDocument('NIT', '900123456', TENANT_NIT)).toBe(true);
  });

  it('tolera el dígito de verificación y los separadores', () => {
    expect(isTenantOwnDocument('NIT', '900.123.456-7', '900123456')).toBe(true);
    expect(isTenantOwnDocument('NIT', '900123456', '900123456-7')).toBe(true);
  });

  it('bloquea cuando el NIT es de otra empresa', () => {
    expect(isTenantOwnDocument('NIT', '800999888', TENANT_NIT)).toBe(false);
  });

  it('bloquea cuando el propietario es persona natural (no NIT)', () => {
    expect(isTenantOwnDocument('CC', '900123456', TENANT_NIT)).toBe(false);
  });

  it('bloquea con documento o NIT del tenant vacíos', () => {
    expect(isTenantOwnDocument('NIT', '', TENANT_NIT)).toBe(false);
    expect(isTenantOwnDocument('NIT', '900123456', '')).toBe(false);
  });

  it('normalizeNitDigits deja solo dígitos', () => {
    expect(normalizeNitDigits('900.123.456-7')).toBe('9001234567');
    expect(normalizeNitDigits(null)).toBe('');
  });

  it('el mensaje de bloqueo menciona la compañía', () => {
    expect(OWNER_NOT_TENANT_MESSAGE).toMatch(/compañía/i);
  });
});
