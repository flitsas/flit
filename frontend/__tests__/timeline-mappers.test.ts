import { describe, expect, it } from 'vitest';
import {
  mapEventsToTimelineNodes,
  mapIdentidadToTimelineNodes,
  mapStatusHistoryToTimelineNodes,
} from '@/components/operacion/detalle/timeline-mappers';
import type {
  BiometricValidation,
  ProcedureInstanceEvent,
  StatusHistory,
} from '@/lib/api/types/procedure-runtime';

describe('timeline-mappers', () => {
  it('mapStatusHistoryToTimelineNodes ordena y marca el último hito', () => {
    const history: StatusHistory[] = [
      { fromStatus: null, toStatus: 'borrador', changedAt: '2026-01-01T10:00:00Z', reason: null },
      { fromStatus: 'borrador', toStatus: 'entregado', changedAt: '2026-01-02T10:00:00Z', reason: null },
    ];
    const nodes = mapStatusHistoryToTimelineNodes(history);
    expect(nodes).toHaveLength(2);
    expect(nodes[0]!.label).toBe('Borrador');
    expect(nodes[1]!.isActive).toBe(true);
  });

  // Bug #12526 — gestor/correo/empresa venían fijos en '—' (y rol en 'Sistema') aunque el backend
  // tuviera el dato de quién ejecutó la transición.
  it('mapStatusHistoryToTimelineNodes pinta gestor, correo y empresa cuando el backend los trae', () => {
    const history: StatusHistory[] = [
      {
        fromStatus: 'borrador',
        toStatus: 'entregado',
        changedAt: '2026-01-02T10:00:00Z',
        reason: null,
        changedByName: 'Laura Restrepo',
        changedByEmail: 'laura.restrepo@renting.com',
        changedByCompania: 'Renting Colombia S.A.S',
      },
    ];
    const nodes = mapStatusHistoryToTimelineNodes(history);
    expect(nodes[0]!.info.gestor).toBe('Laura Restrepo');
    expect(nodes[0]!.info.correo).toBe('laura.restrepo@renting.com');
    expect(nodes[0]!.info.empresa).toBe('Renting Colombia S.A.S');
    expect(nodes[0]!.info.rol).toBe('Gestor');
    // El campo se llama "Fecha y hora" en la tarjeta: debe traer la hora, no solo el día
    // (formatFechaHora, no formatFecha — esta última es la fecha de negocio sin hora, HU #11018).
    expect(nodes[0]!.info.fecha).toBe('2026/01/02 05:00');
  });

  it('mapStatusHistoryToTimelineNodes cae al guion cuando fue un proceso automático', () => {
    const history: StatusHistory[] = [
      { fromStatus: null, toStatus: 'borrador', changedAt: '2026-01-01T10:00:00Z', reason: null },
    ];
    const nodes = mapStatusHistoryToTimelineNodes(history);
    expect(nodes[0]!.info.gestor).toBe('—');
    expect(nodes[0]!.info.correo).toBe('—');
    expect(nodes[0]!.info.empresa).toBe('—');
    expect(nodes[0]!.info.rol).toBe('Sistema');
  });

  it('mapIdentidadToTimelineNodes respeta firma del baúl', () => {
    const nodes = mapIdentidadToTimelineNodes('TRASPASO', [], ['vendedor']);
    expect(nodes).toHaveLength(2);
    const vendedor = nodes.find((n) => n.label.includes('Vendedor'));
    expect(vendedor?.info.extra).toBe('Acreditado por firma del baúl');
    expect(vendedor?.color).toBe('#8CC63F');
  });

  it('mapIdentidadToTimelineNodes usa estado biométrico cuando hay validación', () => {
    const validations: BiometricValidation[] = [
      {
        id: 'v1',
        partyRole: 'comprador',
        name: 'Ana Pérez',
        documentType: 'CC',
        documentNumber: '123',
        email: 'ana@test.com',
        status: 'aprobado',
        intentos: 1,
        maxIntentos: 3,
        score: 0.9,
        expiresAt: '2026-05-01T00:00:00Z',
        validatedAt: '2026-04-01T12:00:00Z',
        expired: false,
        provider: 'kyverum',
        captureUrl: null,
      },
    ];
    const nodes = mapIdentidadToTimelineNodes('MATRICULAS', validations, []);
    expect(nodes).toHaveLength(1);
    expect(nodes[0]!.label).toContain('Aprobado');
    expect(nodes[0]!.info.gestor).toBe('Ana Pérez');
  });

  // Bug #12376, defecto 3 — el nodo de identidad muestra el correo del REGISTRO, no el operativo
  // (que puede haber cambiado por un reenvío administrativo).
  it('mapIdentidadToTimelineNodes prioriza registeredEmail sobre email', () => {
    const validations: BiometricValidation[] = [
      {
        id: 'v1',
        partyRole: 'comprador',
        name: 'Ana Pérez',
        documentType: 'CC',
        documentNumber: '123',
        email: 'ana.nuevo@test.com',
        registeredEmail: 'ana.original@test.com',
        status: 'enviado',
        intentos: 0,
        maxIntentos: 3,
        score: null,
        expiresAt: '2026-05-01T00:00:00Z',
        validatedAt: null,
        expired: false,
        provider: 'kyverum',
        captureUrl: null,
      },
    ];
    const nodes = mapIdentidadToTimelineNodes('MATRICULAS', validations, []);
    expect(nodes[0]!.info.correo).toBe('ana.original@test.com');
  });

  it('mapIdentidadToTimelineNodes cae a email cuando no hay registeredEmail', () => {
    const validations: BiometricValidation[] = [
      {
        id: 'v1',
        partyRole: 'comprador',
        name: 'Ana Pérez',
        documentType: 'CC',
        documentNumber: '123',
        email: 'ana@test.com',
        status: 'enviado',
        intentos: 0,
        maxIntentos: 3,
        score: null,
        expiresAt: '2026-05-01T00:00:00Z',
        validatedAt: null,
        expired: false,
        provider: 'kyverum',
        captureUrl: null,
      },
    ];
    const nodes = mapIdentidadToTimelineNodes('MATRICULAS', validations, []);
    expect(nodes[0]!.info.correo).toBe('ana@test.com');
  });

  // Bug #12376, defecto 4 — la reasignación de gestor se pinta como nodo con gestor origen/destino,
  // ejecutor y fecha.
  it('mapEventsToTimelineNodes pinta reasignar_gestor_admin con origen/destino/ejecutor', () => {
    const events: ProcedureInstanceEvent[] = [
      {
        tipo: 'reasignar_gestor_admin',
        createdAt: '2026-06-01T10:00:00Z',
        createdByName: 'Willyn Londoño',
        previousAssignedToName: 'Carlos Gómez',
        newAssignedToName: 'Diana Ruiz',
        newAssignedToEmail: 'diana.ruiz@renting.com',
        newAssignedToCompania: 'Renting Colombia S.A.S',
      },
    ];
    const nodes = mapEventsToTimelineNodes(events);
    expect(nodes).toHaveLength(1);
    expect(nodes[0]!.label).toBe('Reasignación de gestor');
    expect(nodes[0]!.info.gestor).toBe('Diana Ruiz');
    // El correo/empresa son del gestor NUEVO (la misma "Diana Ruiz" de arriba), no de quien ejecutó.
    expect(nodes[0]!.info.correo).toBe('diana.ruiz@renting.com');
    expect(nodes[0]!.info.empresa).toBe('Renting Colombia S.A.S');
    expect(nodes[0]!.info.rol).toContain('Willyn Londoño');
    expect(nodes[0]!.info.extra).toBe('De Carlos Gómez a Diana Ruiz');
  });

  // Bug #12376, defecto 3 — el reenvío se pinta como evento adicional con el correo (enmascarado)
  // de destino, sin tocar el nodo de identidad original.
  it('mapEventsToTimelineNodes pinta reenvio_validacion_admin con el correo enmascarado', () => {
    const events: ProcedureInstanceEvent[] = [
      {
        tipo: 'reenvio_validacion_admin',
        createdAt: '2026-06-02T10:00:00Z',
        createdByName: 'Willyn Londoño',
        createdByCompania: 'Renting Colombia S.A.S',
        partyRole: 'comprador',
        emailActualizado: true,
        correoDestino: 'nueva@dominio.com',
      },
    ];
    const nodes = mapEventsToTimelineNodes(events);
    expect(nodes).toHaveLength(1);
    expect(nodes[0]!.label).toBe('Reenvío de validación · Comprador');
    expect(nodes[0]!.info.correo).toBe('nueva@dominio.com');
    // Sin gestor propio del evento: la empresa que aporta es la de quien lo ejecutó.
    expect(nodes[0]!.info.empresa).toBe('Renting Colombia S.A.S');
    expect(nodes[0]!.info.extra).toBe('Reenviado a un correo distinto del registrado');
  });

  it('mapEventsToTimelineNodes ordena por fecha ascendente', () => {
    const events: ProcedureInstanceEvent[] = [
      {
        tipo: 'reasignar_gestor_admin',
        createdAt: '2026-06-05T10:00:00Z',
        createdByName: null,
        newAssignedToName: 'Segundo',
      },
      {
        tipo: 'reasignar_gestor_admin',
        createdAt: '2026-06-01T10:00:00Z',
        createdByName: null,
        newAssignedToName: 'Primero',
      },
    ];
    const nodes = mapEventsToTimelineNodes(events);
    expect(nodes.map((n) => n.info.gestor)).toEqual(['Primero', 'Segundo']);
  });
});
