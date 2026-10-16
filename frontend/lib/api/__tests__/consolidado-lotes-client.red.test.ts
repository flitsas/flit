import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import {
  ConsolidadoLotesApiError,
  MENSAJES_RECHAZO_RED,
  consolidadoLotesClient,
  interpretarErrorCrearLote,
  type CodigoRechazoRed,
} from '@/lib/api/consolidado-lotes-client';
import type { LoteConsolidados } from '@/lib/api/types-consolidado-lotes';
import type { ModeloSeleccionLote } from '@/hooks/useSeleccionLote';

// Uso de ejemplo (HU #13419 — vista de red):
//   await consolidadoLotesClient.crearLote({ seleccion: lote.modelo, alcanceRed: lote.alcance });
//   // → POST /api/v1/tramites/consolidados/lotes
//   //   { tipoDocumento, confirmaEfectos: true, alcanceRed: 'red' | '<uuid hija>' | null, seleccion }
//   // `alcanceRed` va en la RAÍZ y nunca dentro de `seleccion.filtro` (deprecado en el contrato).
//   catch (e) { const r = interpretarErrorCrearLote(e); r.tipo === 'red' && r.codigo }

const HIJA = '22222222-2222-2222-2222-222222222222';

/** Datos sintéticos según `LoteConsolidados` del contrato (HU #13417). */
const LOTE_RED: LoteConsolidados = {
  id: '0f8c6a1e-0000-4000-8000-000000000101',
  estado: 'en_cola',
  tipoDocumento: 'consolidado',
  total: 57,
  procesados: 0,
  incluidos: 0,
  omitidos: 0,
  creadoEn: '2026-10-07T15:00:00Z',
  terminadoEn: null,
  expiraEn: null,
  partes: [],
  alcanceRed: 'hija',
};

const respuesta = (status: number, body?: unknown) =>
  new Response(body === undefined ? null : JSON.stringify(body), {
    status,
    headers: { 'Content-Type': 'application/json' },
  });

describe('consolidadoLotesClient.crearLote — alcance de red en la raíz (HU #13419)', () => {
  const fetchMock = vi.fn();
  const cuerpo = () => JSON.parse(String((fetchMock.mock.calls[0] as [string, RequestInit])[1].body));
  beforeEach(() => {
    fetchMock.mockReset();
    vi.stubGlobal('fetch', fetchMock);
  });
  afterEach(() => vi.unstubAllGlobals());

  it('AC1 — modo ids con la vista de red: `alcanceRed: "red"` en la raíz y `filtro: null`', async () => {
    fetchMock.mockResolvedValue(respuesta(202, { ...LOTE_RED, alcanceRed: 'red' }));
    const seleccion: ModeloSeleccionLote<Record<string, unknown>> = {
      modo: 'ids',
      ids: ['inst-propio', 'inst-hija'],
      excluidos: [],
      filtro: null,
    };
    const lote = await consolidadoLotesClient.crearLote({ seleccion, alcanceRed: 'red' });
    expect(lote.alcanceRed).toBe('red');
    expect(cuerpo()).toEqual({
      tipoDocumento: 'consolidado',
      confirmaEfectos: true,
      alcanceRed: 'red',
      seleccion: { modo: 'ids', ids: ['inst-propio', 'inst-hija'], excluidos: [], filtro: null },
    });
  });

  it('AC2 — modo filtro acotado a una hija: el uuid de la hija va en la raíz', async () => {
    fetchMock.mockResolvedValue(respuesta(202, LOTE_RED));
    await consolidadoLotesClient.crearLote({
      seleccion: { modo: 'filtro', ids: [], excluidos: ['inst-9'], filtro: { estado: 'preparado' } },
      alcanceRed: HIJA,
    });
    const body = cuerpo();
    expect(body.alcanceRed).toBe(HIJA);
    expect(body.seleccion).toEqual({
      modo: 'filtro',
      ids: [],
      excluidos: ['inst-9'],
      filtro: { estado: 'preparado' },
    });
  });

  it('contrato — `seleccion.filtro.alcanceRed` (deprecado) nunca viaja, aunque venga en el modelo', async () => {
    fetchMock.mockResolvedValue(respuesta(202, LOTE_RED));
    await consolidadoLotesClient.crearLote({
      seleccion: { modo: 'filtro', ids: [], excluidos: [], filtro: { estado: 'preparado', alcanceRed: HIJA } },
      alcanceRed: HIJA,
    });
    const body = cuerpo();
    expect(body.alcanceRed).toBe(HIJA);
    expect(body.seleccion.filtro).toEqual({ estado: 'preparado' });
    expect('alcanceRed' in body.seleccion.filtro).toBe(false);
  });

  it('borde — alcance propio explícito (`null`) viaja como `alcanceRed: null`', async () => {
    fetchMock.mockResolvedValue(respuesta(202, { ...LOTE_RED, alcanceRed: undefined }));
    await consolidadoLotesClient.crearLote({
      seleccion: { modo: 'ids', ids: ['inst-1'], excluidos: [], filtro: null },
      alcanceRed: null,
    });
    expect(cuerpo()).toHaveProperty('alcanceRed', null);
  });

  it('borde — sin `alcanceRed` (bandeja OT, Super Admin) el cuerpo no cambia: no aparece la clave', async () => {
    fetchMock.mockResolvedValue(respuesta(202, LOTE_RED));
    await consolidadoLotesClient.crearLote({
      seleccion: { modo: 'ids', ids: ['inst-1'], excluidos: [], filtro: null },
    });
    expect('alcanceRed' in cuerpo()).toBe(false);
  });
});

