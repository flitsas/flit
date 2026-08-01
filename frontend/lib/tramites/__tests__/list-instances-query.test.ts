import { describe, expect, it } from 'vitest';
import {
  buildListInstancesSearchParams,
  dayEndIso,
  dayStartIso,
  hasListInstancesServerQuery,
} from '../list-instances-query';
import {
  TRAMITES_COLUMNS,
  tramitesColumnToSortBy,
} from '../tramites-table-columns';

describe('list-instances-query', () => {
  it('expande fechas YYYY-MM-DD a inicio/fin de día ISO', () => {
    expect(dayStartIso('2026-08-01')).toBe('2026-08-01T00:00:00.000Z');
    expect(dayEndIso('2026-08-01')).toBe('2026-08-01T23:59:59.999Z');
  });

  it('arma query con filtros de placa, actores, gestor, firmado y fechas', () => {
    const q = buildListInstancesSearchParams({
      placa: 'ABC123',
      vendedor: 'Pérez',
      comprador: 'García',
      gestor: 'Ana',
      firmado: true,
      createdFrom: '2026-01-01',
      createdTo: '2026-01-31',
      updatedFrom: '2026-02-01',
      updatedTo: '2026-02-28',
      sortBy: 'placa',
      sortDir: 'asc',
      take: 200,
      skip: 0,
    });
    expect(q.get('placa')).toBe('ABC123');
    expect(q.get('vendedor')).toBe('Pérez');
    expect(q.get('comprador')).toBe('García');
    expect(q.get('gestor')).toBe('Ana');
    expect(q.get('firmado')).toBe('true');
    expect(q.get('createdFrom')).toBe('2026-01-01T00:00:00.000Z');
    expect(q.get('createdTo')).toBe('2026-01-31T23:59:59.999Z');
    expect(q.get('updatedFrom')).toBe('2026-02-01T00:00:00.000Z');
    expect(q.get('updatedTo')).toBe('2026-02-28T23:59:59.999Z');
    expect(q.get('sortBy')).toBe('placa');
    expect(q.get('sortDir')).toBe('asc');
    expect(q.get('take')).toBe('200');
    expect(q.get('skip')).toBe('0');
  });

  it('omite claves vacías', () => {
    const q = buildListInstancesSearchParams({ placa: '  ', sortBy: '' });
    expect(q.toString()).toBe('');
    expect(hasListInstancesServerQuery({})).toBe(false);
  });
});

describe('tramitesColumnToSortBy', () => {
  it('mapea columnas UI a sortBy del API', () => {
    expect(tramitesColumnToSortBy('fechaCreacion')).toBe('createdAt');
    expect(tramitesColumnToSortBy('fechaActualizacion')).toBe('updatedAt');
    expect(tramitesColumnToSortBy('comprador')).toBe('comprador');
    expect(tramitesColumnToSortBy('gestor')).toBe('gestor');
    expect(tramitesColumnToSortBy('placa')).toBe('placa');
    expect(tramitesColumnToSortBy('vin')).toBe('vin');
  });

  it('marca ordenables las columnas pedidas por negocio', () => {
    const sortable = TRAMITES_COLUMNS.filter((c) => c.sortable).map((c) => c.key);
    expect(sortable).toEqual([
      'vin',
      'placa',
      'comprador',
      'fechaCreacion',
      'fechaActualizacion',
      'gestor',
    ]);
  });
});
