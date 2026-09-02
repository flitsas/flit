import { describe, expect, it } from 'vitest';
import {
  DEFAULT_TRAMITES_VISIBLE_COLUMNS,
  TRAMITES_COLUMNS,
  buildTramitesGridLayout,
  buildTramitesColWidths,
} from '../tramites-table-columns';

/**
 * `buildTramitesGridLayout` es la única fuente del `gridTemplateColumns` de TramitesTable:
 * cabecera y filas la invocan con el mismo `visibleColumns`, así que fijar su comportamiento aquí
 * evita el desalineamiento cabecera/filas al ocultar columnas (regresión que motivó extraerla).
 */
describe('buildTramitesGridLayout', () => {
  it('el default compacto trae columnas esenciales + Acciones (sin pista de checkbox)', () => {
    const layout = buildTramitesGridLayout(DEFAULT_TRAMITES_VISIBLE_COLUMNS);
    const tracks = layout.gridTemplateColumns.split(/\s+/);
    expect(layout.includeSelectColumn).toBe(false);
    expect(tracks).toHaveLength(DEFAULT_TRAMITES_VISIBLE_COLUMNS.length + 1);
    expect(DEFAULT_TRAMITES_VISIBLE_COLUMNS.length).toBeLessThan(TRAMITES_COLUMNS.length);
  });

  it('incluye lo operativo y deja opcionales fuera del default', () => {
    expect(DEFAULT_TRAMITES_VISIBLE_COLUMNS).toEqual([
      'radicado',
      'placa',
      'propietario',
      'comprador',
      'tramite',
      'secretaria',
    ]);
    // Fuera del default porque su dato viaja apilado dentro de `radicado` (fechas), `placa`
    // (vehículo) y `tramite` (estado, paso). Siguen existiendo en el catálogo: activarlas desde
    // el selector MUEVE el dato a su columna, no lo duplica.
    for (const compuesta of [
      'vin',
      'vehiculo',
      'paso',
      'estado',
      'fechaCreacion',
      'fechaActualizacion',
    ]) {
      expect(DEFAULT_TRAMITES_VISIBLE_COLUMNS).not.toContain(compuesta);
      expect(TRAMITES_COLUMNS.map((c) => c.key)).toContain(compuesta);
    }
    // Fuera del default por decisión de producto, no por estar apiladas: se activan desde el
    // selector y la preferencia del gestor manda a partir de ahí.
    for (const opcional of ['gestor', 'fuente']) {
      expect(DEFAULT_TRAMITES_VISIBLE_COLUMNS).not.toContain(opcional);
      expect(TRAMITES_COLUMNS.map((c) => c.key)).toContain(opcional);
    }
    // La acreditación va DENTRO de la celda de su actor: ni una columna por parte ni una columna
    // única que las agrupe. `firmado` era esa columna única y dejó de existir.
    for (const inexistente of ['firmado', 'firmaVendedor', 'firmaComprador']) {
      expect(TRAMITES_COLUMNS.map((c) => c.key)).not.toContain(inexistente);
    }
    // VIN y marca/modelo se leen apilados dentro de `placa`, pero siguen teniendo su columna de
    // desglose: encenderla MUEVE el dato, no lo duplica.
    for (const desglose of ['vin', 'vehiculo']) {
      expect(TRAMITES_COLUMNS.find((c) => c.key === desglose)?.group).toBe('Desglose adicional');
    }
    // Y ninguna de las dos puede rotularse "Vehículo": ese rótulo es el de la columna fundida.
    expect(TRAMITES_COLUMNS.filter((c) => c.label === 'Vehículo').map((c) => c.key)).toEqual([
      'placa',
    ]);
  });

  it('el orden del catálogo es el orden real de la tabla: primero el listado, luego los desgloses', () => {
    const keys = TRAMITES_COLUMNS.map((c) => c.key);
    // Las 8 del listado van al frente, en el orden en que se leen de izquierda a derecha.
    expect(keys.slice(0, 8)).toEqual([
      'radicado',
      'placa',
      'propietario',
      'comprador',
      'tramite',
      'secretaria',
      'gestor',
      'fuente',
    ]);
    // Las visibles por defecto son un SUBCONJUNTO del grupo "Listado" —no todo el grupo— y se
    // leen en el mismo orden: es lo que garantiza que la cabecera no se reordene al ocultar una.
    const listado = keys.slice(0, 8);
    expect(listado.filter((k) => DEFAULT_TRAMITES_VISIBLE_COLUMNS.includes(k))).toEqual([
      ...DEFAULT_TRAMITES_VISIBLE_COLUMNS,
    ]);
  });

  it('cada columna declara su grupo para el desplegable, sin mezclar los dos bloques', () => {
    const grupos = TRAMITES_COLUMNS.map((c) => c.group);
    expect(grupos.every(Boolean)).toBe(true);
    // Una vez que empieza el segundo grupo no se vuelve al primero: si se intercalaran, la lista
    // del selector volvería a leerse revuelta.
    const primerDesglose = grupos.indexOf('Desglose adicional');
    expect(primerDesglose).toBeGreaterThan(0);
    expect(grupos.slice(primerDesglose).every((g) => g === 'Desglose adicional')).toBe(true);
  });

  it('reserva la pista del checkbox solo cuando includeSelectColumn es true', () => {
    const without = buildTramitesGridLayout(['radicado', 'placa']);
    const withSelect = buildTramitesGridLayout(['radicado', 'placa'], {
      includeSelectColumn: true,
    });
    expect(without.includeSelectColumn).toBe(false);
    expect(withSelect.includeSelectColumn).toBe(true);
    expect(withSelect.gridTemplateColumns.split(/\s+/).length).toBe(
      without.gridTemplateColumns.split(/\s+/).length + 1,
    );
    expect(withSelect.minWidthPx).toBeGreaterThan(without.minWidthPx);
  });

  it('al ocultar columnas, el grid trae exactamente visibles + Acciones', () => {
    const visible = ['radicado', 'placa', 'estado'];
    const layout = buildTramitesGridLayout(visible);
    const tracks = layout.gridTemplateColumns.split(/\s+/);
    expect(tracks).toHaveLength(visible.length + 1);
  });

  it('el orden de los tracks es SIEMPRE el canónico de TRAMITES_COLUMNS, sin importar el orden de entrada', () => {
    const canonical = buildTramitesGridLayout(['radicado', 'placa', 'estado']);
    const shuffled = buildTramitesGridLayout(['estado', 'radicado', 'placa']);
    expect(shuffled.gridTemplateColumns).toBe(canonical.gridTemplateColumns);
  });

  it('nunca produce un grid vacío aunque `visibleKeys` no incluya ninguna columna conocida', () => {
    const layout = buildTramitesGridLayout([]);
    const tracks = layout.gridTemplateColumns.split(/\s+/);
    // Cae a TODAS las columnas + Acciones — la tabla nunca queda con un grid inutilizable.
    expect(tracks).toHaveLength(TRAMITES_COLUMNS.length + 1);
  });

  it('el ancho mínimo baja cuando hay menos columnas visibles', () => {
    const full = buildTramitesGridLayout(TRAMITES_COLUMNS.map((c) => c.key));
    const partial = buildTramitesGridLayout(DEFAULT_TRAMITES_VISIBLE_COLUMNS);
    expect(partial.minWidthPx).toBeLessThan(full.minWidthPx);
  });
});

