import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

import type { InstanceSummary } from '@/lib/api/types/procedure-runtime';
import {
  BarraSeleccionLote,
  CasillaFilaLote,
  textoContadorLote,
  type BarraSeleccionLoteProps,
} from '@/components/operacion/BarraSeleccionLote';
import {
  CONSOLIDADO_MASIVO_DOWNLOAD_PERMISSION,
  TOKEN_STORAGE_KEY,
  canDescargarConsolidadosMasivo,
} from '@/lib/auth/jwt';

// Uso de ejemplo:
//   <BarraSeleccionLote estadoTabla="lleno" estadoCabecera="parcial" contador={348}
//     todosDelFiltro onAlternarTodos={...} onLimpiar={...} mensajeTope={null} />
//   <CasillaFilaLote radicado="TR-0001" seleccionado={false} onAlternar={...} />

// ── Mocks para montar TramitesTable (sin red real) ─────────────────────────────────────────────
const mocks = vi.hoisted(() => ({
  listInstances: vi.fn(),
  searchInstances: vi.fn(),
  searchEstadoCounts: vi.fn(),
  listFilterFields: vi.fn(),
  listInstanceEstadoCounts: vi.fn().mockResolvedValue({}),
  getConsultationConfig: vi.fn(),
  getInstance: vi.fn(),
  getStatusHistory: vi.fn().mockResolvedValue({ items: [], total: 0, page: 1, pageSize: 50 }),
}));

vi.mock('@/lib/api/tramites-client', () => ({
  tramitesClient: mocks,
  DEV_TENANT_ID: 'tenant-dev',
  DEV_USER_ID: 'user-dev',
}));
vi.mock('@/lib/api/ui-preferences', () => ({
  uiPreferencesClient: {
    get: vi.fn().mockResolvedValue({ scope: 'tramites.columns', value: {} }),
    put: vi.fn().mockResolvedValue({ scope: 'tramites.columns', value: { visible: [] } }),
  },
}));
const routerPush = vi.hoisted(() => vi.fn());
vi.mock('next/navigation', () => ({
  useRouter: () => ({ push: routerPush, replace: vi.fn(), prefetch: vi.fn() }),
}));

import { TramitesTable } from '@/components/operacion/TramitesTable';
import { ToastProvider } from '@/components/admin/Toast';

function makeToken(payload: Record<string, unknown>): string {
  const header = Buffer.from(JSON.stringify({ alg: 'none', typ: 'JWT' })).toString('base64url');
  const body = Buffer.from(JSON.stringify(payload)).toString('base64url');
  return `${header}.${body}.`;
}

/** Trámites ya radicados de prueba (datos sintéticos, sin PII). */
function makeInstances(n: number): InstanceSummary[] {
  return Array.from({ length: n }, (_, i) => {
    const num = String(i + 1).padStart(4, '0');
    return {
      id: `inst-${num}`,
      referenceNumber: `TR-${num}`,
      modalidad: 'TRASPASO',
      estado: 'preparado',
      placa: `P${num}`,
      vin: `VIN-${num}`,
      vehiculoMarca: 'Marca',
      vehiculoLinea: 'Linea',
      compradorNombre: `Comprador ${num}`,
      compradorDocumento: `100${num}`,
      vendedorNombre: `Vendedor ${num}`,
      vendedorDocumento: `200${num}`,
      organismoTransito: null,
      pasoActual: 6,
      totalPasos: 6,
      createdAt: '2026-06-18T00:00:00Z',
      draftFinalizedAt: null,
      identityValidationStatus: null,
      signaturePending: false,
      canSubmit: false,
      prioritario: false,
      tenantId: '11111111-1111-1111-1111-111111111111',
      companiaNombre: null,
      subsanacionActiva: false,
      subsanacionCount: 0,
      ultimoRechazoMotivo: null,
      updatedAt: null,
      gestorNombre: null,
      fuente: 'dashboard',
      firmaVendedorEstado: 'pendiente',
      firmaCompradorEstado: 'pendiente',
      consolidadoAttachmentId: null,
    } satisfies InstanceSummary;
  });
}

const baseProps: BarraSeleccionLoteProps = {
  estadoTabla: 'lleno',
  estadoCabecera: 'nada',
  contador: 0,
  todosDelFiltro: false,
  onAlternarTodos: () => {},
  onLimpiar: () => {},
  mensajeTope: null,
};

