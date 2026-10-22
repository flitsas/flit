/**
 * HU #12788 — contrato HTTP de `tramitesClient.generarConsolidado`.
 *
 * Uso de ejemplo:
 *   tramitesClient.generarConsolidado('inst-1')                  → POST …/instances/inst-1/consolidado
 *   tramitesClient.generarConsolidado('inst-1', undefined, true) → POST …/instances/inst-1/consolidado?force=true
 */
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { tramitesClient } from '@/lib/api/tramites-client';

function lastUrlAndMethod() {
  const mock = (globalThis.fetch as ReturnType<typeof vi.fn>).mock;
  const [url, init] = mock.calls[mock.calls.length - 1] as [string, RequestInit | undefined];
  return { url: String(url), method: init?.method };
}

describe('tramitesClient.generarConsolidado — parámetro force (HU #12788)', () => {
  beforeEach(() => {
    vi.stubGlobal(
      'fetch',
      vi.fn(
        async () =>
          new Response(
            JSON.stringify({
              document: { attachmentId: 'a', tipo: 'consolidado', filename: 'c.pdf', sha256: 'h' },
            }),
            { status: 200, headers: { 'content-type': 'application/json' } },
          ),
      ),
    );
  });

  afterEach(() => {
    vi.unstubAllGlobals();
    vi.restoreAllMocks();
  });

  it('sin force: POST al consolidado sin query string', async () => {
    await tramitesClient.generarConsolidado('inst-1');
    const { url, method } = lastUrlAndMethod();
    expect(method).toBe('POST');
    expect(url).toMatch(/\/api\/v1\/tramites\/instances\/inst-1\/consolidado$/);
    expect(url).not.toContain('force');
  });

  it('force=false explícito tampoco envía la query', async () => {
    await tramitesClient.generarConsolidado('inst-1', undefined, false);
    expect(lastUrlAndMethod().url).not.toContain('force');
  });

  it('force=true: añade ?force=true', async () => {
    await tramitesClient.generarConsolidado('inst-1', undefined, true);
    const { url, method } = lastUrlAndMethod();
    expect(method).toBe('POST');
    expect(url).toMatch(/\/consolidado\?force=true$/);
  });
});
