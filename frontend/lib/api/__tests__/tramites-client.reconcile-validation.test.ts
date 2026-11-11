/**
 * Contrato del cliente: reconciliar con Kyverum Verify por `validationId` (trámite o prevalidación).
 *
 * Uso de ejemplo: await tramitesClient.reconcileBiometricValidation('v-9', 'tenant-1')
 *   → POST /api/v1/tramites/biometric-validations/v-9/reconcile  (X-Tenant-Id: tenant-1)
 */
import { afterEach, describe, expect, it, vi } from 'vitest';

import { tramitesClient, TramitesApiError } from '@/lib/api/tramites-client';

function jsonResponse(status: number, body: unknown): Response {
  return new Response(JSON.stringify(body), {
    status,
    headers: { 'content-type': 'application/json' },
  });
}

afterEach(() => {
  vi.unstubAllGlobals();
});

describe('tramitesClient.reconcileBiometricValidation', () => {
  it('hace POST a la ruta por validación con X-Tenant-Id y devuelve { status, updated }', async () => {
    const fetchMock = vi.fn().mockResolvedValue(jsonResponse(200, { status: 'aprobado', updated: true }));
    vi.stubGlobal('fetch', fetchMock);

    const res = await tramitesClient.reconcileBiometricValidation('v-9', 'tenant-1');

    expect(res).toEqual({ status: 'aprobado', updated: true });
    const [url, init] = fetchMock.mock.calls[0] as [string, RequestInit];
    expect(url).toMatch(/\/api\/v1\/tramites\/biometric-validations\/v-9\/reconcile$/);
    expect(init.method).toBe('POST');
    expect((init.headers as Record<string, string>)['X-Tenant-Id']).toBe('tenant-1');
  });

  it('un Problem 409 se propaga como TramitesApiError con el detail', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn().mockResolvedValue(
        jsonResponse(409, { title: 'Conflict', detail: 'La validación no es del proveedor Kyverum.' }),
      ),
    );
    const err = await tramitesClient.reconcileBiometricValidation('v-9', 'tenant-1').catch((e) => e);
    expect(err).toBeInstanceOf(TramitesApiError);
    expect((err as TramitesApiError).status).toBe(409);
    expect((err as Error).message).toContain('La validación no es del proveedor Kyverum.');
  });
});