const casillaTodos = () => screen.getByRole('checkbox', { name: /Seleccionar todos \(del filtro\)/ });
const contador = () => screen.getByTestId('contador-seleccion-lote');

describe('BarraSeleccionLote — HU #13380', () => {
  it('AC7 — tabla llena: la cabecera refleja «nada», «parcial» (mixed) y «todo»', () => {
    const { rerender } = render(<BarraSeleccionLote {...baseProps} />);
    expect(casillaTodos()).toBeEnabled();
    expect(casillaTodos()).not.toBeChecked();
    expect(casillaTodos()).toHaveAttribute('aria-checked', 'false');

    rerender(<BarraSeleccionLote {...baseProps} estadoCabecera="parcial" contador={3} />);
    expect(casillaTodos()).toHaveAttribute('aria-checked', 'mixed');
    expect((casillaTodos() as HTMLInputElement).indeterminate).toBe(true);

    rerender(<BarraSeleccionLote {...baseProps} estadoCabecera="todo" contador={350} todosDelFiltro />);
    expect(casillaTodos()).toBeChecked();
    expect(casillaTodos()).toHaveAttribute('aria-checked', 'true');
    expect((casillaTodos() as HTMLInputElement).indeterminate).toBe(false);
  });

  it.each(['vacio', 'cargando', 'error'] as const)(
    'AC7 — tabla %s: «Seleccionar todos» deshabilitada y sin contador',
    (estadoTabla) => {
      render(
        <BarraSeleccionLote {...baseProps} estadoTabla={estadoTabla} estadoCabecera="parcial" contador={5} />,
      );
      expect(casillaTodos()).toBeDisabled();
      expect(casillaTodos()).not.toHaveAttribute('aria-checked', 'mixed');
      expect(contador()).toBeEmptyDOMElement();
    },
  );

  it('AC2 — el contador vive en una región aria-live="polite"', () => {
    render(<BarraSeleccionLote {...baseProps} contador={350} estadoCabecera="todo" todosDelFiltro />);
    expect(contador()).toHaveAttribute('aria-live', 'polite');
    expect(contador()).toHaveTextContent('350 trámites seleccionados (todos los del filtro)');
  });

  it('AC7 — la casilla de cabecera se acciona con teclado (espacio)', async () => {
    const onAlternarTodos = vi.fn();
    render(<BarraSeleccionLote {...baseProps} onAlternarTodos={onAlternarTodos} />);
    casillaTodos().focus();
    await userEvent.keyboard(' ');
    expect(onAlternarTodos).toHaveBeenCalledTimes(1);
  });

  it('AC5 — con tope alcanzado muestra el aviso que sugiere usar el filtro', () => {
    render(
      <BarraSeleccionLote
        {...baseProps}
        contador={10_000}
        estadoCabecera="parcial"
        mensajeTope="Llegaste al tope de 10.000 trámites marcados uno a uno. Usa el filtro y «Seleccionar todos» para incluir más."
      />,
    );
    expect(screen.getByRole('alert')).toHaveTextContent(/Usa el filtro/);
  });

  it('«Limpiar selección» solo aparece con algo seleccionado y llama onLimpiar', async () => {
    const onLimpiar = vi.fn();
    const { rerender } = render(<BarraSeleccionLote {...baseProps} onLimpiar={onLimpiar} />);
    expect(screen.queryByRole('button', { name: 'Limpiar selección' })).not.toBeInTheDocument();
    rerender(<BarraSeleccionLote {...baseProps} contador={2} estadoCabecera="parcial" onLimpiar={onLimpiar} />);
    await userEvent.click(screen.getByRole('button', { name: 'Limpiar selección' }));
    expect(onLimpiar).toHaveBeenCalledTimes(1);
  });

  it('contrato — textoContadorLote: singular/plural y separador de miles', () => {
    expect(textoContadorLote(1, false)).toBe('1 trámite seleccionado');
    expect(textoContadorLote(0, false)).toBe('0 trámites seleccionados');
    expect(textoContadorLote(10_000, true)).toBe('10.000 trámites seleccionados (todos los del filtro)');
  });
});

