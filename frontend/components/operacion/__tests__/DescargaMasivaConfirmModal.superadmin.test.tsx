// HU #13387 (épica #13216, Feature #13307) — selector de tipo del Super Admin en la confirmación
// de la descarga masiva. El modal es el de #13381/#13394: el selector solo aparece con
// `selectorTipo` (lo activa `TramitesTable` cuando el usuario es Super Admin), el texto cambia entre
// las dos constantes aprobadas y al confirmar se envía `tipoDocumento` con el tipo elegido.
//
// Uso de ejemplo:
//   <BotonDescargaMasivaZip selectorTipo={esSuperAdmin} seleccion={lote.modelo} contador={lote.contador}
//     crear={(sel, tipo) => consolidadoLotesClient.crearLote({ seleccion: sel, tipoDocumento: tipo, cabecerasDelListado: {} })}
//     onCreado={alCrear} onLoteActivo={mostrarLote} />
import { act, render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import type { LoteConsolidados } from '@/lib/api/types-consolidado-lotes';
import type { ModeloSeleccionLote } from '@/hooks/useSeleccionLote';
import { ConsolidadoLotesApiError } from '@/lib/api/consolidado-lotes-client';
import {
  BotonDescargaMasivaZip,
  DescargaMasivaConfirmModal,
  TEXTO_CONFIRMACION_DESCARGA_MAESTROS,
  TEXTO_CONFIRMACION_DESCARGA_MASIVA,
} from '@/components/operacion/DescargaMasivaConfirmModal';

/** Datos sintéticos según `LoteConsolidados` del contrato (sin PII). */
const LOTE: LoteConsolidados = {
  id: '0f8c6a1e-0000-4000-8000-000000000055',
  estado: 'en_cola',
  tipoDocumento: 'consolidado',
  total: 118,
  procesados: 0,
  incluidos: 0,
  omitidos: 0,
  creadoEn: '2026-10-07T15:00:00Z',
  partes: [],
};
const SELECCION: ModeloSeleccionLote<Record<string, unknown>> = {
  modo: 'filtro',
  ids: [],
  excluidos: ['inst-0002', 'inst-0005'],
  filtro: { condiciones: [{ field: 'compania', operator: 'in', value: ['tenant-b'] }], alcanceRed: null },
};

const fetchMock = vi.fn();
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
    contador: 118,
    onCreado: vi.fn(),
    onLoteActivo: vi.fn(),
    selectorTipo: true,
    crear: vi.fn().mockResolvedValue(LOTE),
    ...over,
  };
  const r = render(<DescargaMasivaConfirmModal {...props} />);
  return { props, ...r };
}
const dialogo = () => screen.getByRole('dialog', { name: /Descargar consolidados en ZIP/ });
const confirmar = () => within(dialogo()).getByRole('button', { name: /Confirmar descarga/ });
const grupo = () => within(dialogo()).getByRole('radiogroup', { name: 'Tipo de documento' });
const radio = (nombre: RegExp) => within(dialogo()).getByRole('radio', { name: nombre });

