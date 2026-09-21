// HU #12601 (Feature #12595, ADR-0059) — el detalle y el historial del gestor hablan de los estados
// reales de la ruta de placa y del distintivo de rechazo desde Preasignación.
import { describe, expect, it } from 'vitest';
import { estadoChipStyle } from '@/lib/tramites/estados';
import { detalleEstadoHeader } from '../detalle-estado-header';
import { mapStatusHistoryToTimelineNodes } from '../timeline-mappers';
import type { StatusHistory } from '@/lib/api/types/procedure-runtime';

function hito(from: string | null, to: string, reason: string | null = null, at = '2026-09-16T10:00:00Z'): StatusHistory {
  return {
    fromStatus: from,
    toStatus: to,
    changedAt: at,
    reason,
    changedByName: null,
    changedByEmail: null,
    changedByCompania: null,
  } as StatusHistory;
}

describe('detalleEstadoHeader — ruta de placa (ADR-0059)', () => {
  it('AC2 — preasignacion y asignado son esperas distintas, y las dos se dicen', () => {
    const pre = detalleEstadoHeader('preasignacion');
    expect(pre.label).toBe('Preasignación');
    expect(pre.pendiente).toBe(true);
    expect(pre.alert).toMatch(/asignarla/);
    expect(pre.color).toBe(estadoChipStyle('preasignacion').accent);

    const asg = detalleEstadoHeader('asignado');
    expect(asg.label).toBe('Asignado');
    expect(asg.pendiente).toBe(true);
    expect(asg.alert).toMatch(/SOAT/);
    expect(asg.alert).toMatch(/envía/);
    expect(asg.color).toBe(estadoChipStyle('asignado').accent);
  });

  it('HU #12726 (C.1) — entregado usa el accent del catálogo (teal), no morado ni dorado', () => {
    const hdr = detalleEstadoHeader('entregado');
    expect(hdr.color).toBe(estadoChipStyle('entregado').accent);
    expect(hdr.color).toBe('#00A99D');
  });

  it('AC3 — rechazado desde preasignacion dice «Rechazado preasignación» y explica la vuelta a la cola', () => {
    const hdr = detalleEstadoHeader('rechazado', 'preasignacion');
    expect(hdr.label).toBe('Rechazado preasignación');
    expect(hdr.alert).toMatch(/antes de asignar placa/);
    expect(hdr.color).toBe(detalleEstadoHeader('rechazado').color);

    expect(detalleEstadoHeader('rechazado', 'entregado').label).toBe('Rechazado');
    expect(detalleEstadoHeader('rechazado').label).toBe('Rechazado');
  });

  it('revocado tiene cabecera propia (final, placa liberada)', () => {
    const hdr = detalleEstadoHeader('revocado');
    expect(hdr.label).toBe('Revocado');
    expect(hdr.pendiente).toBe(false);
    expect(hdr.alert).toMatch(/revocada/i);
  });
});

describe('historial del trámite — hitos de la ruta de placa (AC4)', () => {
  it('formatea «Preasignación desde Preparado», «Asignado desde Preasignación», «Entregado desde Asignado» y «Rechazado desde Preasignación (motivo)»', () => {
    const nodos = mapStatusHistoryToTimelineNodes([
      hito('preparado', 'preasignacion', null, '2026-09-16T10:00:00Z'),
      hito('preasignacion', 'asignado', 'Placa ABC123 asignada por el organismo de tránsito.', '2026-09-16T11:00:00Z'),
      hito('asignado', 'entregado', null, '2026-09-16T12:00:00Z'),
      hito('preasignacion', 'rechazado', 'Documentos ilegibles', '2026-09-16T13:00:00Z'),
    ]);

    expect(nodos.map((n) => n.info.extra)).toEqual([
      'Preasignación desde Preparado',
      'Asignado desde Preasignación (Placa ABC123 asignada por el organismo de tránsito.)',
      'Entregado desde Asignado',
      'Rechazado desde Preasignación (Documentos ilegibles)',
    ]);
    expect(nodos.map((n) => n.label)).toEqual(['Preasignación', 'Asignado', 'Entregado', 'Rechazado']);
  });
});