/**
 * `buildTramitesColWidths` es la contraparte de `buildTramitesGridLayout` para `<colgroup>`
 * (que no admite `fr`): reparte los mismos pesos de TRAMITES_COLUMNS como porcentaje, en el
 * mismo orden canónico. Las pistas fijas —Selección, las columnas `fixed` y Acciones— salen en su
 * propia unidad y el resto les cede su parte proporcional con `calc()`.
 */
describe('buildTramitesColWidths', () => {
  /**
   * Porcentaje de una pista flexible, siempre de la forma `calc(12.3456% - 40.50px - 0.2778rem)`
   * (el descuento en `rem` solo aparece con pista de Selección). `null` en una pista fija, que no
   * lleva porcentaje: `150px` o `2.25rem`.
   */
  function percentOf(width: string): number | null {
    const match = /(-?\d+(?:\.\d+)?)%/.exec(width);
    return match ? parseFloat(match[1]) : null;
  }

  /** Descuento en `px` de una pista `calc(...)`; 0 si no descuenta nada. */
  function pxDiscountOf(width: string): number {
    const match = /-\s*(\d+(?:\.\d+)?)px/.exec(width);
    return match ? parseFloat(match[1]) : 0;
  }

  /** Descuento en `rem` de una pista `calc(...)`; 0 si no descuenta nada. */
  function remDiscountOf(width: string): number {
    const match = /-\s*(\d+(?:\.\d+)?)rem/.exec(width);
    return match ? parseFloat(match[1]) : 0;
  }

  /** Suma el porcentaje de las pistas flexibles (las fijas no llevan y no cuentan). */
  function sumPercent(widths: string[]): number {
    return widths.reduce((sum, w) => sum + (percentOf(w) ?? 0), 0);
  }

  it('la suma de los porcentajes da 100% dentro de tolerancia', () => {
    const widths = buildTramitesColWidths(DEFAULT_TRAMITES_VISIBLE_COLUMNS);
    expect(sumPercent(widths)).toBeCloseTo(100, 1);
  });

  it('respeta el orden canónico de TRAMITES_COLUMNS sin importar el orden de entrada', () => {
    const canonical = buildTramitesColWidths(['radicado', 'placa', 'estado']);
    const shuffled = buildTramitesColWidths(['estado', 'radicado', 'placa']);
    expect(shuffled).toEqual(canonical);
  });

  it('la pista de Selección solo aparece cuando se pide, es fija (no en %) y el resto le cede su parte proporcional', () => {
    const without = buildTramitesColWidths(['radicado', 'placa']);
    const withSelect = buildTramitesColWidths(['radicado', 'placa'], {
      includeSelectColumn: true,
    });
    // radicado + placa + Acciones.
    expect(without).toHaveLength(3);
    // Selección + radicado + placa + Acciones.
    expect(withSelect).toHaveLength(4);
    expect(withSelect[0]).toBe('2.25rem');
    expect(withSelect[0].endsWith('%')).toBe(false);
    // El resto (sin la pista fija) sigue sumando 100%.
    expect(sumPercent(withSelect)).toBeCloseTo(100, 1);
    // Mismos pesos fr → mismos porcentajes que la versión sin Selección.
    expect(withSelect.slice(1).map(percentOf)).toEqual(without.map(percentOf));
    // La tabla va a `width: 100%`: si el 100% se repartiera íntegro entre estas pistas, la fila
    // desbordaría el ancho fijo de la pista de Selección. Por eso cada una descuenta su parte
    // proporcional y los descuentos suman EXACTAMENTE esa pista.
    // Solo las flexibles ceden: Acciones es una pista fija en px y no descuenta nada.
    const descuentos = withSelect.filter((w) => percentOf(w) !== null).map(remDiscountOf);
    expect(descuentos.every((d) => d > 0)).toBe(true);
    expect(descuentos.reduce((sum, d) => sum + d, 0)).toBeCloseTo(2.25, 2);
    // Sin pista de Selección no hay rem que ceder, pero sí el ancho de Acciones: las flexibles
    // siguen siendo `calc()` con descuento en px y ninguna descuenta rem.
    expect(without.map(remDiscountOf).every((d) => d === 0)).toBe(true);
  });

  it('las columnas de dato atómico son de ancho FIJO: no crecen y el resto les cede su parte', () => {
    // El default no trae ninguna fija, así que se mide sobre él MÁS `fuente` (etiqueta corta y
    // conocida), que es de las que no ganan nada con más espacio.
    const visibles = [...DEFAULT_TRAMITES_VISIBLE_COLUMNS, 'fuente'];
    const widths = buildTramitesColWidths(visibles);
    const orden = TRAMITES_COLUMNS.filter((c) => visibles.includes(c.key)).map((c) => c.key);
    const porClave = new Map(orden.map((key, index) => [key, widths[index]]));
    const defPorClave = new Map(TRAMITES_COLUMNS.map((c) => [c.key, c]));

    // Fuente sale en px exactos (su `minPx`), sin porcentaje: no absorbe el sobrante.
    expect(porClave.get('fuente')).toBe(`${defPorClave.get('fuente')!.minPx}px`);
    expect(percentOf(porClave.get('fuente')!)).toBeNull();
    // Acciones (estructural, no está en la preferencia) va última y también es fija.
    expect(widths[widths.length - 1]).toMatch(/^\d+px$/);
    // Las de texto sí reparten el 100% y descuentan, entre todas, exactamente lo que ocupa lo
    // fijo: la columna fija visible + Acciones.
    const flexibles = widths.filter((w) => percentOf(w) !== null);
    const fijoEsperado =
      defPorClave.get('fuente')!.minPx + parseFloat(widths[widths.length - 1]);
    expect(flexibles.reduce((sum, w) => sum + pxDiscountOf(w), 0)).toBeCloseTo(fijoEsperado, 0);
  });

  it('el peso de una columna flexible es su piso: reparte en proporción a lo que necesita', () => {
    // `secretaria` (piso 190) crece más que `gestor` (piso 160) en la misma proporción que sus
    // pisos. Es lo que garantiza que ninguna caiga por debajo de su mínimo al comprimir la tabla.
    const widths = buildTramitesColWidths(['secretaria', 'gestor']);
    const [secretaria, gestor] = widths.map((w) => percentOf(w));
    expect(secretaria).not.toBeNull();
    expect(gestor).not.toBeNull();
    expect(secretaria! / gestor!).toBeCloseTo(190 / 160, 3);
  });

  it('cae a TODAS las columnas si `visibleKeys` no casa con ninguna conocida (mismo fallback que buildTramitesGridLayout)', () => {
    const widths = buildTramitesColWidths([]);
    expect(widths).toHaveLength(TRAMITES_COLUMNS.length + 1);
    expect(sumPercent(widths)).toBeCloseTo(100, 1);
  });
});
