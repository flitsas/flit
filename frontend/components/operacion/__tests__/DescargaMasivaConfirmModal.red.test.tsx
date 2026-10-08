import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

import type { LoteConsolidados } from '@/lib/api/types-consolidado-lotes';
import type { ModeloSeleccionLote } from '@/hooks/useSeleccionLote';
import { DescargaMasivaConfirmModal } from '@/components/operacion/DescargaMasivaConfirmModal';
import { MENSAJES_RECHAZO_RED, type CodigoRechazoRed } from '@/lib/api/consolidado-lotes-client';

// Uso de ejemplo (HU #13419 — vista de red):
//   <DescargaMasivaConfirmModal open seleccion={lote.modelo} contador={lote.contador}
//     alcanceRed={lote.alcance} etiquetaAlcance="Red · Concesionario Hijo SAS"
//     onCreado={alCrear} onLoteActivo={seguir} onRechazoRed={(codigo) => ...} onClose={cerrar} />

const HIJA = '22222222-2222-2222-2222-222222222222';

/** Datos sintéticos según `LoteConsolidados` del contrato (HU #13417). */
const LOTE: LoteConsolidados = {
  id: '0f8c6a1e-0000-4000-8000-000000000201',
  estado: 'en_cola',
  tipoDocumento: 'consolidado',
  total: 2,
  procesados: 0,
  incluidos: 0,
  omitidos: 0,
  creadoEn: '2026-10-07T15:00:00Z',
  terminadoEn: null,
  expiraEn: null,
  partes: [],
  alcanceRed: 'red',
};
const SELECCION: ModeloSeleccionLote<Record<string, unknown>> = {
  modo: 'ids',
  ids: ['inst-propio', 'inst-hija'],
  excluidos: [],
  filtro: null,
};

const fetchMock = vi.fn();
const respuesta = (status: number, body?: unknown) =>
  new Response(body === undefined ? null : JSON.stringify(body), {
    status,
    headers: { 'Content-Type': 'application/json' },
  });
function servidor(crear: () => Response) {
  fetchMock.mockImplementation(async (url: string, init?: RequestInit) => {
    const path = new URL(url).pathname;
    if (path === '/api/v1/tramites/consolidados/lotes' && init?.method === 'POST') return crear();
    throw new TypeError('Failed to fetch');
  });
}

beforeEach(() => {
  fetchMock.mockReset();
  vi.stubGlobal('fetch', fetchMock);
});
afterEach(() => vi.unstubAllGlobals());

function montar(over: Partial<Parameters<typeof DescargaMasivaConfirmModal>[0]> = {}) {
  const props = {
    open: true,
    onClose: vi.fn(),
    seleccion: SELECCION,
    contador: 2,
    onCreado: vi.fn(),
    onLoteActivo: vi.fn(),
    onRechazoRed: vi.fn(),
    alcanceRed: 'red' as string | null,
    ...over,
  };
  render(<DescargaMasivaConfirmModal {...props} />);
  return props;
}
const dialogo = () => screen.getByRole('dialog', { name: /Descargar consolidados en ZIP/ });
const confirmar = () => within(dialogo()).getByRole('button', { name: /Confirmar descarga/ });

describe('DescargaMasivaConfirmModal — vista de red (HU #13419)', () => {
  it('AC1 — confirmar con la red envía `alcanceRed: "red"` en la raíz y modo ids', async () => {
    const user = userEvent.setup();
    servidor(() => respuesta(202, LOTE));
    const props = montar();
    await user.click(confirmar());
    await waitFor(() => expect(props.onCreado).toHaveBeenCalledWith(LOTE));
    const body = JSON.parse(String((fetchMock.mock.calls[0][1] as RequestInit).body));
    expect(body.alcanceRed).toBe('red');
    expect(body.seleccion.modo).toBe('ids');
    expect(body.seleccion.filtro).toBeNull();
  });

  it('AC2 — acotado a una hija: el uuid viaja en la raíz y el modal dice el alcance', async () => {
    const user = userEvent.setup();
    servidor(() => respuesta(202, { ...LOTE, alcanceRed: 'hija' }));
    montar({
      alcanceRed: HIJA,
      etiquetaAlcance: 'Red · Concesionario Hijo SAS',
      seleccion: { modo: 'filtro', ids: [], excluidos: [], filtro: { estado: 'preparado' } },
      contador: 57,
    });
    expect(within(dialogo()).getByText(/Red · Concesionario Hijo SAS/)).toBeInTheDocument();
    await user.click(confirmar());
    await waitFor(() => expect(fetchMock).toHaveBeenCalled());
    const body = JSON.parse(String((fetchMock.mock.calls[0][1] as RequestInit).body));
    expect(body.alcanceRed).toBe(HIJA);
    expect(body.seleccion.modo).toBe('filtro');
  });

  it.each<CodigoRechazoRed>([
    'network_documents_disabled',
    'network_role_required',
    'network_child_out_of_scope',
    'network_scope_required',
  ])('AC4 — 403 %s: mensaje claro, sin reintento y sin crear el aviso del lote', async (codigo) => {
    const user = userEvent.setup();
    servidor(() => respuesta(403, { error: codigo, detail: 'texto del servidor' }));
    const props = montar();
    await user.click(confirmar());
    const alerta = await within(dialogo()).findByText(MENSAJES_RECHAZO_RED[codigo]);
    expect(alerta).toBeInTheDocument();
    expect(within(dialogo()).queryByText(/network_/)).not.toBeInTheDocument();
    expect(confirmar()).toBeDisabled();
    expect(props.onCreado).not.toHaveBeenCalled();
    expect(props.onLoteActivo).not.toHaveBeenCalled();
    expect(props.onRechazoRed).toHaveBeenCalledWith(codigo);
  });

  it('borde — sin vista de red (alcanceRed undefined) el cuerpo no lleva la clave y no hay rótulo', async () => {
    const user = userEvent.setup();
    servidor(() => respuesta(202, { ...LOTE, alcanceRed: undefined }));
    montar({ alcanceRed: undefined });
    expect(within(dialogo()).queryByText(/^Alcance/)).not.toBeInTheDocument();
    await user.click(confirmar());
    await waitFor(() => expect(fetchMock).toHaveBeenCalled());
    const body = JSON.parse(String((fetchMock.mock.calls[0][1] as RequestInit).body));
    expect('alcanceRed' in body).toBe(false);
  });
});
