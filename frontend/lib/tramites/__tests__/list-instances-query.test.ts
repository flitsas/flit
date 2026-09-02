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

  // Estado, familia, organismo y tipo dejaron de filtrarse en el cliente: si no viajan en el query
  // string, el backend devuelve el universo entero y la tabla vuelve a recortar una página.
  it('serializa los filtros que resuelve el servidor (estado, familia, organismo, tipo)', () => {
    const q = buildListInstancesSearchParams({
      estado: 'borrador,preparado',
      modalidad: 'TRASPASO',
      organismoTransito: 'bogota',
      tipoCodigo: 'TRASPASO_STANDARD',
    });
    // Varios estados viajan separados por coma: "todo lo que no está cerrado" son varios a la vez.
    expect(q.get('estado')).toBe('borrador,preparado');
    expect(q.get('modalidad')).toBe('TRASPASO');
    expect(q.get('organismoTransito')).toBe('bogota');
    expect(q.get('tipoCodigo')).toBe('TRASPASO_STANDARD');
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
    // Se compara el CONJUNTO, no el orden: el orden del catálogo es la disposición de la tabla
    // y cambia cuando se reorganizan las columnas; qué columnas son ordenables, no.
    expect([...sortable].sort()).toEqual(
      ['comprador', 'fechaActualizacion', 'fechaCreacion', 'gestor', 'placa', 'vin'].sort(),
    );
  });
});