describe('CasillaFilaLote — HU #13380 AC1', () => {
  it('nombra el radicado y se opera con teclado sin disparar el clic de la fila', async () => {
    const onAlternar = vi.fn();
    const onFila = vi.fn();
    render(
      <table>
        <tbody>
          <tr onClick={onFila}>
            <td>
              <CasillaFilaLote radicado="TR-0007" seleccionado={false} onAlternar={onAlternar} />
            </td>
          </tr>
        </tbody>
      </table>,
    );
    const casilla = screen.getByRole('checkbox', { name: /TR-0007/ });
    casilla.focus();
    await userEvent.keyboard(' ');
    expect(onAlternar).toHaveBeenCalledTimes(1);
    expect(onFila).not.toHaveBeenCalled();

    await userEvent.click(casilla);
    expect(onAlternar).toHaveBeenCalledTimes(2);
    expect(onFila).not.toHaveBeenCalled();
  });
});

describe('canDescargarConsolidadosMasivo — HU #13380 AC6', () => {
  it('con el permiso o siendo SuperAdmin: sí; sin él o sin sesión: no', () => {
    expect(CONSOLIDADO_MASIVO_DOWNLOAD_PERMISSION).toBe('consolidado-masivo.download');
    expect(canDescargarConsolidadosMasivo({ permissions: ['Consolidado-Masivo.Download'] } as never)).toBe(true);
    expect(canDescargarConsolidadosMasivo({ role: 'SuperAdmin' } as never)).toBe(true);
    expect(canDescargarConsolidadosMasivo({ permissions: ['tramites.read'] } as never)).toBe(false);
    expect(canDescargarConsolidadosMasivo(null)).toBe(false);
  });
});

