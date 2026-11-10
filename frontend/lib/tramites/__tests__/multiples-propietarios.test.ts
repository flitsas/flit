// ADR-0053 (Múltiple Propietario) en el listado de trámites: la marca «Con múltiples propietarios»
// en la columna Marcas y todos los propietarios de cada lado en el Excel.
import { describe, expect, it } from 'vitest';
import { marcasDe, marcasLabel } from '../tramites-row-labels';
import { tramitesExportFields } from '../tramites-table-columns';
import type { InstanceSummary } from '@/lib/api/types/procedure-runtime';

function fila(parcial: Partial<InstanceSummary>): InstanceSummary {
  return {
    id: '1',
    referenceNumber: 'FT1-1',
    modalidad: 'TRASPASO',
    ...parcial,
  } as InstanceSummary;
}

describe('marca «Con múltiples propietarios»', () => {
  it('se pinta solo cuando el servidor la marca', () => {
    expect(marcasDe(fila({ multiplesPropietarios: true })).map((m) => m.id)).toEqual([
      'multiples-propietarios',
    ]);
    expect(marcasDe(fila({ multiplesPropietarios: false }))).toEqual([]);
    // Backend anterior al campo: sin marca, como con prenda y transformación.
    expect(marcasDe(fila({}))).toEqual([]);
  });

  it('convive con las otras marcas, en orden, y sale en texto para el Excel', () => {
    const conTodas = fila({
      tienePrenda: true,
      tieneTransformacion: true,
      multiplesPropietarios: true,
    });
    expect(marcasDe(conTodas).map((m) => m.id)).toEqual([
      'prenda',
      'transformacion',
      'multiples-propietarios',
    ]);
    expect(marcasLabel(conTodas)).toBe('prenda, transformación, múltiples propietarios');
  });
});

describe('exportación con copropietarios', () => {
  const campo = (id: string) =>
    tramitesExportFields(['propietario', 'comprador']).find((c) => c.id === id)!;

  it('lleva a todos los propietarios del lado, principal primero', () => {
    const conVarios = fila({
      vendedorNombre: 'Ana Uno',
      vendedoresActores: [
        { nombre: 'Ana Uno', firmaEstado: 'firmado' },
        { nombre: 'Luis Dos', firmaEstado: 'pendiente' },
      ],
      compradorNombre: 'Eva Tres',
      compradoresActores: [
        { nombre: 'Eva Tres', firmaEstado: null },
        { nombre: 'Juan Cuatro', firmaEstado: null },
        { nombre: 'Sara Cinco', firmaEstado: null },
      ],
    });
    expect(campo('vendedor').raw(conVarios)).toBe('Ana Uno, Luis Dos');
    expect(campo('comprador').raw(conVarios)).toBe('Eva Tres, Juan Cuatro, Sara Cinco');
  });

  it('sin la lista (backend anterior) cae al nombre del principal', () => {
    const anterior = fila({ vendedorNombre: 'Ana Uno', compradorNombre: 'Eva Tres' });
    expect(campo('vendedor').raw(anterior)).toBe('Ana Uno');
    expect(campo('comprador').raw(anterior)).toBe('Eva Tres');
  });
});
