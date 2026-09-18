// Épica #12552 — formato único del producto.
//
// Sustituye el criterio de la HU #11018 (`AÑO/MES/DÍA` sin hora en tablas y documentos de
// negocio) por decisión del negocio: un instante lleva hora, y el orden es día/mes/año.
import { describe, expect, it } from 'vitest';
import { formatFechaCalendario, formatFechaHora } from '../date';

describe('formatFechaHora — instantes', () => {
  it('muestra día/mes/año y la hora, en hora de Colombia', () => {
    // 15:42Z son las 10:42 en Bogotá (UTC−5).
    expect(formatFechaHora('2026-07-28T15:42:13Z')).toBe('28/07/2026 10:42');
  });

  it('no muestra segundos, por decisión del negocio', () => {
    expect(formatFechaHora('2026-07-28T15:42:13Z')).not.toMatch(/:\d{2}:\d{2}$/);
  });

  it('rellena a dos dígitos y usa reloj de 24 horas', () => {
    expect(formatFechaHora('2026-01-05T12:00:00Z')).toBe('05/01/2026 07:00');
    expect(formatFechaHora('2026-01-05T23:30:00Z')).toBe('05/01/2026 18:30');
    // 02:00Z del día 6 es todavía el día 5 a las 21:00 en Bogotá.
    expect(formatFechaHora('2026-01-06T02:00:00Z')).toBe('05/01/2026 21:00');
  });

  it('acepta Date además de ISO', () => {
    expect(formatFechaHora(new Date('2026-12-31T12:00:00Z'))).toBe('31/12/2026 07:00');
  });

  it.each([null, undefined, '', 'no-es-fecha'])('degrada a fallback con %s', (valor) => {
    expect(formatFechaHora(valor as string | null | undefined)).toBe('—');
  });

  it('respeta el fallback indicado', () => {
    expect(formatFechaHora(null, 'sin fecha')).toBe('sin fecha');
  });
});

describe('formatFechaCalendario — fechas sin hora (RN-08)', () => {
  it('muestra día/mes/año sin hora', () => {
    expect(formatFechaCalendario('2026-07-28')).toBe('28/07/2026');
  });

  it('rellena mes y día a dos dígitos', () => {
    expect(formatFechaCalendario('2026-01-05')).toBe('05/01/2026');
  });

  it('nunca añade una hora que el dato no tiene', () => {
    expect(formatFechaCalendario('2026-07-28')).not.toMatch(/\d{2}:\d{2}/);
  });

  it.each([null, undefined, '', 'no-es-fecha', '2026-13-45'])(
    'degrada a fallback con %s',
    (valor) => {
      expect(formatFechaCalendario(valor as string | null | undefined)).toBe('—');
    },
  );

  it('respeta el fallback indicado', () => {
    expect(formatFechaCalendario(null, 'sin fecha')).toBe('sin fecha');
  });
});

describe('las dos funciones no se confunden', () => {
  it('un instante formateado como calendario pierde la hora, no el día', () => {
    // 02:00Z del 1 de julio son las 21:00 del 30 de junio en Bogotá: al ser un INSTANTE sí se
    // convierte, y el día calendario de Colombia es el 30.
    expect(formatFechaCalendario('2026-07-01T02:00:00Z')).toBe('30/06/2026');
    expect(formatFechaHora('2026-07-01T02:00:00Z')).toBe('30/06/2026 21:00');
  });

  it('una fecha de calendario pasada a formatFechaHora sale sin hora y sin correrse', () => {
    // Es una llamada equivocada, pero inventarle medianoche y convertirla la retrasaría un día
    // (el defecto de la HU #11194). Mejor una fecha correcta y más corta que una incorrecta.
    expect(formatFechaHora('2026-07-01')).toBe('01/07/2026');
  });
});
