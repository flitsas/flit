/**
 * HU #12792 AC4 (decisión D3, HU #10858) — no regresión: el frontend no añade ni pide marca de agua
 * al consolidado. No existe ningún parámetro/flag de watermark en el cliente ni en el visor; este
 * test lo asegura en la petición HTTP y en el código fuente de la cadena de entrega.
 *
 * Uso de ejemplo:
 *   await tramitesClient.generarConsolidado('inst-1');      // POST …/consolidado (sin query)
 *   await tramitesClient.entregarConsolidado('inst-1');     // GET  …/consolidado/entrega (sin query)
 */
import { readFileSync } from 'node:fs';
import path from 'node:path';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { tramitesClient } from '@/lib/api/tramites-client';

const fetchMock = vi.fn();

beforeEach(() => {
  fetchMock.mockReset();
  fetchMock.mockImplementation(
    async () =>
      new Response(
        JSON.stringify({
          document: { attachmentId: 'att-1', tipo: 'consolidado', filename: 'c.pdf', sha256: 's' },
        }),
        { status: 200, headers: { 'content-type': 'application/json' } },
      ),
  );
  vi.stubGlobal('fetch', fetchMock);
});

afterEach(() => {
  vi.unstubAllGlobals();
});

function url(): URL {
  return new URL(String(fetchMock.mock.calls[0][0]));
}

describe('HU #12792 AC4 — el cliente no pide marca de agua', () => {
  it('generarConsolidado: POST sin query de watermark', async () => {
    await tramitesClient.generarConsolidado('inst-1');
    const u = url();
    expect(u.pathname).toBe('/api/v1/tramites/instances/inst-1/consolidado');
    expect(u.search).toBe('');
  });

  it('entregarConsolidado: GET sin query de watermark (vigente / definitivo)', async () => {
    await tramitesClient.entregarConsolidado('inst-1');
    const u = url();
    expect(u.pathname).toBe('/api/v1/tramites/instances/inst-1/consolidado/entrega');
    expect(u.search).toBe('');
  });

  it('ni con force ni con soloLectura aparece un parámetro de marca de agua', async () => {
    await tramitesClient.generarConsolidado('inst-1', undefined, true);
    await tramitesClient.entregarConsolidado('inst-1', { tipo: 'consolidado', soloLectura: true });
    for (const [llamada, init] of fetchMock.mock.calls as [string, RequestInit | undefined][]) {
      expect(String(llamada)).not.toMatch(/watermark|marca/i);
      expect(String(init?.body ?? '')).not.toMatch(/watermark|marca/i);
      const headers = new Headers(init?.headers);
      headers.forEach((valor, clave) => {
        expect(`${clave}:${valor}`).not.toMatch(/watermark|marca/i);
      });
    }
  });
});

describe('HU #12792 AC4 — no existe flag de watermark en la cadena de entrega', () => {
  const RAIZ = path.resolve(__dirname, '..');
  it.each([
    'lib/api/tramites-client.ts',
    'components/operacion/ExpedienteVisor.tsx',
    'components/operacion/TramiteDocumentosModal.tsx',
    'components/shared/IndicadorVigenciaConsolidado.tsx',
    'lib/tramites/vigencia-consolidado.ts',
  ])('%s no menciona watermark', (archivo) => {
    const fuente = readFileSync(path.join(RAIZ, archivo), 'utf8');
    expect(fuente).not.toMatch(/watermark|marca_?de_?agua|marcaAgua/i);
  });
});
