// HU #12664 — un solo formateador de fechas en el frontend.
//
// El formato lo fijan `formatFechaHora` (instantes) y `formatFechaCalendario` (fechas de
// calendario, RN-08) en `lib/format/date`. Cualquier punto que arme su propio `Intl` vuelve a
// abrir la puerta a que conviva otro formato, que es de lo que venimos: antes de esta Épica había
// cuatro (`dateStyle` medium/short × `timeStyle` medium/short) repartidos por la app.
import { readFileSync, readdirSync, statSync } from 'node:fs';
import path from 'node:path';
import { describe, expect, it } from 'vitest';
import { formatFechaCalendario, formatFechaHora } from '../lib/format/date';

const RAIZ = path.resolve(__dirname, '..');
const IGNORAR_DIR = new Set(['node_modules', '.next', 'coverage', '__tests__', '.turbo']);

/**
 * Los únicos que pueden formatear fechas por su cuenta, y por qué.
 *
 * Los dos indicadores de «última actualización» son un RELOJ DE FRESCURA, no un campo de fecha:
 * dicen hace cuánto se refrescó una lista que se refresca cada pocos segundos. La fecha ahí sería
 * siempre hoy, y en `Validaciones` el segundo es justamente la información útil. No entran en el
 * alcance del formato estándar, que habla de campos de fecha.
 */
const EXENTOS = new Map([
  ['lib/format/date.ts', 'es el formateador'],
  ['lib/xlsx.ts', 'arma una fecha serial de Excel a partir de las partes, no un texto'],
  ['components/atom/modules/_reportes/LiveNowPanel.tsx', 'reloj de frescura: solo la hora'],
  ['components/atom/modules/Validaciones.tsx', 'reloj de frescura del auto-refresco (LiveIndicator)'],
]);

function fuentes(dir = RAIZ): string[] {
  const salida: string[] = [];
  for (const entrada of readdirSync(dir)) {
    if (IGNORAR_DIR.has(entrada)) continue;
    const abs = path.join(dir, entrada);
    if (statSync(abs).isDirectory()) {
      salida.push(...fuentes(abs));
      continue;
    }
    if (!/\.(ts|tsx)$/.test(entrada) || /\.test\.(ts|tsx)$/.test(entrada)) continue;
    salida.push(abs);
  }
  return salida;
}

const PROPIO =
  /new Intl\.DateTimeFormat\s*\(|\.toLocaleDateString\s*\(|\.toLocaleTimeString\s*\(/g;

function formateadoresPropios(texto: string): string[] {
  return (texto.match(PROPIO) ?? []).map((m) => m.replace(/\s*\($/, ''));
}

describe('guardián: el formato de fecha lo fija un solo sitio', () => {
  it('ningún componente arma su propio formateador de fechas', () => {
    const malos = fuentes()
      .map((abs) => ({ rel: path.relative(RAIZ, abs).replace(/\\/g, '/'), abs }))
      .filter(({ rel }) => !EXENTOS.has(rel))
      .map(({ rel, abs }) => ({
        rel,
        hallazgos: formateadoresPropios(readFileSync(abs, 'utf8')),
      }))
      .filter(({ hallazgos }) => hallazgos.length > 0)
      .map(({ rel, hallazgos }) => `${rel}: ${hallazgos.join(', ')}`);

    expect(malos).toEqual([]);
  });

  it('el guardián detecta de verdad un formateador propio', () => {
    // Sin esto, un detector roto se leería como «todo en orden».
    expect(formateadoresPropios('new Intl.DateTimeFormat("es-CO", {})')).toHaveLength(1);
    expect(formateadoresPropios('d.toLocaleDateString("es-CO")')).toHaveLength(1);
    expect(formateadoresPropios('formatFechaHora(d)')).toHaveLength(0);
  });

  it('las exenciones siguen existiendo: una lista obsoleta no vigila nada', () => {
    for (const rel of EXENTOS.keys()) {
      expect(() => statSync(path.join(RAIZ, rel)), `${rel} ya no existe`).not.toThrow();
    }
  });
});

describe('el formato que fija el único formateador', () => {
  it('un instante es DD/MM/YYYY HH:mm en hora de Colombia', () => {
    expect(formatFechaHora('2026-09-17T19:05:00Z')).toBe('17/09/2026 14:05');
  });

  it('una fecha de calendario es DD/MM/YYYY, sin hora', () => {
    expect(formatFechaCalendario('2026-09-17')).toBe('17/09/2026');
  });
});
