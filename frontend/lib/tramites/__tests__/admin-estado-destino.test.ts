// «Cambiar estado» del administrador: el selector ofrece todos los estados de negocio menos `aprobado`
// (y el actual); los de la ruta de placa solo en matrícula.
import { describe, expect, it } from 'vitest';
import {
  ADMIN_ESTADO_DESTINO_OPTIONS,
  opcionesEstadoDestinoAdmin,
} from '../admin-tramite-permissions';
import { ESTADOS_TRAMITE } from '../estados';

describe('ADMIN_ESTADO_DESTINO_OPTIONS', () => {
  it('cubre todos los estados de negocio menos aprobado y el legado subsanacion', () => {
    const esperados = ESTADOS_TRAMITE.filter((e) => e !== 'aprobado' && e !== 'subsanacion');
    expect([...ADMIN_ESTADO_DESTINO_OPTIONS].sort()).toEqual([...esperados].sort());
  });

  it('nunca ofrece aprobado', () => {
    expect(ADMIN_ESTADO_DESTINO_OPTIONS).not.toContain('aprobado');
  });
});

describe('opcionesEstadoDestinoAdmin', () => {
  it('matrícula en asignado: ofrece los demás, incluida preasignación y revocado, en orden de ciclo de vida', () => {
    expect(opcionesEstadoDestinoAdmin('asignado', 'MATRICULAS')).toEqual([
      'borrador',
      'preparado',
      'preasignacion',
      'entregado',
      'rechazado',
      'anulado',
      'revocado',
    ]);
  });

  it('matrícula en entregado: ofrece preasignación y asignado', () => {
    const opciones = opcionesEstadoDestinoAdmin('entregado', 'MATRICULAS');
    expect(opciones).toContain('preasignacion');
    expect(opciones).toContain('asignado');
    expect(opciones).not.toContain('entregado');
  });

  it('fuera de matrícula no ofrece los estados de la ruta de placa', () => {
    for (const familia of ['TRASPASO', 'OTROS']) {
      const opciones = opcionesEstadoDestinoAdmin('entregado', familia);
      expect(opciones).not.toContain('preasignacion');
      expect(opciones).not.toContain('asignado');
      expect(opciones).toContain('revocado');
    }
  });

  it('sin familia conocida ofrece todos menos el actual', () => {
    expect(opcionesEstadoDestinoAdmin('borrador', null)).toEqual(
      ADMIN_ESTADO_DESTINO_OPTIONS.filter((e) => e !== 'borrador'),
    );
  });
});