describe('DescargaMasivaConfirmModal — selector de tipo del Super Admin (HU #13387)', () => {
  it('AC3 — con selectorTipo muestra «Consolidado» (marcado por defecto) y «Consolidado maestro»', () => {
    montar();
    expect(grupo()).toBeInTheDocument();
    expect(radio(/^Consolidado$/)).toBeChecked();
    expect(radio(/^Consolidado maestro$/)).not.toBeChecked();
  });

  it('AC3 — sin selectorTipo (Gestor) no hay radios y la creación por defecto lleva tipo consolidado', async () => {
    const user = userEvent.setup();
    fetchMock.mockResolvedValue(
      new Response(JSON.stringify(LOTE), { status: 202, headers: { 'Content-Type': 'application/json' } }),
    );
    const { props } = montar({ selectorTipo: undefined, crear: undefined });
    expect(within(dialogo()).queryByRole('radio')).not.toBeInTheDocument();
    expect(within(dialogo()).getByText(TEXTO_CONFIRMACION_DESCARGA_MASIVA)).toBeInTheDocument();
    await user.click(confirmar());
    await waitFor(() => expect(props.onCreado).toHaveBeenCalledWith(LOTE));
    expect(JSON.parse(String((fetchMock.mock.calls[0][1] as RequestInit).body)).tipoDocumento).toBe('consolidado');
  });

  it('AC3 — al reabrir el modal el selector vuelve a «Consolidado»', async () => {
    const user = userEvent.setup();
    const { rerender, props } = montar();
    await user.click(radio(/^Consolidado maestro$/));
    rerender(<DescargaMasivaConfirmModal {...props} open={false} />);
    rerender(<DescargaMasivaConfirmModal {...props} open />);
    expect(radio(/^Consolidado$/)).toBeChecked();
  });

  it('AC4 — «Consolidado» muestra exactamente el texto del consolidado', () => {
    montar();
    expect(within(dialogo()).getByText(TEXTO_CONFIRMACION_DESCARGA_MASIVA)).toBeInTheDocument();
    expect(within(dialogo()).queryByText(TEXTO_CONFIRMACION_DESCARGA_MAESTROS)).not.toBeInTheDocument();
  });

  it('AC4 — «Consolidado maestro» muestra exactamente el texto del maestro', async () => {
    const user = userEvent.setup();
    montar();
    await user.click(radio(/^Consolidado maestro$/));
    expect(within(dialogo()).getByText(TEXTO_CONFIRMACION_DESCARGA_MAESTROS)).toBeInTheDocument();
    expect(within(dialogo()).queryByText(TEXTO_CONFIRMACION_DESCARGA_MASIVA)).not.toBeInTheDocument();
  });

  it.each([
    ['consolidado', /^Consolidado$/],
    ['consolidado_maestro', /^Consolidado maestro$/],
  ] as const)('AC4 — al confirmar se envía tipoDocumento = %s', async (tipo, nombre) => {
    const user = userEvent.setup();
    const { props } = montar();
    await user.click(radio(nombre));
    await user.click(confirmar());
    await waitFor(() => expect(props.onCreado).toHaveBeenCalledWith(LOTE));
    expect(props.crear).toHaveBeenCalledWith(SELECCION, tipo);
  });

  it('AC6 — 409 lote_activo: aviso del lote activo y onLoteActivo con su id', async () => {
    const user = userEvent.setup();
    const crear = vi.fn().mockRejectedValue(new ConsolidadoLotesApiError(409, 'lote_activo', 'lote-activo-1'));
    fetchMock.mockResolvedValue(new Response(null, { status: 404 }));
    const { props } = montar({ crear });
    await user.click(confirmar());
    expect(await within(dialogo()).findByText(/Ya hay una descarga en curso/)).toBeInTheDocument();
    expect(props.onLoteActivo).toHaveBeenCalledWith('lote-activo-1');
    expect(props.onCreado).not.toHaveBeenCalled();
  });

  it.each([
    ['400 tipo_no_permitido', new ConsolidadoLotesApiError(400, 'tipo_no_permitido')],
    ['403', new ConsolidadoLotesApiError(403, null)],
    ['503 motor_inactivo', new ConsolidadoLotesApiError(503, 'motor_inactivo')],
  ])('AC6 — %s: «No se pudo completar la descarga, intente de nuevo» y la selección se conserva', async (_n, err) => {
    const user = userEvent.setup();
    const crear = vi.fn().mockRejectedValue(err);
    const { props } = montar({ crear });
    await user.click(radio(/^Consolidado maestro$/));
    await user.click(confirmar());
    expect(await within(dialogo()).findByRole('alert')).toHaveTextContent(
      'No se pudo completar la descarga, intente de nuevo',
    );
    expect(props.onCreado).not.toHaveBeenCalled();
    expect(props.onClose).not.toHaveBeenCalled();
    // La selección no se toca: el modal sigue con el mismo contador y el tipo elegido.
    expect(within(dialogo()).getByText(/118 trámites seleccionados/)).toBeInTheDocument();
    expect(radio(/^Consolidado maestro$/)).toBeChecked();
    expect(confirmar()).toBeEnabled();
  });

  it('AC7 — grupo de radios con etiqueta visible, operable con teclado y con foco visible', async () => {
    const user = userEvent.setup();
    montar();
    const g = grupo();
    expect(within(g).getByText('Tipo de documento')).toBeVisible();
    radio(/^Consolidado$/).focus();
    await user.keyboard('{ArrowDown}');
    expect(radio(/^Consolidado maestro$/)).toBeChecked();
    expect(radio(/^Consolidado maestro$/)).toHaveFocus();
    expect(within(dialogo()).getByText(TEXTO_CONFIRMACION_DESCARGA_MAESTROS)).toBeInTheDocument();
    for (const r of within(g).getAllByRole('radio')) {
      expect(r.className).toMatch(/focus-visible:ring-2/);
    }
  });

  it('AC7 — el foco queda atrapado (radios incluidos) y Escape cierra sin crear lote', async () => {
    const user = userEvent.setup();
    const { props } = montar();
    const d = dialogo();
    for (let i = 0; i < 6; i++) {
      await user.tab();
      expect(d.contains(document.activeElement)).toBe(true);
    }
    await user.keyboard('{Escape}');
    expect(props.onClose).toHaveBeenCalled();
    expect(props.crear).not.toHaveBeenCalled();
  });

  it('AC7 — sin colores en hex en el selector (solo tokens del tema)', () => {
    montar();
    const g = grupo();
    for (const el of [g, ...Array.from(g.querySelectorAll('*'))]) {
      expect(el.getAttribute('class') ?? '').not.toMatch(/#[0-9a-fA-F]{3,8}/);
      expect(el.getAttribute('style') ?? '').not.toMatch(/#[0-9a-fA-F]{3,8}/);
    }
  });

  it('AC7 — mientras se crea, confirmar queda deshabilitado con indicador de carga y el selector inerte', async () => {
    const user = userEvent.setup();
    let liberar: (l: LoteConsolidados) => void = () => {};
    const crear = vi.fn(() => new Promise<LoteConsolidados>((res) => (liberar = res)));
    const { props } = montar({ crear });
    await user.click(confirmar());
    const enCurso = await within(dialogo()).findByRole('button', { name: /Creando la descarga/ });
    expect(enCurso).toBeDisabled();
    expect(within(dialogo()).getByTestId('descarga-masiva-cuerpo')).toHaveAttribute('aria-busy', 'true');
    expect(radio(/^Consolidado maestro$/)).toBeDisabled();
    await act(async () => liberar(LOTE));
    await waitFor(() => expect(props.onCreado).toHaveBeenCalledWith(LOTE));
  });

  it('contrato — BotonDescargaMasivaZip pasa selectorTipo al modal', async () => {
    const user = userEvent.setup();
    const crear = vi.fn().mockResolvedValue(LOTE);
    render(<BotonDescargaMasivaZip selectorTipo crear={crear} seleccion={SELECCION} contador={118} onCreado={vi.fn()} />);
    await user.click(screen.getByRole('button', { name: /Descargar ZIP/ }));
    expect(grupo()).toBeInTheDocument();
  });
});
