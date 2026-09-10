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
      },
    ];
    const nodes = mapEventsToTimelineNodes(events);
    expect(nodes).toHaveLength(1);
    expect(nodes[0]!.label).toBe('Reasignación de gestor');
    expect(nodes[0]!.info.gestor).toBe('Diana Ruiz');
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
        partyRole: 'comprador',
        emailActualizado: true,
        correoDestinoEnmascarado: 'n***@dominio.com',
      },
    ];
    const nodes = mapEventsToTimelineNodes(events);
    expect(nodes).toHaveLength(1);
    expect(nodes[0]!.label).toBe('Reenvío de validación · Comprador');
    expect(nodes[0]!.info.correo).toBe('n***@dominio.com');
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
