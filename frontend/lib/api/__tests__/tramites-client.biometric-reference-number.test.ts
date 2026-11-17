import { afterEach, describe, expect, it } from 'vitest';
import { tramitesClient } from '../tramites-client';

/**
 * Ajuste 2026-11-10 (D1=a) — buscar por radicado en Validación de Identidad. Contrato acordado con
 * backend: query param opcional `referenceNumber` (coincidencia parcial en servidor) en
 * `GET /api/v1/tramites/biometric-validations/by-person` y en la variante de red
 * `GET /api/v1/tramites/network/identity-validations/by-person`.
 *
 * Uso de ejemplo: `await tramitesClient.listTenantBiometricPersons({ referenceNumber: 'FT1-00000' })`
 * → `GET …/biometric-validations/by-person?referenceNumber=FT1-00000`.
 */
describe('tramites-client — referenceNumber en la grilla por persona', () => {
  const originalFetch = globalThis.fetch;
  let urls: string[] = [];

  afterEach(() => {
    globalThis.fetch = originalFetch;
    urls = [];
  });

  function capture() {
    globalThis.fetch = (async (input: RequestInfo | URL) => {
      urls.push(String(input));
      return new Response(JSON.stringify({ persons: [], stats: {}, page: 1, pageSize: 20, total: 0 }), {
        status: 200,
        headers: { 'Content-Type': 'application/json' },
      });
    }) as typeof fetch;
  }

  it('la ruta propia envía referenceNumber', async () => {
    capture();
    await tramitesClient.listTenantBiometricPersons({ referenceNumber: 'FT1-00000' });
    const url = new URL(urls[0], 'http://x');
    expect(url.pathname).toBe('/api/v1/tramites/biometric-validations/by-person');
    expect(url.searchParams.get('referenceNumber')).toBe('FT1-00000');
  });

  it('la ruta de red envía referenceNumber junto al childTenantId', async () => {
    capture();
    await tramitesClient.listNetworkIdentityPersons({ referenceNumber: 'FT2-0000010' }, 'hijo-1');
    const url = new URL(urls[0], 'http://x');
    expect(url.pathname).toBe('/api/v1/tramites/network/identity-validations/by-person');
    expect(url.searchParams.get('referenceNumber')).toBe('FT2-0000010');
    expect(url.searchParams.get('childTenantId')).toBe('hijo-1');
  });

  it('vacío o ausente no viaja', async () => {
    capture();
    await tramitesClient.listTenantBiometricPersons({ referenceNumber: '  ' });
    await tramitesClient.listTenantBiometricPersons({});
    for (const u of urls) {
      expect(new URL(u, 'http://x').searchParams.has('referenceNumber')).toBe(false);
    }
  });
});