describe('interpretarErrorCrearLote — 403 de red (HU #13419 AC4)', () => {
  const fetchMock = vi.fn();
  beforeEach(() => {
    fetchMock.mockReset();
    vi.stubGlobal('fetch', fetchMock);
  });
  afterEach(() => vi.unstubAllGlobals());

  const CODIGOS: CodigoRechazoRed[] = [
    'network_documents_disabled',
    'network_role_required',
    'network_child_out_of_scope',
    'network_scope_required',
  ];

  it.each(CODIGOS)('403 %s ⇒ tipo «red» con su mensaje propio', async (codigo) => {
    fetchMock.mockResolvedValue(respuesta(403, { error: codigo, detail: 'texto del servidor' }));
    const err = await consolidadoLotesClient
      .crearLote({ seleccion: { modo: 'ids', ids: ['x'], excluidos: [], filtro: null }, alcanceRed: 'red' })
      .catch((e) => e);
    expect(err).toBeInstanceOf(ConsolidadoLotesApiError);
    expect(err.codigo).toBe(codigo);
    expect(interpretarErrorCrearLote(err)).toEqual({
      tipo: 'red',
      codigo,
      mensaje: MENSAJES_RECHAZO_RED[codigo],
    });
  });

  it('los cuatro mensajes son distintos, legibles y sin el código técnico', () => {
    const mensajes = CODIGOS.map((c) => MENSAJES_RECHAZO_RED[c]);
    expect(new Set(mensajes).size).toBe(4);
    for (const m of mensajes) {
      expect(m.length).toBeGreaterThan(20);
      expect(m).not.toMatch(/network_/);
    }
  });

  it('el código también se lee de `extensions.error` (ProblemDetails)', async () => {
    fetchMock.mockResolvedValue(respuesta(403, { title: 'Forbidden', extensions: { error: 'network_child_out_of_scope' } }));
    const err = await consolidadoLotesClient
      .crearLote({ seleccion: { modo: 'ids', ids: ['x'], excluidos: [], filtro: null }, alcanceRed: 'red' })
      .catch((e) => e);
    expect(interpretarErrorCrearLote(err)).toMatchObject({ tipo: 'red', codigo: 'network_child_out_of_scope' });
  });

  it('borde — un 403 sin código de red sigue siendo «permiso» (sin regresión de #13381)', () => {
    expect(interpretarErrorCrearLote(new ConsolidadoLotesApiError(403, null)).tipo).toBe('permiso');
    expect(interpretarErrorCrearLote(new ConsolidadoLotesApiError(403, 'sin_compania')).tipo).toBe('permiso');
  });
});
