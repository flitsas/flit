import { describe, expect, it } from 'vitest';
import {
  DEFAULT_TRAMITES_VISIBLE_COLUMNS,
  TRAMITES_COLUMNS,
  buildTramitesGridLayout,
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
      'tramite',
      'propietario',
      'comprador',
      'paso',
      'estado',
      'fechaCreacion',
      'secretaria',
    ]);
    for (const optional of ['vin', 'vehiculo', 'gestor', 'fuente', 'firmaVendedor', 'firmaComprador']) {
      expect(DEFAULT_TRAMITES_VISIBLE_COLUMNS).not.toContain(optional);
    }
    expect(TRAMITES_COLUMNS.map((c) => c.key)).not.toContain('firmaVendedor');
    expect(TRAMITES_COLUMNS.map((c) => c.key)).not.toContain('firmaComprador');
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
