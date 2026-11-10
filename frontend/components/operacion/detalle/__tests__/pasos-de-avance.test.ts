import { describe, expect, it } from 'vitest';
import type { StatusHistory } from '@/lib/api/types/procedure-runtime';
import { pasosDeAvance } from '../DetalleResumenAvance';

const h = (fromStatus: string | null, toStatus: string, changedAt: string) =>
  ({ fromStatus, toStatus, changedAt, reason: null }) as StatusHistory;

// HU #14482 — la línea de avance del detalle resume el historial real; no inventa pasos.
describe('pasosDeAvance', () => {
  it('hechos del historial, el vigente como actual y lo que falta de la ruta como pendiente', () => {
    const pasos = pasosDeAvance({
      history: [h('borrador', 'preparado', '2026-10-28T15:00:00Z'), h('preparado', 'entregado', '2026-10-29T15:00:00Z')],
      estadoVigente: 'entregado',
      createdAt: '2026-10-27T15:00:00Z',
      familia: 'TRASPASO',
    });
    expect(pasos.map((p) => [p.estado, p.tipo])).toEqual([
      ['borrador', 'hecho'],
      ['preparado', 'hecho'],
      ['entregado', 'actual'],
      ['aprobado', 'pendiente'],
    ]);
  });

  it('la matrícula recorre la ruta de placa en los pendientes; un rechazo no tiene pendientes', () => {
    const matricula = pasosDeAvance({ history: [], estadoVigente: 'preparado', createdAt: '2026-10-27T15:00:00Z', familia: 'MATRICULAS' });
    expect(matricula.filter((p) => p.tipo === 'pendiente').map((p) => p.estado)).toEqual([
      'preasignacion',
      'asignado',
      'entregado',
      'aprobado',
    ]);
    const rechazo = pasosDeAvance({
      history: [h('entregado', 'rechazado', '2026-10-30T15:00:00Z')],
      estadoVigente: 'rechazado',
      rejectedFrom: 'preasignacion',
      createdAt: '2026-10-27T15:00:00Z',
      familia: 'MATRICULAS',
    });
    expect(rechazo.at(-1)).toMatchObject({ estado: 'rechazado', tipo: 'actual' });
    expect(rechazo.some((p) => p.tipo === 'pendiente')).toBe(false);
  });
});
