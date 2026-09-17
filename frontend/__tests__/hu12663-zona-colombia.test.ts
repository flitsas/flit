// HU #12663 — toda fecha con hora se muestra en la zona de Colombia, no en la del navegador.
import { readFileSync, readdirSync, statSync } from 'node:fs';
import path from 'node:path';
import { describe, expect, it } from 'vitest';
import { ZONA_COLOMBIA } from '../lib/format/date';

const RAIZ = path.resolve(__dirname, '..');

/**
 * Archivos que legítimamente NO fijan la zona de Colombia.
 *
 * `signatureVaultDisplay.ts` formatea una VIGENCIA, que es una fecha de calendario y no un
 * instante: la recibe como `AAAA-MM-DD`, la interpreta en UTC y la muestra en UTC, de modo que el
 * día no se corre. Convertirla a Colombia la retrasaría una jornada — el defecto de la HU #11194,
 * que la Épica #12552 recoge como excepción RN-08.
 */
const EXENTOS = new Set(['components/admin/companies/signature-vault/signatureVaultDisplay.ts']);

const IGNORAR_DIR = new Set(['node_modules', '.next', 'coverage', '__tests__', '.turbo']);

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

/** Texto del objeto de opciones (o cadena vacía) que sigue a una llamada en `desde`. */
function argumentos(texto: string, desde: number): string {
  let nivel = 0;
  for (let i = desde; i < texto.length; i += 1) {
    const c = texto[i];
    if (c === '(') nivel += 1;
    else if (c === ')') {
      nivel -= 1;
      if (nivel === 0) return texto.slice(desde + 1, i);
    } else if (c === '"' || c === "'" || c === '`') {
      for (i += 1; i < texto.length && texto[i] !== c; i += 1) {
        if (texto[i] === '\\') i += 1;
      }
    }
  }
  return '';
}

const CLAVE_FECHA =
  /\b(dateStyle|timeStyle|year|month|day|hour|minute|second|weekday|era|hour12)\s*:/;

/**
 * Llamadas que con certeza formatean una FECHA:
 * `Intl.DateTimeFormat`, `toLocaleDateString` y `toLocaleTimeString` siempre lo hacen;
 * `toLocaleString` solo se cuenta cuando lleva opciones de fecha o cuando el receptor es
 * literalmente un `new Date(...)`, porque también se usa para miles y decimales.
 */
function infracciones(texto: string): string[] {
  const halladas: string[] = [];
  const patron =
    /(new Intl\.DateTimeFormat|new Date\([^)]*\)\.toLocaleString|\.toLocaleDateString|\.toLocaleTimeString|\.toLocaleString)\s*\(/g;

  for (let m = patron.exec(texto); m !== null; m = patron.exec(texto)) {
    const abre = m.index + m[0].length - 1;
    const args = argumentos(texto, abre);
    if (args.includes('timeZone')) continue;

    const seguro =
      m[1] === '.toLocaleString' ? CLAVE_FECHA.test(args) : true;
    if (seguro) halladas.push(`${m[1]}(${args.replace(/\s+/g, ' ').slice(0, 60)})`);
  }
  return halladas;
}

describe('ZONA_COLOMBIA', () => {
  it('es el identificador de la zona de Colombia', () => {
    expect(ZONA_COLOMBIA).toBe('America/Bogota');
  });

  it('fijarla cambia lo que se muestra frente a otra zona', () => {
    // 02:00Z del día 18 es todavía el 17 a las 21:00 en Colombia. Si la zona no se fija, el
    // resultado depende de la máquina; este par demuestra que fijarla tiene efecto real.
    const instante = new Date('2026-09-18T02:00:00Z');
    const opciones: Intl.DateTimeFormatOptions = { dateStyle: 'short', timeStyle: 'short' };

    const enColombia = instante.toLocaleString('es-CO', { ...opciones, timeZone: ZONA_COLOMBIA });
    const enTokio = instante.toLocaleString('es-CO', { ...opciones, timeZone: 'Asia/Tokyo' });

    // `dateStyle: short` en es-CO abrevia el año a dos dígitos: 17/09/26.
    expect(enColombia).toContain('17/09/26');
    expect(enTokio).toContain('18/09/26');
    expect(enColombia).not.toBe(enTokio);
  });
});

describe('guardián: ningún punto formatea una fecha sin fijar la zona', () => {
  // La suite corre con TZ=America/Bogota (vitest.config.ts), así que una prueba de
  // comportamiento NO puede cazar este defecto: el proceso ya está en la zona correcta y el
  // formateo sin fijar sale bien por casualidad. Por eso el guardián mira el código.
  it('no queda ninguna llamada sin timeZone', () => {
    const malos = fuentes()
      .map((abs) => ({ rel: path.relative(RAIZ, abs).replace(/\\/g, '/'), abs }))
      .filter(({ rel }) => !EXENTOS.has(rel))
      .map(({ rel, abs }) => ({ rel, hallazgos: infracciones(readFileSync(abs, 'utf8')) }))
      .filter(({ hallazgos }) => hallazgos.length > 0)
      .map(({ rel, hallazgos }) => `${rel}: ${hallazgos.join(' | ')}`);

    expect(malos).toEqual([]);
  });

  it('el guardián detecta de verdad una llamada sin zona', () => {
    // Sin esta comprobación, un fallo del detector se leería como «todo en orden».
    expect(infracciones('new Intl.DateTimeFormat("es-CO", { dateStyle: "short" })')).toHaveLength(1);
    expect(infracciones('d.toLocaleDateString("es-CO")')).toHaveLength(1);
    expect(
      infracciones('new Intl.DateTimeFormat("es-CO", { dateStyle: "short", timeZone: Z })'),
    ).toHaveLength(0);
    // Un formateo de número no es una infracción.
    expect(infracciones('total.toLocaleString("es-CO")')).toHaveLength(0);
  });
});
