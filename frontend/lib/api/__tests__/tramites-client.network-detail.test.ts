import { afterEach, describe, expect, it } from 'vitest';
import { desenvolverDetalleDeRed, tramitesClient } from '../tramites-client';

/**
 * HU #12362 — contrato de `GET /api/v1/tramites/network/instances/{id}`
 * (`NetworkProcedureDetailResponse`, #12358): el servidor ENVUELVE el detalle en
 * `{ tenantId, tenantName, instance }`. El cliente lo aplana al shape del detalle propio para que
 * el modal y sus secciones (actores, fieldValues, statusHistory) lean lo mismo en consulta.
 *
 * Uso de ejemplo: `await tramitesClient.getNetworkInstance('inst-red')` →
 * `{ id, actors, fieldValues, …, tenantId: <hijo>, tenantName, fromNetwork: true }`.
 */
describe('tramites-client — getNetworkInstance desenvuelve el sobre del detalle de red', () => {
  const originalFetch = globalThis.fetch;

  afterEach(() => {
    globalThis.fetch = originalFetch;
  });

  function respondWith(body: unknown) {
    globalThis.fetch = (async () =>
      new Response(JSON.stringify(body), {
        status: 200,
        headers: { 'Content-Type': 'application/json' },
      })) as typeof fetch;
  }

  const instance = {
    id: 'inst-red',
    referenceNumber: 'TR-RED',
    status: 'entregado',
    procedureTypeId: 'pt-1',
    tenantId: 'hijo',
    createdAt: '2026-06-18T00:00:00Z',
    submittedAt: null,
    completedAt: null,
    fieldValues: [{ formFieldId: 'f', fieldKey: 'placa', valueText: 'BBB222', valueJson: null, source: 'x' }],
    statusHistory: [],
    actors: [
      { actorType: 'comprador', documentType: 'CC', documentNumber: '1', fullName: 'Comp', email: null },
    ],
  };

  it('aplana `{ tenantId, tenantName, instance }` al shape del detalle propio', async () => {
    respondWith({ tenantId: 'hijo', tenantName: 'Hijo SAS', instance });
    const res = await tramitesClient.getNetworkInstance('inst-red');
    expect(res.id).toBe('inst-red');
    expect(res.actors).toEqual(instance.actors);
    expect(res.fieldValues).toEqual(instance.fieldValues);
    expect(res.tenantId).toBe('hijo');
    expect(res.tenantName).toBe('Hijo SAS');
    expect(res.fromNetwork).toBe(true);
    expect(res).not.toHaveProperty('instance');
  });

  it('el tenantId del sobre manda sobre el de `instance` y tenantName nulo cae a cadena vacía', () => {
    const res = desenvolverDetalleDeRed({
      tenantId: 'hijo-sobre',
      tenantName: null,
      instance: { ...instance, tenantId: 'otro' } as never,
    });
    expect(res.tenantId).toBe('hijo-sobre');
    expect(res.tenantName).toBe('');
    expect(res.fromNetwork).toBe(true);
  });

  it('tolera una respuesta ya plana (sin `instance`)', () => {
    const res = desenvolverDetalleDeRed({ ...instance, tenantName: 'Plano' } as never);
    expect(res.id).toBe('inst-red');
    expect(res.actors).toEqual(instance.actors);
    expect(res.tenantId).toBe('hijo');
    expect(res.tenantName).toBe('Plano');
  });
});
