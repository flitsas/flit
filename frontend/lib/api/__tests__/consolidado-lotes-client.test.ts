import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import {
  ConsolidadoLotesApiError,
  MENSAJE_LOTE_ACTIVO,
  MENSAJE_REINTENTAR_DESCARGA,
  MENSAJE_TOPE_DESCARGA,
  consolidadoLotesClient,
  interpretarErrorCrearLote,
} from '@/lib/api/consolidado-lotes-client';
import type { LoteConsolidados } from '@/lib/api/types-consolidado-lotes';
import type { ModeloSeleccionLote } from '@/hooks/useSeleccionLote';

// Uso de ejemplo:
//   const lote = await consolidadoLotesClient.crearLote({ seleccion: modelo });
//   // → POST /api/v1/tramites/consolidados/lotes { tipoDocumento: 'consolidado', confirmaEfectos: true, seleccion }
//   try { ... } catch (e) { const r = interpretarErrorCrearLote(e); r.tipo === 'lote_activo' && r.loteActivoId }

/** Lote de prueba según `LoteConsolidados` del contrato §5 (datos sintéticos). */
const LOTE_EN_COLA: LoteConsolidados = {
  id: '0f8c6a1e-0000-4000-8000-000000000001',
  estado: 'en_cola',
  tipoDocumento: 'consolidado',
  total: 348,
  procesados: 0,
  incluidos: 0,
  omitidos: 0,
  generados: 0,
  creadoEn: '2026-10-07T15:00:00Z',
  terminadoEn: null,
  expiraEn: null,
  nombreBase: 'consolidados_20261007_1500',
  partes: [],
};

const SELECCION_FILTRO: ModeloSeleccionLote<Record<string, unknown>> = {
  modo: 'filtro',
  ids: [],
  excluidos: ['inst-0002', 'inst-0005'],
  filtro: { estado: 'preparado', alcanceRed: null },
};

function respuesta(status: number, body?: unknown): Response {
  return new Response(body === undefined ? null : JSON.stringify(body), {
    status,
    headers: { 'Content-Type': 'application/json' },
  });
}

