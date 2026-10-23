// HU #13394 (épica #13216, Feature #13308) — variante «maestro» de la confirmación de descarga
// masiva y creación inyectada. La bandeja del OT reutiliza el modal de #13381 con el texto del
// maestro (CF-11) y su propio cliente (`crearLoteConsolidadosOt`), sin duplicar modal ni botón. El
// texto del Gestor no cambia (sus pruebas siguen en DescargaMasivaConfirmModal.test.tsx).
//
// Uso de ejemplo:
//   <BotonDescargaMasivaZip variante="maestro" seleccion={lote.modelo} contador={lote.contador}
//     crear={(sel) => crearLoteConsolidadosOt({ tipoDocumento: 'consolidado_maestro', confirmaEfectos: true, seleccion: sel })}
//     onCreado={alCrear} onLoteActivo={mostrarLote} />
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

import type { LoteConsolidados } from '@/lib/api/types-consolidado-lotes';
import type { ModeloSeleccionLote } from '@/hooks/useSeleccionLote';
import { ConsolidadoLotesApiError } from '@/lib/api/consolidado-lotes-client';
import {
  BotonDescargaMasivaZip,
  DescargaMasivaConfirmModal,
  TEXTO_CONFIRMACION_DESCARGA_MAESTROS,
  TEXTO_CONFIRMACION_DESCARGA_MASIVA,
} from '@/components/operacion/DescargaMasivaConfirmModal';

const LOTE: LoteConsolidados = {
  id: '0f8c6a1e-0000-4000-8000-000000000001',
  estado: 'en_cola',
  tipoDocumento: 'consolidado_maestro',
  total: 2,
  procesados: 0,
  incluidos: 0,
  omitidos: 0,
  creadoEn: '2026-10-07T15:00:00Z',
  partes: [],
};
const SELECCION: ModeloSeleccionLote<Record<string, unknown>> = {
  modo: 'ids',
  ids: ['proc-0001', 'proc-0003'],
  excluidos: [],
  filtro: null,
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
    contador: 2,
    onCreado: vi.fn(),
    onLoteActivo: vi.fn(),
    ...over,
  };
  render(<DescargaMasivaConfirmModal {...props} />);
  return props;
}
const dialogo = () => screen.getByRole('dialog');

describe('DescargaMasivaConfirmModal — variante maestro y creación inyectada (HU #13394)', () => {
  it('AC1 — variante maestro: texto aprobado exacto (CF-11) y sin selector de tipo', () => {
    montar({ variante: 'maestro' });
    expect(TEXTO_CONFIRMACION_DESCARGA_MAESTROS).toBe(
      'Se descargará el consolidado maestro que cada trámite tiene guardado, tal como está, aunque no refleje cambios posteriores. Solo se generará el consolidado maestro de los trámites que todavía no tienen uno.',
    );
    expect(within(dialogo()).getByText(TEXTO_CONFIRMACION_DESCARGA_MAESTROS)).toBeInTheDocument();
    expect(within(dialogo()).queryByText(TEXTO_CONFIRMACION_DESCARGA_MASIVA)).not.toBeInTheDocument();
    expect(within(dialogo()).queryByRole('combobox')).not.toBeInTheDocument();
    expect(within(dialogo()).queryByRole('radio')).not.toBeInTheDocument();
  });

  it('contrato — sin variante sigue el texto del Gestor (no cambia)', () => {
    montar();
    expect(within(dialogo()).getByText(TEXTO_CONFIRMACION_DESCARGA_MASIVA)).toBeInTheDocument();
  });

  it('AC2 — confirmar usa la función de creación inyectada con la selección, sin la ruta del Gestor', async () => {
    const user = userEvent.setup();
    const crear = vi.fn().mockResolvedValue(LOTE);
    const props = montar({ variante: 'maestro', crear });
    await user.click(within(dialogo()).getByRole('button', { name: /Confirmar descarga/ }));
    await waitFor(() => expect(props.onCreado).toHaveBeenCalledWith(LOTE));
    expect(crear).toHaveBeenCalledWith(SELECCION);
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it('AC5 — 403: mensaje de sin permiso y no se ofrece reintentar', async () => {
    const user = userEvent.setup();
    const crear = vi.fn().mockRejectedValue(new ConsolidadoLotesApiError(403, null));
    montar({ variante: 'maestro', crear });
    const confirmar = within(dialogo()).getByRole('button', { name: /Confirmar descarga/ });
    await user.click(confirmar);
    expect(await within(dialogo()).findByRole('alert')).toHaveTextContent(/No tienes permiso/);
    expect(within(dialogo()).getByRole('button', { name: /Confirmar descarga/ })).toBeDisabled();
    expect(crear).toHaveBeenCalledTimes(1);
  });

  it('BotonDescargaMasivaZip pasa variante y crear al modal', async () => {
    const user = userEvent.setup();
    const crear = vi.fn().mockResolvedValue(LOTE);
    const onCreado = vi.fn();
    render(
      <BotonDescargaMasivaZip variante="maestro" crear={crear} seleccion={SELECCION} contador={2} onCreado={onCreado} />,
    );
    await user.click(screen.getByRole('button', { name: /Descargar ZIP/ }));
    expect(within(dialogo()).getByText(TEXTO_CONFIRMACION_DESCARGA_MAESTROS)).toBeInTheDocument();
    await user.click(within(dialogo()).getByRole('button', { name: /Confirmar descarga/ }));
    await waitFor(() => expect(onCreado).toHaveBeenCalledWith(LOTE));
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
  });
});
