import { describe, expect, it } from 'vitest';
import type { BiometricValidation, ProcedureInstanceEvent } from '@/lib/api/types/procedure-runtime';
import { mapEventsToTimelineNodes, mapIdentidadToTimelineNodes } from '../timeline-mappers';

/**
 * Bug #14634 — el tracking del trámite (dashboard) no mostraba el motivo real de un rechazo de VID
 * Manual: `mapIdentidadToTimelineNodes` leía `rejectionReason`/`ultimoIntentoMotivo` (Kyverum/mock),
 * nunca `rejectionReasonCode` (el código homologado, HU #13299). Y el histórico de un rechazo YA
 * resuelto (captura repetida o aprobada después) no dejaba ningún rastro en el timeline.
 */

const validacionBase: BiometricValidation = {
  id: 'val-1',
  partyRole: 'comprador',
  name: 'Laura Restrepo Ossa',
  documentType: 'CC',
  documentNumber: '1020998455',
  email: 'l.restrepo@correo.com',
  status: 'rechazado',
  intentos: 1,
  maxIntentos: 3,
  score: null,
  expiresAt: '2026-11-20T00:00:00Z',
  validatedAt: null,
  expired: false,
  provider: 'kyverum', // ocultar-manual.ts ya lo disfraza así para no-SuperAdmin
  captureUrl: null,
};

describe('Bug #14634 — mapIdentidadToTimelineNodes muestra el motivo real', () => {
  it('traduce rejectionReasonCode con el catálogo cerrado en vez de caer al nombre genérico del estado', () => {
    const nodes = mapIdentidadToTimelineNodes('TRASPASO', [
      { ...validacionBase, partyRole: 'vendedor', rejectionReasonCode: 'rostro_no_coincide' },
      { ...validacionBase, partyRole: 'comprador', status: 'aprobado' },
    ], []);

    const vendedor = nodes.find((n) => n.label.startsWith('Vendedor'));
    expect(vendedor?.info.extra).toBe('El rostro no coincide con el documento');
  });

  it('sin rejectionReasonCode (Kyverum/mock) sigue usando rejectionReason como antes', () => {
    const nodes = mapIdentidadToTimelineNodes('OTROS', [
      { ...validacionBase, rejectionReason: 'La foto no coincide con el documento' },
    ], []);

    expect(nodes[0]!.info.extra).toBe('La foto no coincide con el documento');
  });
});

describe('Bug #14634 — identidad_manual_rechazada deja rastro en el timeline de eventos', () => {
  const evento: ProcedureInstanceEvent = {
    tipo: 'identidad_manual_rechazada',
    createdAt: '2026-11-19T10:00:00Z',
    createdByName: 'Willyn Londoño Calle',
    partyRole: 'vendedor',
    rejectionReasonLabel: 'Imagen borrosa',
  };

  it('pinta un nodo con el motivo homologado y quién revisó, sin mencionar "manual"', () => {
    const nodes = mapEventsToTimelineNodes([evento]);

    expect(nodes).toHaveLength(1);
    expect(nodes[0]!.label).toBe('Validación de identidad rechazada · Vendedor');
    expect(nodes[0]!.info.extra).toBe('Imagen borrosa');
    expect(nodes[0]!.info.gestor).toBe('Willyn Londoño Calle');
    expect(JSON.stringify(nodes[0])).not.toMatch(/manual/i);
  });

  it('sin motivo cae al texto de respaldo, no a vacío', () => {
    const nodes = mapEventsToTimelineNodes([{ ...evento, rejectionReasonLabel: null }]);
    expect(nodes[0]!.info.extra).toBe('Sin motivo adicional registrado');
  });
});
