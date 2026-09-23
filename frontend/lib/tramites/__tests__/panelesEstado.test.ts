import { describe, expect, it } from 'vitest';
import { familiaUsaEstado, panelesDeEstado } from '../panelesEstado';

// Epic #12686 — HU #12802.
describe('panelesDeEstado', () => {
  it('AC1 — gestor en Todos: 8 tarjetas en orden, sin Preparado, Subsanación ni Rechazado preasignación', () => {
    expect(panelesDeEstado('gestor', '')).toEqual([
      'borrador',
      'preasignacion',
      'asignado',
      'entregado',
      'aprobado',
      'rechazado',
      'revocado',
      'anulado',
    ]);
  });

  it('AC2 — SuperAdmin en Todos: 9 tarjetas, con Preparado', () => {
    expect(panelesDeEstado('superadmin', '')).toEqual([
      'borrador',
      'preparado',
      'preasignacion',
      'asignado',
      'entregado',
      'aprobado',
      'rechazado',
      'revocado',
      'anulado',
    ]);
  });

  it.each(['TRASPASO', 'OTROS'] as const)(
    'AC3 — %s oculta Preasignación y Asignado en las dos perspectivas',
    (familia) => {
      for (const perspectiva of ['gestor', 'superadmin'] as const) {
        const paneles = panelesDeEstado(perspectiva, familia);
        expect(paneles).not.toContain('preasignacion');
        expect(paneles).not.toContain('asignado');
        expect(paneles).toContain('entregado');
      }
      expect(panelesDeEstado('gestor', familia)).toHaveLength(6);
      expect(panelesDeEstado('superadmin', familia)).toHaveLength(7);
    },
  );

  it('AC4 — Matrículas muestra todas las tarjetas de la perspectiva', () => {
    expect(panelesDeEstado('gestor', 'MATRICULAS')).toEqual(panelesDeEstado('gestor', ''));
    expect(panelesDeEstado('superadmin', 'MATRICULAS')).toEqual(panelesDeEstado('superadmin', ''));
  });

  it('AC5 — la tarjeta Asignado deja de existir al pasar a Traspaso (el listado limpia el filtro)', () => {
    expect(panelesDeEstado('gestor', '').includes('asignado')).toBe(true);
    expect(panelesDeEstado('gestor', 'TRASPASO').includes('asignado')).toBe(false);
    expect(panelesDeEstado('gestor', 'TRASPASO').includes('aprobado')).toBe(true);
  });
});

describe('familiaUsaEstado', () => {
  it('la pestaña Todos y Matrículas usan la ruta de placa; Traspaso y Otros no', () => {
    expect(familiaUsaEstado('', 'preasignacion')).toBe(true);
    expect(familiaUsaEstado('MATRICULAS', 'asignado')).toBe(true);
    expect(familiaUsaEstado('TRASPASO', 'preasignacion')).toBe(false);
    expect(familiaUsaEstado('OTROS', 'asignado')).toBe(false);
    expect(familiaUsaEstado('OTROS', 'borrador')).toBe(true);
  });
});