// Montar la tabla completa es caro en jsdom (3.000 líneas, menú y modales): margen de tiempo amplio.
describe('TramitesTable + selección de lote — HU #13380', { timeout: 30_000 }, () => {
  beforeEach(() => {
    vi.clearAllMocks();
    sessionStorage.clear();
    // Doble del servidor: aplica búsqueda, recorta por skip/take y devuelve el total del universo.
    mocks.searchInstances.mockImplementation(async (params?: unknown) => {
      const todos: InstanceSummary[] = (await mocks.listInstances(params)) ?? [];
      const p = (params ?? {}) as { busqueda?: string; skip?: number; take?: number };
      const texto = p.busqueda?.trim().toLowerCase();
      const universo = texto
        ? todos.filter((i) => `${i.placa} ${i.compradorNombre}`.toLowerCase().includes(texto))
        : todos;
      const skip = p.skip ?? 0;
      const take = p.take ?? universo.length;
      return { items: universo.slice(skip, skip + take), total: universo.length };
    });
    mocks.searchEstadoCounts.mockResolvedValue({});
    mocks.listFilterFields.mockResolvedValue([]);
    mocks.getConsultationConfig.mockResolvedValue({
      vehiclePlate: 'kyverum_runt',
      onlyOwnVehicles: false,
      blockProcedureFamily: { matriculas: false, traspaso: false, otros: false },
    });
  });

  afterEach(() => {
    window.localStorage.removeItem(TOKEN_STORAGE_KEY);
  });

  function conPermiso() {
    window.localStorage.setItem(
      TOKEN_STORAGE_KEY,
      makeToken({ sub: 'u1', role: 'Gestor', permissions: ['consolidado-masivo.download'] }),
    );
  }

  const montar = () =>
    render(
      <ToastProvider>
        <TramitesTable />
      </ToastProvider>,
    );

  it('AC6 — sin el permiso no hay casillas ni barra de selección', async () => {
    window.localStorage.setItem(
      TOKEN_STORAGE_KEY,
      makeToken({ sub: 'u1', role: 'Gestor', permissions: ['tramites.read'] }),
    );
    mocks.listInstances.mockResolvedValue(makeInstances(3));
    montar();
    await screen.findByText('P0001');
    expect(
      screen.queryByRole('region', { name: 'Acciones sobre los trámites seleccionados' }),
    ).not.toBeInTheDocument();
    expect(screen.queryByRole('checkbox', { name: /^Seleccionar el trámite/ })).not.toBeInTheDocument();
  });

  it('AC1 — con permiso cada fila tiene casilla con el radicado; marcarla no abre el detalle', async () => {
    conPermiso();
    mocks.listInstances.mockResolvedValue(makeInstances(3));
    montar();
    await screen.findByText('P0001');
    // HU #13380 (casilla única) — con el permiso el nombre es neutro: la selección sirve a varias acciones.
    const casillas = await screen.findAllByRole('checkbox', { name: /^Seleccionar el trámite TR-\d+$/ });
    expect(casillas).toHaveLength(3);
    expect(screen.getByRole('checkbox', { name: /TR-0002/ })).toBeInTheDocument();

    const casilla = screen.getByRole('checkbox', { name: /TR-0001/ });
    casilla.focus();
    await userEvent.keyboard(' ');
    expect(casilla).toBeChecked();
    expect(contador()).toHaveTextContent('1 trámite seleccionado');
    expect(routerPush).not.toHaveBeenCalled();
    expect(mocks.getInstance).not.toHaveBeenCalled();
  });

  it('AC2 — total 350 y una página visible: «todos» cuenta 350 y desmarcar 2 da 348', async () => {
    conPermiso();
    mocks.listInstances.mockResolvedValue(makeInstances(350));
    montar();
    await screen.findByText('P0001');

    await userEvent.click(casillaTodos());
    expect(contador()).toHaveTextContent('350 trámites seleccionados (todos los del filtro)');
    expect(casillaTodos()).toHaveAttribute('aria-checked', 'true');

    await userEvent.click(screen.getByRole('checkbox', { name: /TR-0001/ }));
    await userEvent.click(screen.getByRole('checkbox', { name: /TR-0002/ }));
    expect(contador()).toHaveTextContent('348 trámites seleccionados');
    expect(casillaTodos()).toHaveAttribute('aria-checked', 'mixed');
    // El total viene del servidor por la ruta filtrada (siempre con take), no de las filas pintadas.
    expect(mocks.searchInstances).toHaveBeenLastCalledWith(expect.objectContaining({ take: 10, skip: 0 }));
  });

  it('AC3 — 3 filas marcadas cuentan 3 y se conservan al cambiar de página', async () => {
    conPermiso();
    mocks.listInstances.mockResolvedValue(makeInstances(23));
    montar();
    await screen.findByText('P0001');
    for (const r of ['TR-0001', 'TR-0002', 'TR-0003']) {
      await userEvent.click(screen.getByRole('checkbox', { name: new RegExp(r) }));
    }
    expect(contador()).toHaveTextContent('3 trámites seleccionados');

    await userEvent.click(screen.getByRole('button', { name: 'Página siguiente' }));
    await screen.findByText('P0011');
    expect(contador()).toHaveTextContent('3 trámites seleccionados');

    await userEvent.click(screen.getByRole('button', { name: 'Página anterior' }));
    await screen.findByText('P0001');
    expect(screen.getByRole('checkbox', { name: /TR-0002/ })).toBeChecked();
  });

  it('AC4 — con «todos» del filtro, cambiar el filtro reinicia la selección a 0', async () => {
    conPermiso();
    mocks.listInstances.mockResolvedValue(makeInstances(23));
    montar();
    await screen.findByText('P0001');
    await userEvent.click(casillaTodos());
    expect(contador()).toHaveTextContent('23 trámites seleccionados');

    await userEvent.type(screen.getByRole('searchbox', { name: 'Buscar trámites' }), 'P0001');
    await waitFor(() =>
      expect(mocks.searchInstances).toHaveBeenLastCalledWith(
        expect.objectContaining({ busqueda: 'P0001' }),
      ),
      { timeout: 5_000 },
    );
    await waitFor(() => expect(contador()).toHaveTextContent('0 trámites seleccionados'));
    expect(casillaTodos()).not.toBeChecked();
  });

  it('AC7 — tabla vacía: la barra existe pero «Seleccionar todos» está deshabilitada y sin contador', async () => {
    conPermiso();
    mocks.listInstances.mockResolvedValue([]);
    montar();
    await screen.findByText('Aún no hay trámites');
    const region = screen.getByRole('region', { name: 'Acciones sobre los trámites seleccionados' });
    expect(within(region).getByRole('checkbox', { name: /Seleccionar todos/ })).toBeDisabled();
    expect(contador()).toBeEmptyDOMElement();
  });

  it('AC7 — error al cargar: «Seleccionar todos» deshabilitada y sin contador', async () => {
    conPermiso();
    mocks.listInstances.mockRejectedValue(new Error('fallo de red'));
    montar();
    await screen.findByText('Error al cargar trámites');
    expect(casillaTodos()).toBeDisabled();
    expect(contador()).toBeEmptyDOMElement();
  });
});
