import { describe, it, expect } from 'vitest';
import { formatCOP, digitsOnly, groupThousands } from '../currency';

describe('formatCOP', () => {
  it('formatea pesos como moneda COP sin decimales', () => {
    // El separador de miles es NBSP/punto según el runtime; comparamos por dígitos.
    expect(digitsOnly(formatCOP(50_000_000))).toBe('50000000');
    expect(formatCOP(50_000_000)).toContain('$');
    expect(formatCOP(50_000_000)).toContain('50.000.000');
  });

  it('devuelve cadena vacía para null/undefined/NaN', () => {
    expect(formatCOP(null)).toBe('');
    expect(formatCOP(undefined)).toBe('');
    expect(formatCOP(Number.NaN)).toBe('');
  });

  it('formatea el cero como $0', () => {
    expect(formatCOP(0)).toContain('0');
  });
});

describe('digitsOnly', () => {
  it('descarta todo lo que no sea dígito', () => {
    expect(digitsOnly('$ 50.000.000')).toBe('50000000');
    expect(digitsOnly('abc1d2e3')).toBe('123');
    expect(digitsOnly('')).toBe('');
  });
});

describe('groupThousands', () => {
  it('agrupa dígitos con separador de miles', () => {
    expect(groupThousands('50000000')).toBe('50.000.000');
    expect(groupThousands('1000')).toBe('1.000');
    expect(groupThousands('999')).toBe('999');
  });

  it('normaliza entrada con símbolos y ceros a la izquierda', () => {
    expect(groupThousands('$1.234.567')).toBe('1.234.567');
    expect(groupThousands('007')).toBe('7');
    expect(groupThousands('0')).toBe('0');
  });

  it('cadena vacía cuando no hay dígitos', () => {
    expect(groupThousands('')).toBe('');
    expect(groupThousands('abc')).toBe('');
  });
});
