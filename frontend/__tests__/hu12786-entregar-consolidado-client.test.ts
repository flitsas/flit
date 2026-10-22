/**
 * HU #12786 — contrato del cliente HTTP de la ruta de entrega del consolidado (HU #12785):
 * `GET /api/v1/tramites/instances/{id}/consolidado/entrega` con `?tipo=`, `?force=`, `?soloLectura=`
 * (omitidos = default del backend) y `X-Tenant-Id` del tenant de la fila cuando se pasa.
 *
 * Uso de ejemplo:
 *   const res = await tramitesClient.entregarConsolidado('inst-1');            // vigente
 *   const res = await tramitesClient.entregarConsolidado('inst-1', {}, 'ten'); // SuperAdmin
 *   res.document.attachmentId // id a bajar por download / preview-url
 */
import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { tramitesClient } from '@/lib/api/tramites-client';

const RESPUESTA = {
  document: { attachmentId: 'att-1', tipo: 'consolidado', filename: 'c.pdf', sha256: 's' },
  regenerado: false,
  definitivoPorEstadoFinal: false,
  modo: 'vigente',
};

const fetchMock = vi.fn();

beforeEach(() => {
  fetchMock.mockReset();
  fetchMock.mockResolvedValue(
    new Response(JSON.stringify(RESPUESTA), {
      status: 200,
      headers: { 'content-type': 'application/json' },
    }),
  );
  vi.stubGlobal('fetch', fetchMock);
});

afterEach(() => {
  vi.unstubAllGlobals();
});

function llamada(): { url: URL; init: RequestInit } {
  const [url, init] = fetchMock.mock.calls[0] as [string, RequestInit];
  return { url: new URL(url), init };
}

describe('tramitesClient.entregarConsolidado — contrato HU #12785', () => {
  it('GET a la ruta de entrega sin query por defecto y devuelve los metadatos', async () => {
    const res = await tramitesClient.entregarConsolidado('inst-1');

    const { url, init } = llamada();
    expect(url.pathname).toBe('/api/v1/tramites/instances/inst-1/consolidado/entrega');
    expect(url.search).toBe('');
    expect(init.method ?? 'GET').toBe('GET');
    expect(res.document.attachmentId).toBe('att-1');
    expect(res.modo).toBe('vigente');
    expect(res.definitivoPorEstadoFinal).toBe(false);
  });

  it('solo manda los parámetros pedidos (tipo, force, soloLectura)', async () => {
    await tramitesClient.entregarConsolidado('inst-1', {
      tipo: 'consolidado_maestro',
      force: true,
      soloLectura: true,
    });

    const { url } = llamada();
    expect(url.searchParams.get('tipo')).toBe('consolidado_maestro');
    expect(url.searchParams.get('force')).toBe('true');
    expect(url.searchParams.get('soloLectura')).toBe('true');
  });

  it('force/soloLectura en false no viajan (comportamiento normal del backend)', async () => {
    await tramitesClient.entregarConsolidado('inst-1', { force: false, soloLectura: false });

    expect(llamada().url.search).toBe('');
  });

  it('manda X-Tenant-Id cuando el SuperAdmin pasa el tenant de la fila', async () => {
    await tramitesClient.entregarConsolidado('inst-1', {}, 'tenant-fila');

    const headers = new Headers(llamada().init.headers);
    expect(headers.get('X-Tenant-Id')).toBe('tenant-fila');
  });

  it('propaga el error del backend (404 consolidado_no_generado) sin tragárselo', async () => {
    fetchMock.mockResolvedValueOnce(
      new Response(JSON.stringify({ error: 'consolidado_no_generado' }), { status: 404 }),
    );

    await expect(tramitesClient.entregarConsolidado('inst-1', { soloLectura: true })).rejects.toThrow();
  });
});
