// HU #11018 — formato de fecha único de negocio: AÑO/MES/DÍA, sin hora.
import { describe, expect, it } from 'vitest';
import { formatFecha, formatFechaHora } from '../date';

describe('formatFecha — fechas de negocio (HU #11018)', () => {
  it('devuelve año/mes/día sin hora', () => {
    expect(formatFecha('2026-07-28T15:42:13Z')).toBe('2026/07/28');
  });

  it('rellena mes y día a dos dígitos', () => {
    expect(formatFecha('2026-01-05T12:00:00Z')).toBe('2026/01/05');
  });

  it('usa el día calendario de Colombia, no UTC', () => {
    // 2026-07-29T02:00Z es todavía el 28 en Bogotá (UTC-5): el negocio habla de ese día.
    expect(formatFecha('2026-07-29T02:00:00Z')).toBe('2026/07/28');
  });

  it('acepta Date además de ISO', () => {
    expect(formatFecha(new Date('2026-12-31T12:00:00Z'))).toBe('2026/12/31');
  });

  it.each([null, undefined, '', 'no-es-fecha'])('degrada a fallback con %s', (valor) => {
    expect(formatFecha(valor as string | null | undefined)).toBe('—');
  });

  it('respeta el fallback indicado', () => {
    expect(formatFecha(null, 'sin fecha')).toBe('sin fecha');
  });
});

describe('formatFechaHora — bitácoras técnicas', () => {
  it('conserva la hora, que en trazas es información de diagnóstico', () => {
    expect(formatFechaHora('2026-07-28T15:42:13Z')).toBe('2026/07/28 10:42');
  });

  it('degrada igual que la fecha de negocio', () => {
    expect(formatFechaHora('no-es-fecha')).toBe('—');
  });
});
