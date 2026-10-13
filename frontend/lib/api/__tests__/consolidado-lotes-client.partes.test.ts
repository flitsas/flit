import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import { ConsolidadoLotesApiError, consolidadoLotesClient } from '@/lib/api/consolidado-lotes-client';

// Uso de ejemplo:
//   await consolidadoLotesClient.descargarParte(loteId, { numero: 2, nombreArchivo: 'consolidados_…_parte-02-de-03.zip' });
//   // → GET /api/v1/consolidados/lotes/{loteId}/partes/2 (application/zip); 410 → ConsolidadoLotesApiError(410, 'descarga_expirada')

const PARTE = { numero: 2, nombreArchivo: 'consolidados_20261007_1000_parte-02-de-03.zip' };

describe('consolidadoLotesClient.descargarParte — HU #13382', () => {
  const fetchMock = vi.fn();
  beforeEach(() => {
    fetchMock.mockReset();
    vi.stubGlobal('fetch', fetchMock);
  });
  afterEach(() => vi.unstubAllGlobals());

  it('GET a /api/v1/consolidados/lotes/{id}/partes/{n} (contrato §5)', async () => {
    fetchMock.mockResolvedValue(
      new Response(new Blob(['PK']), {
        status: 200,
        headers: {
          'Content-Type': 'application/zip',
          'Content-Disposition': `attachment; filename="${PARTE.nombreArchivo}"`,
        },
      }),
    );
    await consolidadoLotesClient.descargarParte('lote-1', PARTE);
    const [url, init] = fetchMock.mock.calls[0] as [string, RequestInit];
    expect(new URL(url).pathname).toBe('/api/v1/consolidados/lotes/lote-1/partes/2');
    expect(init.method ?? 'GET').toBe('GET');
  });

  it('410 descarga_expirada → ConsolidadoLotesApiError con status y código', async () => {
    fetchMock.mockResolvedValue(
      new Response(JSON.stringify({ error: 'descarga_expirada' }), {
        status: 410,
        headers: { 'Content-Type': 'application/json' },
      }),
    );
    const err = await consolidadoLotesClient.descargarParte('lote-1', PARTE).catch((e) => e);
    expect(err).toBeInstanceOf(ConsolidadoLotesApiError);
    expect(err).toMatchObject({ status: 410, codigo: 'descarga_expirada' });
  });

  it('código en extensions (ProblemDetails) también se lee', async () => {
    fetchMock.mockResolvedValue(
      new Response(JSON.stringify({ title: 'x', extensions: { error: 'auditoria_no_registrada' } }), { status: 503 }),
    );
    const err = await consolidadoLotesClient.descargarParte('lote-1', PARTE).catch((e) => e);
    expect(err).toMatchObject({ status: 503, codigo: 'auditoria_no_registrada' });
  });

  it('falla de red → status 0', async () => {
    fetchMock.mockRejectedValue(new TypeError('Failed to fetch'));
    const err = await consolidadoLotesClient.descargarParte('lote-1', PARTE).catch((e) => e);
    expect(err).toMatchObject({ status: 0, codigo: null });
  });
});
