import { afterEach, describe, expect, it, vi } from 'vitest';
import { fetchNetworkDocumentos, TramitesApiError } from '../tramites-client';

/**
 * HU #13419 AC5 (épica #13216) — contrato de `GET /api/v1/tramites/network/documentos`
 * (`200 { documentosRed: boolean }`, mismas puertas y 403 que `/network/children`).
 *
 * Uso de ejemplo: `await fetchNetworkDocumentos(controller.signal)` → `true` si la cabeza tiene los
 * documentos de red encendidos; `false` si están apagados o la respuesta no trae el booleano
 * (fail-closed); un 403 lanza `TramitesApiError` y decide el llamador.
 */
describe('fetchNetworkDocumentos — HU #13419 AC5', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  function responder(status: number, body: unknown) {
    const fetchMock = vi.fn(async () =>
      new Response(body === undefined ? null : JSON.stringify(body), {
        status,
        headers: { 'Content-Type': 'application/json' },
      }),
    );
    vi.stubGlobal('fetch', fetchMock);
    return fetchMock;
  }

  it('happy path — 200 { documentosRed: true } ⇒ true, por la ruta de red y con la señal de aborto', async () => {
    const fetchMock = responder(200, { documentosRed: true });
    const ctrl = new AbortController();
    await expect(fetchNetworkDocumentos(ctrl.signal)).resolves.toBe(true);
    const [url, init] = fetchMock.mock.calls[0] as unknown as [string, RequestInit];
    expect(new URL(url).pathname).toBe('/api/v1/tramites/network/documentos');
    expect(init.signal).toBe(ctrl.signal);
    expect(init.method ?? 'GET').toBe('GET');
  });

  it('contrato — 200 { documentosRed: false } ⇒ false (cabeza CONCESIÓN con documentos de red apagados)', async () => {
    responder(200, { documentosRed: false });
    await expect(fetchNetworkDocumentos()).resolves.toBe(false);
  });

  it.each([
    ['sin el campo', {}],
    ['campo no booleano', { documentosRed: 'true' }],
  ])('borde — respuesta %s ⇒ false (fail-closed)', async (_caso, body) => {
    responder(200, body);
    await expect(fetchNetworkDocumentos()).resolves.toBe(false);
  });

  it('borde — 403 network_scope_required ⇒ lanza TramitesApiError con status 403', async () => {
    responder(403, { error: 'network_scope_required', detail: 'no es cabeza' });
    const err = await fetchNetworkDocumentos().catch((e: unknown) => e);
    expect(err).toBeInstanceOf(TramitesApiError);
    expect((err as TramitesApiError).status).toBe(403);
  });
});