describe('consolidadoLotesClient.crearLote — HU #13381', () => {
  const fetchMock = vi.fn();
  beforeEach(() => {
    fetchMock.mockReset();
    vi.stubGlobal('fetch', fetchMock);
  });
  afterEach(() => vi.unstubAllGlobals());

  it('AC2 — POST a /api/v1/tramites/consolidados/lotes con confirmaEfectos=true y el modelo tal cual', async () => {
    fetchMock.mockResolvedValue(respuesta(202, LOTE_EN_COLA));
    const lote = await consolidadoLotesClient.crearLote({ seleccion: SELECCION_FILTRO });

    expect(lote).toEqual(LOTE_EN_COLA);
    expect(fetchMock).toHaveBeenCalledTimes(1);
    const [url, init] = fetchMock.mock.calls[0] as [string, RequestInit];
    expect(new URL(url).pathname).toBe('/api/v1/tramites/consolidados/lotes');
    expect(init.method).toBe('POST');
    expect((init.headers as Record<string, string>)['Content-Type']).toBe('application/json');
    expect(JSON.parse(String(init.body))).toEqual({
      tipoDocumento: 'consolidado',
      confirmaEfectos: true,
      seleccion: SELECCION_FILTRO,
    });
  });

  it('AC2 — selección por ids: viajan los ids marcados', async () => {
    fetchMock.mockResolvedValue(respuesta(202, LOTE_EN_COLA));
    await consolidadoLotesClient.crearLote({
      seleccion: { modo: 'ids', ids: ['inst-0001', 'inst-0003'], excluidos: [], filtro: null },
    });
    const body = JSON.parse(String((fetchMock.mock.calls[0] as [string, RequestInit])[1].body));
    expect(body.seleccion).toEqual({ modo: 'ids', ids: ['inst-0001', 'inst-0003'], excluidos: [], filtro: null });
    expect(body.confirmaEfectos).toBe(true);
  });

  it('AC3 — 409 lote_activo: error con el código y el id del lote activo', async () => {
    fetchMock.mockResolvedValue(
      respuesta(409, { error: 'lote_activo', loteActivoId: LOTE_EN_COLA.id, detail: 'Ya hay uno' }),
    );
    const err = await consolidadoLotesClient.crearLote({ seleccion: SELECCION_FILTRO }).catch((e) => e);
    expect(err).toBeInstanceOf(ConsolidadoLotesApiError);
    expect(err.status).toBe(409);
    expect(err.codigo).toBe('lote_activo');
    expect(err.loteActivoId).toBe(LOTE_EN_COLA.id);
    expect(interpretarErrorCrearLote(err)).toEqual({
      tipo: 'lote_activo',
      mensaje: MENSAJE_LOTE_ACTIVO,
      loteActivoId: LOTE_EN_COLA.id,
    });
  });

  it('AC3 — el código también se lee de `extensions.error` (ProblemDetails)', async () => {
    fetchMock.mockResolvedValue(
      respuesta(409, { title: 'Conflict', extensions: { error: 'lote_activo', loteActivoId: 'abc' } }),
    );
    const err = await consolidadoLotesClient.crearLote({ seleccion: SELECCION_FILTRO }).catch((e) => e);
    expect(err.codigo).toBe('lote_activo');
    expect(err.loteActivoId).toBe('abc');
  });

  it('AC4 — 422: mensaje de tope', async () => {
    fetchMock.mockResolvedValue(respuesta(422, { error: 'seleccion_invalida' }));
    const err = await consolidadoLotesClient.crearLote({ seleccion: SELECCION_FILTRO }).catch((e) => e);
    expect(err.status).toBe(422);
    expect(interpretarErrorCrearLote(err)).toEqual({ tipo: 'tope', mensaje: MENSAJE_TOPE_DESCARGA });
    expect(MENSAJE_TOPE_DESCARGA).toMatch(/10\.000/);
  });

  it('AC4 — 503 motor_inactivo: «No se pudo completar la descarga, intente de nuevo»', async () => {
    fetchMock.mockResolvedValue(respuesta(503, { error: 'motor_inactivo' }));
    const err = await consolidadoLotesClient.crearLote({ seleccion: SELECCION_FILTRO }).catch((e) => e);
    expect(err.status).toBe(503);
    expect(err.codigo).toBe('motor_inactivo');
    expect(interpretarErrorCrearLote(err)).toEqual({ tipo: 'reintentar', mensaje: MENSAJE_REINTENTAR_DESCARGA });
    expect(MENSAJE_REINTENTAR_DESCARGA).toBe('No se pudo completar la descarga, intente de nuevo');
  });

  it('AC4 — 503 con cuerpo HTML de gateway: mismo mensaje, sin volcar el HTML', async () => {
    fetchMock.mockResolvedValue(new Response('<html>Bad gateway</html>', { status: 503 }));
    const err = await consolidadoLotesClient.crearLote({ seleccion: SELECCION_FILTRO }).catch((e) => e);
    expect(err.codigo).toBeNull();
    expect(err.message).not.toMatch(/html/i);
    expect(interpretarErrorCrearLote(err).mensaje).toBe(MENSAJE_REINTENTAR_DESCARGA);
  });

  it('AC4 — falla de red (fetch rechaza): status 0 y mensaje de reintento', async () => {
    fetchMock.mockRejectedValue(new TypeError('Failed to fetch'));
    const err = await consolidadoLotesClient.crearLote({ seleccion: SELECCION_FILTRO }).catch((e) => e);
    expect(err).toBeInstanceOf(ConsolidadoLotesApiError);
    expect(err.status).toBe(0);
    expect(interpretarErrorCrearLote(err)).toEqual({ tipo: 'reintentar', mensaje: MENSAJE_REINTENTAR_DESCARGA });
  });

  it('edge — un error que no es del cliente se trata como reintento', () => {
    expect(interpretarErrorCrearLote(new Error('x')).tipo).toBe('reintentar');
    expect(interpretarErrorCrearLote(undefined).tipo).toBe('reintentar');
  });

  it('edge — 403: mensaje de permiso, distinto del de reintento', async () => {
    fetchMock.mockResolvedValue(respuesta(403));
    const err = await consolidadoLotesClient.crearLote({ seleccion: SELECCION_FILTRO }).catch((e) => e);
    const r = interpretarErrorCrearLote(err);
    expect(r.tipo).toBe('permiso');
    expect(r.mensaje).not.toBe(MENSAJE_REINTENTAR_DESCARGA);
  });
});

describe('consolidadoLotesClient lecturas — contrato §5', () => {
  const fetchMock = vi.fn();
  beforeEach(() => {
    fetchMock.mockReset();
    vi.stubGlobal('fetch', fetchMock);
  });
  afterEach(() => vi.unstubAllGlobals());

  it('obtenerLote — GET /api/v1/consolidados/lotes/{id}', async () => {
    fetchMock.mockResolvedValue(respuesta(200, LOTE_EN_COLA));
    const lote = await consolidadoLotesClient.obtenerLote(LOTE_EN_COLA.id);
    expect(lote).toEqual(LOTE_EN_COLA);
    const [url, init] = fetchMock.mock.calls[0] as [string, RequestInit];
    expect(new URL(url).pathname).toBe(`/api/v1/consolidados/lotes/${LOTE_EN_COLA.id}`);
    expect(init.method ?? 'GET').toBe('GET');
  });

  it('obtenerLoteActual — 204 devuelve null; 200 devuelve el lote', async () => {
    fetchMock.mockResolvedValueOnce(new Response(null, { status: 204 }));
    expect(await consolidadoLotesClient.obtenerLoteActual()).toBeNull();
    fetchMock.mockResolvedValueOnce(respuesta(200, LOTE_EN_COLA));
    expect(await consolidadoLotesClient.obtenerLoteActual()).toEqual(LOTE_EN_COLA);
    expect(new URL((fetchMock.mock.calls[1] as [string])[0]).pathname).toBe('/api/v1/consolidados/lotes/actual');
  });

  it('obtenerLote — 404 lanza ConsolidadoLotesApiError', async () => {
    fetchMock.mockResolvedValue(respuesta(404));
    await expect(consolidadoLotesClient.obtenerLote('x')).rejects.toBeInstanceOf(ConsolidadoLotesApiError);
  });
});
