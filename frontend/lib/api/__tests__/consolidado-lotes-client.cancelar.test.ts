import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import {
  ConsolidadoLotesApiError,
  MENSAJE_NO_SE_PUDO_CANCELAR,
  consolidadoLotesClient,
} from '@/lib/api/consolidado-lotes-client';
import { setActiveTramitesTenant } from '@/lib/api/tramites-client';
import type { LoteConsolidados, LoteTerminadoConflict } from '@/lib/api/types-consolidado-lotes';
import { TOKEN_STORAGE_KEY } from '@/lib/auth/jwt';

// Uso de ejemplo (HU #13388):
//   const lote = await consolidadoLotesClient.cancelar(loteId);
//   // → POST /api/v1/consolidados/lotes/{loteId}/cancelacion → 202 LoteConsolidados { estado: 'cancelado' }
//   // 409 lote_terminado / 404 / 403 / red → ConsolidadoLotesApiError { status, codigo }

/** Datos sintéticos según `LoteConsolidados` (contrato §4 del diseño #13307). */
const CANCELADO: LoteConsolidados = {
  id: '0f8c6a1e-0000-4000-8000-000000000088',
  estado: 'cancelado',
  tipoDocumento: 'consolidado',
  total: 348,
  procesados: 120,
  incluidos: 120,
  omitidos: 0,
  creadoEn: '2026-10-07T15:00:00Z',
  terminadoEn: '2026-10-07T15:05:00Z',
  expiraEn: '2026-10-07T15:05:00Z',
  partes: [],
};

function makeToken(payload: Record<string, unknown>): string {
  const header = Buffer.from(JSON.stringify({ alg: 'none', typ: 'JWT' })).toString('base64url');
  const body = Buffer.from(JSON.stringify(payload)).toString('base64url');
  return `${header}.${body}.`;
}
const respuesta = (status: number, body?: unknown) =>
  new Response(body === undefined ? null : JSON.stringify(body), {
    status,
    headers: { 'Content-Type': 'application/json' },
  });

describe('consolidadoLotesClient.cancelar — HU #13388', () => {
  const fetchMock = vi.fn();
  beforeEach(() => {
    fetchMock.mockReset();
    vi.stubGlobal('fetch', fetchMock);
  });
  afterEach(() => {
    vi.unstubAllGlobals();
    setActiveTramitesTenant(undefined);
    window.localStorage.removeItem(TOKEN_STORAGE_KEY);
  });

  it('AC2 — POST /api/v1/consolidados/lotes/{id}/cancelacion sin cuerpo y devuelve el lote del 202', async () => {
    fetchMock.mockResolvedValue(respuesta(202, CANCELADO));
    const lote = await consolidadoLotesClient.cancelar(CANCELADO.id);
    expect(lote).toEqual(CANCELADO);
    const [url, init] = fetchMock.mock.calls[0] as [string, RequestInit];
    expect(new URL(url).pathname).toBe(`/api/v1/consolidados/lotes/${CANCELADO.id}/cancelacion`);
    expect(init.method).toBe('POST');
    expect(init.body).toBeUndefined();
  });

  it('AC2 (contrato) — el id se codifica en la ruta', async () => {
    fetchMock.mockResolvedValue(respuesta(202, CANCELADO));
    await consolidadoLotesClient.cancelar('a/b');
    expect(String(fetchMock.mock.calls[0][0])).toContain('/api/v1/consolidados/lotes/a%2Fb/cancelacion');
  });

  it('cabeceras — las mismas que obtenerLote (ruta neutra: el dueño es el sub del JWT)', async () => {
    window.localStorage.setItem(TOKEN_STORAGE_KEY, makeToken({ sub: 'sa', role: 'SuperAdmin' }));
    setActiveTramitesTenant('33333333-3333-3333-3333-333333333333');
    fetchMock.mockResolvedValueOnce(respuesta(200, { ...CANCELADO, estado: 'en_proceso' }));
    fetchMock.mockResolvedValueOnce(respuesta(202, CANCELADO));
    await consolidadoLotesClient.obtenerLote(CANCELADO.id);
    await consolidadoLotesClient.cancelar(CANCELADO.id);
    const h = (i: number) => (fetchMock.mock.calls[i][1] as RequestInit).headers;
    expect(h(1)).toEqual(h(0));
    expect((h(1) as Record<string, string>).Authorization).toMatch(/^Bearer /);
  });

  it('AC5 — 409 lote_terminado lanza ConsolidadoLotesApiError con el código estable', async () => {
    const conflicto: LoteTerminadoConflict = { error: 'lote_terminado', estado: 'completado' };
    fetchMock.mockResolvedValue(respuesta(409, conflicto));
    const err = await consolidadoLotesClient.cancelar('x').catch((e: unknown) => e);
    expect(err).toBeInstanceOf(ConsolidadoLotesApiError);
    expect((err as ConsolidadoLotesApiError).status).toBe(409);
    expect((err as ConsolidadoLotesApiError).codigo).toBe('lote_terminado');
  });

  it('AC5 — 409 lote_terminado también se lee de extensions (ProblemDetails)', async () => {
    fetchMock.mockResolvedValue(respuesta(409, { status: 409, extensions: { error: 'lote_terminado', estado: 'fallido' } }));
    const err = (await consolidadoLotesClient.cancelar('x').catch((e: unknown) => e)) as ConsolidadoLotesApiError;
    expect(err.codigo).toBe('lote_terminado');
  });

  it.each([404, 403])('AC6 — %i lanza ConsolidadoLotesApiError con ese status', async (status) => {
    fetchMock.mockResolvedValue(respuesta(status));
    const err = (await consolidadoLotesClient.cancelar('x').catch((e: unknown) => e)) as ConsolidadoLotesApiError;
    expect(err).toBeInstanceOf(ConsolidadoLotesApiError);
    expect(err.status).toBe(status);
  });

  it('AC6 — falla de red → status 0', async () => {
    fetchMock.mockRejectedValue(new TypeError('Failed to fetch'));
    const err = (await consolidadoLotesClient.cancelar('x').catch((e: unknown) => e)) as ConsolidadoLotesApiError;
    expect(err.status).toBe(0);
  });

  it('contrato — texto aprobado del error de cancelación', () => {
    expect(MENSAJE_NO_SE_PUDO_CANCELAR).toBe('No se pudo cancelar la descarga');
  });
});
