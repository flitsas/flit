import { describe, expect, it } from 'vitest';
import {
  buildListInstancesSearchParams,
  dayEndIso,
  dayStartIso,
  hasListInstancesServerQuery,
} from '../list-instances-query';
import {
  DEFAULT_TRAMITES_VISIBLE_COLUMNS,
  TRAMITES_COLUMNS,
  tramitesSortOptions,
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

describe('tramitesSortOptions — HU #12108', () => {
  it('una celda compuesta ofrece cada uno de sus datos con su clave de orden', () => {
    // "Radicado" apila las dos fechas: un clic en la cabecera no podría decir por cuál se ordena.
    expect(tramitesSortOptions('radicado', DEFAULT_TRAMITES_VISIBLE_COLUMNS)).toEqual([
      { id: 'radicado', label: 'Radicado', sort: 'radicado' },
      { id: 'fechaCreacion', label: 'Fecha de creación', sort: 'createdAt' },
      { id: 'fechaActualizacion', label: 'Fecha de actualización', sort: 'updatedAt' },
    ]);
    // "Vehículo" apila la placa y el VIN; marca/modelo no es ordenable en el API.
    expect(tramitesSortOptions('placa', DEFAULT_TRAMITES_VISIBLE_COLUMNS).map((o) => o.sort)).toEqual([
      'placa',
      'vin',
    ]);
    // "Trámite / Estado" apila el tipo y el estado.
    expect(tramitesSortOptions('tramite', DEFAULT_TRAMITES_VISIBLE_COLUMNS).map((o) => o.sort)).toEqual([
      'tipo_tramite',
      'estado',
    ]);
  });

  it('un dato deja de ofrecerse desde la celda compuesta si su columna dedicada está visible', () => {
    // Si no, habría DOS cabeceras distintas ordenando por lo mismo.
    const conVin = [...DEFAULT_TRAMITES_VISIBLE_COLUMNS, 'vin'];
    expect(tramitesSortOptions('placa', conVin).map((o) => o.sort)).toEqual(['placa']);
    expect(tramitesSortOptions('vin', conVin).map((o) => o.sort)).toEqual(['vin']);
  });

  it('una columna sin nada ordenable no ofrece opciones', () => {
    // La secretaría vive en `field_values`: su ORDER BY exige un join, y queda fuera de alcance.
    expect(tramitesSortOptions('secretaria', DEFAULT_TRAMITES_VISIBLE_COLUMNS)).toEqual([]);
  });

  it('las claves ofrecidas son las que el backend acepta', () => {
    const aceptadas = new Set([
      'radicado', 'placa', 'vin', 'comprador', 'gestor',
      'estado', 'tipo_tramite', 'fuente', 'createdAt', 'updatedAt',
    ]);
    for (const columna of TRAMITES_COLUMNS) {
      for (const opcion of tramitesSortOptions(columna.key, DEFAULT_TRAMITES_VISIBLE_COLUMNS)) {
        expect(aceptadas.has(opcion.sort)).toBe(true);
      }
    }
  });
});

describe('columnas ordenables', () => {
  it('son ordenables las columnas que tienen algún dato con clave de orden', () => {
    // Ya no hay un flag `sortable` que mantener aparte: una columna es ordenable si alguno de sus
    // datos lo es. Con el flag, `radicado`, `tramite`, `estado` y `fuente` se volvieron ordenables
    // en el backend y el flag siguió diciendo que no — dos verdades sobre lo mismo.
    const ordenables = TRAMITES_COLUMNS
      .filter((c) => tramitesSortOptions(c.key, DEFAULT_TRAMITES_VISIBLE_COLUMNS).length > 0)
      .map((c) => c.key);

    // Se compara el CONJUNTO, no el orden: el orden del catálogo es la disposición de la tabla.
    expect([...ordenables].sort()).toEqual(
      [
        'radicado', 'placa', 'comprador', 'tramite', 'gestor', 'fuente',
        'vin', 'estado', 'fechaCreacion', 'fechaActualizacion',
      ].sort(),
    );
  });
});
