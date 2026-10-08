import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { render, screen, waitFor, within } from '@testing-library/react';

import { LoteDescargaTracker, LoteDescargaTrackerProvider } from '@/components/shared/LoteDescargaTracker';
import type { LoteConsolidados } from '@/lib/api/types-consolidado-lotes';

/**
 * HU #13419 AC6 (épica #13216) — el aviso global del lote de red rotula «Red · {hija}» resolviendo el
 * nombre por `alcanceHijaId` contra `GET /api/v1/tramites/network/children`, también tras una recarga
 * (el lote llega por `/consolidados/lotes/actual`, sin que nadie lo haya «mostrado» en la pestaña).
 *
 * Uso de ejemplo:
 *   <LoteDescargaTrackerProvider habilitado><LoteDescargaTracker /></LoteDescargaTrackerProvider>
 *   // /actual → { …, alcanceRed: 'hija', alcanceHijaId: '<uuid>' } ⇒ «Red · Concesionario Hijo SAS»
 */

const HIJO = '22222222-2222-2222-2222-222222222222';
const HIJOS = [
  { id: HIJO, nombre: 'Concesionario Hijo SAS' },
  { id: '33333333-3333-3333-3333-333333333333', nombre: 'Autos del Norte SAS' },
];

/** Datos sintéticos (sin PII) según `LoteConsolidados` del contrato. */
const LOTE: LoteConsolidados = {
  id: '0f8c6a1e-0000-4000-8000-000000000419',
  estado: 'en_proceso',
  tipoDocumento: 'consolidado',
  total: 57,
  procesados: 10,
  incluidos: 10,
  omitidos: 0,
  creadoEn: '2026-10-07T15:00:00Z',
  terminadoEn: null,
  expiraEn: null,
  partes: [],
};

const json = (status: number, body: unknown) =>
  new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } });
const fetchMock = vi.fn();
function servidor(actual: LoteConsolidados, hijos: () => Response = () => json(200, HIJOS)) {
  fetchMock.mockImplementation(async (url: string) => {
    const path = new URL(String(url), 'http://localhost').pathname;
    if (path === '/api/v1/consolidados/lotes/actual') return json(200, actual);
    if (path === `/api/v1/consolidados/lotes/${actual.id}`) return json(200, actual);
    if (path === '/api/v1/tramites/network/children') return hijos();
    throw new TypeError('Failed to fetch');
  });
}
const llamadasHijos = () =>
  fetchMock.mock.calls.filter(([url]) => String(url).includes('/api/v1/tramites/network/children'));

const montar = () =>
  render(
    <LoteDescargaTrackerProvider habilitado>
      <LoteDescargaTracker />
    </LoteDescargaTrackerProvider>,
  );
const rotulo = async () => within(await screen.findByTestId('lote-descarga-card')).getByTestId('lote-alcance-red');

beforeEach(() => {
  fetchMock.mockReset();
  vi.stubGlobal('fetch', fetchMock);
});
afterEach(() => {
  vi.unstubAllGlobals();
});

describe('LoteDescargaTracker — rótulo «Red · hija» tras recarga (HU #13419 AC6)', () => {
  it('happy path — lote de hija recuperado por /actual: «Red · nombre de la hija» desde la lista de /network/children', async () => {
    servidor({ ...LOTE, alcanceRed: 'hija', alcanceHijaId: HIJO });
    montar();
    await waitFor(async () => expect(await rotulo()).toHaveTextContent('Red · Concesionario Hijo SAS'));
    expect(llamadasHijos()).toHaveLength(1);
    const init = llamadasHijos()[0][1] as RequestInit;
    expect(init.signal).toBeInstanceOf(AbortSignal);
  });

  it('borde — la hija ya no está en la red: «Red · compañía de la red» (no se hace pasar por toda la red)', async () => {
    servidor({ ...LOTE, alcanceRed: 'hija', alcanceHijaId: '99999999-9999-9999-9999-999999999999' });
    montar();
    await waitFor(() => expect(llamadasHijos()).toHaveLength(1));
    await waitFor(async () => expect(await rotulo()).toHaveTextContent(/Red · compañía de la red$/));
  });

  it.each([
    ['403', () => json(403, { error: 'network_scope_required' })],
    ['500', () => json(500, { error: 'boom' })],
  ])('borde — la lista de hijas falla (%s): «Red · compañía de la red» y el aviso sigue visible', async (_c, hijos) => {
    servidor({ ...LOTE, alcanceRed: 'hija', alcanceHijaId: HIJO }, hijos);
    montar();
    await waitFor(() => expect(llamadasHijos()).toHaveLength(1));
    await waitFor(async () => expect(await rotulo()).toHaveTextContent(/Red · compañía de la red$/));
    expect(screen.getByTestId('lote-descarga-card')).toHaveTextContent('Descarga masiva de consolidados');
  });

  it('contrato — lote de toda la red (alcanceHijaId null): «Red» y no se pide la lista de hijas', async () => {
    servidor({ ...LOTE, alcanceRed: 'red', alcanceHijaId: null });
    montar();
    expect(await rotulo()).toHaveTextContent(/^Alcance:\s*Red$/);
    expect(llamadasHijos()).toHaveLength(0);
  });

  it('contrato — lote propio (sin alcanceRed ni alcanceHijaId): sin rótulo y sin pedir la lista', async () => {
    servidor(LOTE);
    montar();
    const card = await screen.findByTestId('lote-descarga-card');
    expect(within(card).queryByTestId('lote-alcance-red')).not.toBeInTheDocument();
    expect(llamadasHijos()).toHaveLength(0);
  });
});
