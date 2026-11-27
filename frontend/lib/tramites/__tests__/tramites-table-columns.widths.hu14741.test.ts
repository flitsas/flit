import { describe, expect, it } from 'vitest';
import {
  TRAMITES_COLUMNS,
  buildTramitesColWidths,
  buildTramitesGridLayout,
} from '../tramites-table-columns';

// HU #14741 — los anchos propios entran al reparto sin romper que la tabla cubra su contenedor.

const KEYS = ['radicado', 'placa', 'tramite'];
const def = (k: string) => TRAMITES_COLUMNS.find((c) => c.key === k)!;

describe('tramites-table-columns — anchos propios (HU #14741)', () => {
  it('una columna con ancho propio es de ancho exacto en px', () => {
    const w = buildTramitesColWidths(KEYS, { widths: { placa: 333 } });
    expect(w[1]).toBe('333px');
    // Las demás siguen flexibles (calc) y Acciones fija.
    expect(w[0]).toMatch(/^calc\(/);
    expect(w[w.length - 1]).toBe('150px');
  });

  it('sin widths el resultado es idéntico al de siempre', () => {
    expect(buildTramitesColWidths(KEYS, { widths: {} })).toEqual(buildTramitesColWidths(KEYS));
    expect(buildTramitesGridLayout(KEYS, { widths: {} })).toEqual(buildTramitesGridLayout(KEYS));
  });

  it('AC3: nunca baja del mínimo de la columna', () => {
    const minimo = def('placa').minPx;
    const w = buildTramitesColWidths(KEYS, { widths: { placa: 10 } });
    expect(w[1]).toBe(`${minimo}px`);
  });

  it('el ancho guardado de una columna inexistente se ignora', () => {
    expect(buildTramitesColWidths(KEYS, { widths: { fantasma: 999 } })).toEqual(
      buildTramitesColWidths(KEYS),
    );
  });

  it('el mínimo de la tabla suma el ancho propio y la grilla lo usa como px', () => {
    const base = buildTramitesGridLayout(KEYS);
    const ajustado = buildTramitesGridLayout(KEYS, { widths: { placa: def('placa').minPx + 100 } });
    expect(ajustado.minWidthPx).toBe(base.minWidthPx + 100);
    expect(ajustado.gridTemplateColumns).toContain(`${def('placa').minPx + 100}px`);
  });

  it('si todas las columnas tienen ancho propio no hay división por cero', () => {
    const widths = Object.fromEntries(KEYS.map((k) => [k, def(k).minPx + 20]));
    const w = buildTramitesColWidths(KEYS, { widths });
    expect(w.every((x) => !x.includes('NaN') && !x.includes('Infinity'))).toBe(true);
  });
});
