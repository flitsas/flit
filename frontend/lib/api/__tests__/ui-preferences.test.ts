import { afterEach, describe, expect, it } from 'vitest';
import { uiPreferencesClient } from '../ui-preferences';

/**
 * Cliente del selector de columnas: GET/PUT /api/v1/me/ui-preferences/{scope}. El contrato lo
 * define el equipo de backend en paralelo (misma forma acordada); estos tests fijan la
 * invariante del lado cliente: Bearer + X-Tenant-Id (mismo criterio que tramites-client) y el
 * shape `{ value }` en el body del PUT.
 */
function setToken(payload: Record<string, unknown>): void {
  const b64 = (obj: unknown) =>
    Buffer.from(JSON.stringify(obj))
      .toString('base64')
      .replace(/\+/g, '-')
      .replace(/\//g, '_')
      .replace(/=+$/, '');
  const token = `${b64({ alg: 'none' })}.${b64(payload)}.`;
  document.cookie = `flit_token=${token}; path=/`;
}

function clearToken(): void {
  document.cookie = 'flit_token=; expires=Thu, 01 Jan 1970 00:00:00 GMT; path=/';
}

describe('uiPreferencesClient', () => {
  const originalFetch = globalThis.fetch;

  afterEach(() => {
    globalThis.fetch = originalFetch;
    clearToken();
  });

  it('GET envía Bearer + X-Tenant-Id y devuelve { scope, value }', async () => {
    setToken({ sub: 'u1', tenant_id: 'tenant-abc' });
    let capturedUrl = '';
    let capturedHeaders: Record<string, string> = {};
    globalThis.fetch = (async (url: string, init?: RequestInit) => {
      capturedUrl = String(url);
      capturedHeaders = (init?.headers as Record<string, string>) ?? {};
      return new Response(JSON.stringify({ scope: 'tramites.columns', value: {} }), {
        status: 200,
        headers: { 'Content-Type': 'application/json' },
      });
    }) as typeof fetch;

    const res = await uiPreferencesClient.get('tramites.columns');

    expect(capturedUrl).toContain('/api/v1/me/ui-preferences/tramites.columns');
    expect(capturedHeaders.Authorization).toMatch(/^Bearer /);
    expect(capturedHeaders['X-Tenant-Id']).toBe('tenant-abc');
    expect(res).toEqual({ scope: 'tramites.columns', value: {} });
  });

  it('sin preferencia guardada, el backend responde value: {} (no 404)', async () => {
    setToken({ sub: 'u1', tenant_id: 'tenant-abc' });
    globalThis.fetch = (async () =>
      new Response(JSON.stringify({ scope: 'ot.procedures.columns', value: {} }), {
        status: 200,
        headers: { 'Content-Type': 'application/json' },
      })) as typeof fetch;

    const res = await uiPreferencesClient.get('ot.procedures.columns');
    expect(res.value).toEqual({});
  });

  it('PUT envía { value } en el body y devuelve la respuesta del backend', async () => {
    setToken({ sub: 'u1', tenant_id: 'tenant-abc' });
    let capturedMethod = '';
    let capturedBody = '';
    globalThis.fetch = (async (_url: string, init?: RequestInit) => {
      capturedMethod = init?.method ?? '';
      capturedBody = String(init?.body ?? '');
      return new Response(
        JSON.stringify({ scope: 'tramites.columns', value: { visible: ['radicado', 'vin'] } }),
        { status: 200, headers: { 'Content-Type': 'application/json' } },
      );
    }) as typeof fetch;

    const res = await uiPreferencesClient.put('tramites.columns', { visible: ['radicado', 'vin'] });

    expect(capturedMethod).toBe('PUT');
    expect(JSON.parse(capturedBody)).toEqual({ value: { visible: ['radicado', 'vin'] } });
    expect(res.value.visible).toEqual(['radicado', 'vin']);
  });

  it('propaga el mensaje legible del backend cuando la petición falla', async () => {
    globalThis.fetch = (async () =>
      new Response(JSON.stringify({ title: 'boom', detail: 'no se pudo guardar' }), {
        status: 500,
        headers: { 'Content-Type': 'application/problem+json' },
      })) as typeof fetch;

    await expect(uiPreferencesClient.get('tramites.columns')).rejects.toThrow('no se pudo guardar');
  });
});
