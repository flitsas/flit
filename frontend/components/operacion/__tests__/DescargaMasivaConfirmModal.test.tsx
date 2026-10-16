import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { act, render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

import type { InstanceSummary } from '@/lib/api/types/procedure-runtime';
import type { LoteConsolidados } from '@/lib/api/types-consolidado-lotes';
import type { ModeloSeleccionLote } from '@/hooks/useSeleccionLote';
import {
  BotonDescargaMasivaZip,
  DescargaMasivaConfirmModal,
  TEXTO_CONFIRMACION_DESCARGA_MASIVA,
} from '@/components/operacion/DescargaMasivaConfirmModal';
import { TOKEN_STORAGE_KEY } from '@/lib/auth/jwt';

// Uso de ejemplo:
//   <DescargaMasivaConfirmModal open seleccion={lote.modelo} contador={lote.contador}
//     onClose={cerrar} onCreado={(l) => lote.limpiar()} onLoteActivo={(id) => seguir(id)} />
//   <BotonDescargaMasivaZip seleccion={lote.modelo} contador={lote.contador} onCreado={lote.limpiar} />

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
  apiUrl: (path: string) => new URL(path, 'http://localhost:3000').toString(),
  tenantHeader: () => ({}),
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

/** Lote de prueba según `LoteConsolidados` del contrato §5 (datos sintéticos). */
const LOTE: LoteConsolidados = {
  id: '0f8c6a1e-0000-4000-8000-000000000001',
  estado: 'en_cola',
  tipoDocumento: 'consolidado',
  total: 2,
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
const LOTE_ACTIVO: LoteConsolidados = {
  ...LOTE,
  id: '0f8c6a1e-0000-4000-8000-000000000099',
  estado: 'en_proceso',
  total: 120,
  procesados: 45,
  incluidos: 44,
  omitidos: 1,
};

const SELECCION: ModeloSeleccionLote<Record<string, unknown>> = {
  modo: 'ids',
  ids: ['inst-0001', 'inst-0003'],
  excluidos: [],
  filtro: null,
};

const fetchMock = vi.fn();
function respuesta(status: number, body?: unknown): Response {
  return new Response(body === undefined ? null : JSON.stringify(body), {
    status,
    headers: { 'Content-Type': 'application/json' },
  });
}
/** Enruta el doble del servidor: crear lote y leer un lote por id; lo demás, sin red. */
function servidor(crear: () => Promise<Response> | Response, leer?: () => Response) {
  fetchMock.mockImplementation(async (url: string, init?: RequestInit) => {
    const path = new URL(url).pathname;
    if (path === '/api/v1/tramites/consolidados/lotes' && init?.method === 'POST') return crear();
    if (path.startsWith('/api/v1/consolidados/lotes/') && leer) return leer();
    throw new TypeError('Failed to fetch');
  });
}
const llamadasCrear = () =>
  fetchMock.mock.calls.filter(
    ([url, init]) =>
      new URL(url as string).pathname === '/api/v1/tramites/consolidados/lotes' &&
      (init as RequestInit | undefined)?.method === 'POST',
  );

beforeEach(() => {
  fetchMock.mockReset();
  vi.stubGlobal('fetch', fetchMock);
});
afterEach(() => {
  vi.unstubAllGlobals();
  window.localStorage.removeItem(TOKEN_STORAGE_KEY);
});

function montarModal(over: Partial<Parameters<typeof DescargaMasivaConfirmModal>[0]> = {}) {
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
const dialogo = () => screen.getByRole('dialog', { name: /Descargar consolidados en ZIP/ });
const botonConfirmar = () => within(dialogo()).getByRole('button', { name: /Confirmar descarga/ });

describe('DescargaMasivaConfirmModal — HU #13381', () => {
  it('AC1 — muestra el texto aprobado exacto y el número de trámites', () => {
    montarModal();
    expect(TEXTO_CONFIRMACION_DESCARGA_MASIVA).toBe(
      'Se descargará el consolidado que cada trámite tiene guardado, tal como está, aunque no refleje cambios posteriores. Solo se generará el consolidado de los trámites que todavía no tienen uno.',
    );
    expect(within(dialogo()).getByText(TEXTO_CONFIRMACION_DESCARGA_MASIVA)).toBeInTheDocument();
    expect(within(dialogo()).getByText(/2 trámites seleccionados/)).toBeInTheDocument();
  });

  it('AC1 — el foco entra al modal y Tab / Shift+Tab quedan atrapados', async () => {
    const user = userEvent.setup();
    montarModal();
    const d = dialogo();
    expect(d.contains(document.activeElement)).toBe(true);
    expect(document.activeElement).toHaveAccessibleName('Cancelar');
    for (let i = 0; i < 5; i++) {
      await user.tab();
      expect(d.contains(document.activeElement)).toBe(true);
    }
    for (let i = 0; i < 5; i++) {
      await user.tab({ shift: true });
      expect(d.contains(document.activeElement)).toBe(true);
    }
  });

  it('AC1 — Escape cierra sin crear lote', async () => {
    const user = userEvent.setup();
    const props = montarModal();
    await user.keyboard('{Escape}');
    expect(props.onClose).toHaveBeenCalled();
    expect(fetchMock).not.toHaveBeenCalled();
    expect(props.onCreado).not.toHaveBeenCalled();
  });

  it('AC1 — «Cancelar» cierra sin crear lote', async () => {
    const user = userEvent.setup();
    const props = montarModal();
    await user.click(within(dialogo()).getByRole('button', { name: 'Cancelar' }));
    expect(props.onClose).toHaveBeenCalled();
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it('AC2 — confirmar envía POST con confirmaEfectos=true y la selección, y avisa la creación', async () => {
    const user = userEvent.setup();
    servidor(() => respuesta(202, LOTE));
    const props = montarModal();
    await user.click(botonConfirmar());
    await waitFor(() => expect(props.onCreado).toHaveBeenCalledWith(LOTE));
    const [, init] = llamadasCrear()[0] as [string, RequestInit];
    expect(JSON.parse(String(init.body))).toEqual({
      tipoDocumento: 'consolidado',
      confirmaEfectos: true,
      seleccion: SELECCION,
    });
  });

  it('estado cargando — mientras se crea: botón deshabilitado, aria-busy y Escape inerte', async () => {
    const user = userEvent.setup();
    let liberar: (r: Response) => void = () => {};
    servidor(() => new Promise<Response>((res) => (liberar = res)));
    const props = montarModal();
    await user.click(botonConfirmar());
    const enCurso = await within(dialogo()).findByRole('button', { name: /Creando la descarga/ });
    expect(enCurso).toBeDisabled();
    expect(within(dialogo()).getByTestId('descarga-masiva-cuerpo')).toHaveAttribute('aria-busy', 'true');
    await user.keyboard('{Escape}');
    expect(props.onClose).not.toHaveBeenCalled();
    await act(async () => liberar(respuesta(202, LOTE)));
    await waitFor(() => expect(props.onCreado).toHaveBeenCalled());
  });

  it('AC3 — 409 lote_activo: muestra el lote activo con el aviso y entrega el id', async () => {
    const user = userEvent.setup();
    servidor(
      () => respuesta(409, { error: 'lote_activo', loteActivoId: LOTE_ACTIVO.id }),
      () => respuesta(200, LOTE_ACTIVO),
    );
    const props = montarModal();
    await user.click(botonConfirmar());
    expect(await within(dialogo()).findByText(/Ya hay una descarga en curso/)).toBeInTheDocument();
    expect(await within(dialogo()).findByText(/45 de 120 trámites procesados/)).toBeInTheDocument();
    expect(props.onLoteActivo).toHaveBeenCalledWith(LOTE_ACTIVO.id);
    expect(props.onCreado).not.toHaveBeenCalled();
    expect(within(dialogo()).queryByRole('button', { name: /Confirmar descarga/ })).not.toBeInTheDocument();
  });

  it('AC3 — 409 sin poder leer el lote: el aviso se muestra igual', async () => {
    const user = userEvent.setup();
    servidor(
      () => respuesta(409, { error: 'lote_activo', loteActivoId: LOTE_ACTIVO.id }),
      () => respuesta(404),
    );
    montarModal();
    await user.click(botonConfirmar());
    expect(await within(dialogo()).findByText(/Ya hay una descarga en curso/)).toBeInTheDocument();
  });

  it('AC4 — 422: muestra el mensaje de tope con `total`/`tope` del servidor (HU #13420) y no crea', async () => {
    const user = userEvent.setup();
    servidor(() => respuesta(422, { error: 'seleccion_excede_tope', total: 12345, tope: 5000 }));
    const props = montarModal();
    await user.click(botonConfirmar());
    const alerta = await within(dialogo()).findByRole('alert');
    expect(alerta).toHaveTextContent(/12\.345 trámites y supera el tope de 5\.000/);
    expect(props.onCreado).not.toHaveBeenCalled();
  });

  it.each([
    ['503 motor_inactivo', () => respuesta(503, { error: 'motor_inactivo' })],
    ['falla de red', () => Promise.reject(new TypeError('Failed to fetch'))],
  ])('AC4 — %s: «No se pudo completar la descarga, intente de nuevo» y se puede reintentar', async (_n, crear) => {
    const user = userEvent.setup();
    servidor(crear as () => Promise<Response>);
    const props = montarModal();
    await user.click(botonConfirmar());
    const alerta = await within(dialogo()).findByRole('alert');
    expect(alerta).toHaveTextContent('No se pudo completar la descarga, intente de nuevo');
    expect(props.onCreado).not.toHaveBeenCalled();
    expect(props.onClose).not.toHaveBeenCalled();
    expect(botonConfirmar()).toBeEnabled();
  });

  it('estado vacío — sin trámites seleccionados no se puede confirmar', () => {
    montarModal({ contador: 0, seleccion: { modo: 'ids', ids: [], excluidos: [], filtro: null } });
    expect(within(dialogo()).getByText(/No hay trámites seleccionados/)).toBeInTheDocument();
    expect(botonConfirmar()).toBeDisabled();
  });

  it('a11y — todos los botones tienen nombre accesible', () => {
    montarModal();
    within(dialogo())
      .getAllByRole('button')
      .forEach((b) => expect(b).toHaveAccessibleName());
  });
});

describe('BotonDescargaMasivaZip — HU #13381', () => {
  it('sin trámites seleccionados no se muestra', () => {
    render(
      <BotonDescargaMasivaZip
        seleccion={{ modo: 'ids', ids: [], excluidos: [], filtro: null }}
        contador={0}
        onCreado={vi.fn()}
      />,
    );
    expect(screen.queryByRole('button', { name: /Descargar ZIP/ })).not.toBeInTheDocument();
  });

  it('AC2 — abre el modal; al crear, lo cierra y avisa al dueño de la selección', async () => {
    const user = userEvent.setup();
    servidor(() => respuesta(202, LOTE));
    const onCreado = vi.fn();
    render(<BotonDescargaMasivaZip seleccion={SELECCION} contador={2} onCreado={onCreado} />);
    await user.click(screen.getByRole('button', { name: /Descargar ZIP/ }));
    await user.click(botonConfirmar());
    await waitFor(() => expect(onCreado).toHaveBeenCalledWith(LOTE));
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
  });
});

// ── Integración con el listado ─────────────────────────────────────────────────────────────────

function makeToken(payload: Record<string, unknown>): string {
  const header = Buffer.from(JSON.stringify({ alg: 'none', typ: 'JWT' })).toString('base64url');
  const body = Buffer.from(JSON.stringify(payload)).toString('base64url');
  return `${header}.${body}.`;
}

/** Trámites de prueba (datos sintéticos, sin PII). */
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

describe('TramitesTable + «Descargar ZIP» — HU #13381', { timeout: 30_000 }, () => {
  beforeEach(() => {
    vi.clearAllMocks();
    sessionStorage.clear();
    mocks.searchInstances.mockImplementation(async (params?: unknown) => {
      const todos: InstanceSummary[] = (await mocks.listInstances(params)) ?? [];
      const p = (params ?? {}) as { skip?: number; take?: number };
      const skip = p.skip ?? 0;
      const take = p.take ?? todos.length;
      return { items: todos.slice(skip, skip + take), total: todos.length };
    });
    mocks.searchEstadoCounts.mockResolvedValue({});
    mocks.listFilterFields.mockResolvedValue([]);
    mocks.getConsultationConfig.mockResolvedValue({
      vehiclePlate: 'kyverum_runt',
      onlyOwnVehicles: false,
      blockProcedureFamily: { matriculas: false, traspaso: false, otros: false },
    });
    mocks.listInstances.mockResolvedValue(makeInstances(3));
  });

  const token = (permissions: string[]) =>
    window.localStorage.setItem(TOKEN_STORAGE_KEY, makeToken({ sub: 'u1', role: 'Gestor', permissions }));
  const montar = () =>
    render(
      <ToastProvider>
        <TramitesTable />
      </ToastProvider>,
    );
  const contador = () => screen.getByTestId('contador-seleccion-lote');

  it('AC5 — sin consolidado-masivo.download no hay botón «Descargar ZIP»', async () => {
    token(['tramites.read']);
    montar();
    await screen.findByText('P0001');
    expect(screen.queryByRole('button', { name: /Descargar ZIP/ })).not.toBeInTheDocument();
  });

  it('AC5 — con el permiso, el botón aparece solo con al menos 1 trámite seleccionado', async () => {
    const user = userEvent.setup();
    token(['consolidado-masivo.download']);
    montar();
    await screen.findByText('P0001');
    expect(screen.queryByRole('button', { name: /Descargar ZIP/ })).not.toBeInTheDocument();
    await user.click(await screen.findByRole('checkbox', { name: /TR-0001/ }));
    expect(screen.getByRole('button', { name: /Descargar ZIP/ })).toBeInTheDocument();
  });

  it('AC2 — confirmar crea el lote con la selección, cierra el modal y limpia la selección', async () => {
    const user = userEvent.setup();
    token(['consolidado-masivo.download']);
    servidor(() => respuesta(202, LOTE));
    montar();
    await screen.findByText('P0001');
    await user.click(await screen.findByRole('checkbox', { name: /TR-0001/ }));
    await user.click(screen.getByRole('checkbox', { name: /TR-0003/ }));
    await user.click(screen.getByRole('button', { name: /Descargar ZIP/ }));
    await user.click(botonConfirmar());

    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
    const body = JSON.parse(String((llamadasCrear()[0] as [string, RequestInit])[1].body));
    expect(body.confirmaEfectos).toBe(true);
    expect(body.seleccion).toMatchObject({ modo: 'ids', filtro: null, excluidos: [] });
    expect([...body.seleccion.ids].sort()).toEqual(['inst-0001', 'inst-0003']);
    expect(contador()).toHaveTextContent('0 trámites seleccionados');
    expect(screen.getByRole('checkbox', { name: /TR-0001/ })).not.toBeChecked();
    expect(screen.queryByRole('button', { name: /Descargar ZIP/ })).not.toBeInTheDocument();
  });

  it('AC4 — 503: el mensaje de reintento se muestra y la selección se conserva', async () => {
    const user = userEvent.setup();
    token(['consolidado-masivo.download']);
    servidor(() => respuesta(503, { error: 'motor_inactivo' }));
    montar();
    await screen.findByText('P0001');
    await user.click(await screen.findByRole('checkbox', { name: /TR-0002/ }));
    await user.click(screen.getByRole('button', { name: /Descargar ZIP/ }));
    await user.click(botonConfirmar());
    expect(await within(dialogo()).findByRole('alert')).toHaveTextContent(
      'No se pudo completar la descarga, intente de nuevo',
    );
    await user.click(within(dialogo()).getByRole('button', { name: 'Cancelar' }));
    expect(contador()).toHaveTextContent('1 trámite seleccionado');
    expect(screen.getByRole('checkbox', { name: /TR-0002/ })).toBeChecked();
  });
});
