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
      'vin',
      'placa',
      'propietario',
      'comprador',
      'firmado',
      'tramite',
      'secretaria',
      'gestor',
      'fuente',
    ]);
    // Fuera del default porque su dato viaja apilado dentro de `radicado` (fechas), `placa`
    // (vehículo) y `tramite` (estado, paso). Siguen existiendo en el catálogo: activarlas desde
    // el selector MUEVE el dato a su columna, no lo duplica.
    for (const compuesta of ['vehiculo', 'paso', 'estado', 'fechaCreacion', 'fechaActualizacion']) {
      expect(DEFAULT_TRAMITES_VISIBLE_COLUMNS).not.toContain(compuesta);
      expect(TRAMITES_COLUMNS.map((c) => c.key)).toContain(compuesta);
    }
    // La acreditación por parte va dentro de la celda del actor, nunca como columna propia.
    expect(TRAMITES_COLUMNS.map((c) => c.key)).not.toContain('firmaVendedor');
    expect(TRAMITES_COLUMNS.map((c) => c.key)).not.toContain('firmaComprador');
  });

  it('el orden del catálogo es el orden real de la tabla: primero el listado, luego los desgloses', () => {
    const keys = TRAMITES_COLUMNS.map((c) => c.key);
    // Las 10 del listado van al frente, en el orden en que se leen de izquierda a derecha.
    expect(keys.slice(0, 10)).toEqual([
      'radicado',
      'vin',
      'placa',
      'propietario',
      'comprador',
      'firmado',
      'tramite',
      'secretaria',
      'gestor',
      'fuente',
    ]);
    // Y son exactamente las visibles por defecto: la tabla en reposo == el grupo "Listado".
    expect(keys.slice(0, 10)).toEqual([...DEFAULT_TRAMITES_VISIBLE_COLUMNS]);
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
