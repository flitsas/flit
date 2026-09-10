import { describe, expect, it } from 'vitest';
import { formatDateOnly } from '../date-only';

describe('formatDateOnly', () => {
  it('deja DD/MM/YYYY sin horas', () => {
    expect(formatDateOnly('23/01/2027')).toBe('23/01/2027');
  });

  it('convierte ISO con hora/offset a DD/MM/YYYY', () => {
    expect(formatDateOnly('2027-01-23T00:00:00.000-05:00')).toBe('23/01/2027');
  });

  it('convierte YYYY-MM-DD', () => {
    expect(formatDateOnly('2027-01-23')).toBe('23/01/2027');
  });

  it('vacío / null → cadena vacía', () => {
    expect(formatDateOnly('')).toBe('');
    expect(formatDateOnly(null)).toBe('');
    expect(formatDateOnly(undefined)).toBe('');
  });
});
