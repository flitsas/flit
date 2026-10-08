import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

import type { InstanceSummary } from '@/lib/api/types/procedure-runtime';
import type { LoteConsolidados } from '@/lib/api/types-consolidado-lotes';

/**
 * HU #13419 (épica #13216) — «Descargar ZIP» desde la vista de red con el alcance de la tabla.
 *
 * Uso de ejemplo: la cabeza de red (AdminCompany, `is_group_parent`) con el permiso
 * `consolidado-masivo.download` abre /tramites con «Toda la red» (o una hija), marca filas o usa
 * «Seleccionar todos» y confirma. El cuerpo lleva `alcanceRed` en la RAÍZ ('red' o el uuid de la
 * hija) en los dos modos; el aviso global del lote dice «Red» o «Red · {hija}».
 */

const mocks = vi.hoisted(() => ({
  getCamaraComercioRequirements: vi.fn(() => Promise.resolve([])),
  listInstances: vi.fn(),
  searchInstances: vi.fn(),
  searchEstadoCounts: vi.fn(),
  searchNetworkInstances: vi.fn(),
  searchNetworkEstadoCounts: vi.fn(),
  getNetworkInstance: vi.fn(),
  getInstance: vi.fn(),
  listFilterFields: vi.fn(),
  getConsultationConfig: vi.fn(),
  listInstanceEstadoCounts: vi.fn().mockResolvedValue({}),
  getStatusHistory: vi.fn().mockResolvedValue({ items: [], total: 0, page: 1, pageSize: 50 }),
}));
const fetchNetworkChildren = vi.hoisted(() => vi.fn());
const fetchNetworkDocumentos = vi.hoisted(() => vi.fn());
const MockTramitesApiError = vi.hoisted(
  () =>
    class MockTramitesApiError extends Error {
      status: number;
      constructor(status: number, message: string) {
        super(message);
        this.name = 'TramitesApiError';
        this.status = status;
      }
    },
);
vi.mock('@/lib/api/tramites-client', () => ({
  tramitesClient: mocks,
  setActiveTramitesTenant: vi.fn(),
  fetchNetworkChildren,
  fetchNetworkDocumentos,
  TramitesApiError: MockTramitesApiError,
  DEV_TENANT_ID: 'tenant-dev',
  DEV_USER_ID: 'user-dev',
  apiUrl: (path: string) => new URL(path, 'http://localhost:3000').toString(),
  tenantHeader: () => ({}),
}));
const prefs = vi.hoisted(() => ({ get: vi.fn(), put: vi.fn() }));
vi.mock('@/lib/api/ui-preferences', () => ({ uiPreferencesClient: prefs }));
vi.mock('@/hooks/useAccessibleModules', () => ({
  useAccessibleModules: () => ({ modules: [], loading: false, ready: true, error: null }),
}));
vi.mock('next/navigation', () => ({
  useRouter: () => ({ push: vi.fn(), replace: vi.fn(), prefetch: vi.fn() }),
}));
vi.mock('@/components/operacion/TramiteWizard', () => ({
  TramiteWizard: () => <div data-testid="tramite-wizard">asistente</div>,
}));

import { TramitesTable } from '@/components/operacion/TramitesTable';
import { ToastProvider } from '@/components/admin/Toast';
import { LoteDescargaTracker, LoteDescargaTrackerProvider } from '@/components/shared/LoteDescargaTracker';
import { MENSAJES_RECHAZO_RED } from '@/lib/api/consolidado-lotes-client';
import { scopeToOptionValue } from '@/lib/tramites/network-scope';

const CABEZA = '11111111-1111-1111-1111-111111111111';
const HIJO = '22222222-2222-2222-2222-222222222222';
const HIJOS = [
  { id: HIJO, nombre: 'Concesionario Hijo SAS' },
  { id: '33333333-3333-3333-3333-333333333333', nombre: 'Autos del Norte SAS' },
];

function b64(o: unknown): string {
  return btoa(JSON.stringify(o)).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '');
}
function tokenCabeza(): void {
  const token = `${b64({ alg: 'none' })}.${b64({
    sub: 'user-cabeza',
    role: 'AdminCompany',
    tenant_id: CABEZA,
    tenant_type: 'CONCESION',
    is_group_parent: true,
    permissions: ['consolidado-masivo.download'],
  })}.`;
  document.cookie = `flit_token=${token}; path=/`;
}

/** Datos sintéticos (sin PII real). */
function fila(over: Partial<InstanceSummary> & Record<string, unknown>): InstanceSummary {
  return {
    id: 'inst-propio',
    referenceNumber: 'TR-PROPIO',
    modalidad: 'TRASPASO',
    estado: 'preparado',
    placa: 'AAA111',
    vin: 'VIN-1',
    vehiculoMarca: 'Marca',
    vehiculoLinea: 'Linea',
    compradorNombre: 'Comprador',
    compradorDocumento: '1000001',
    vendedorNombre: 'Vendedor',
    vendedorDocumento: '2000001',
    organismoTransito: null,
    pasoActual: 6,
    totalPasos: 6,
    createdAt: '2026-06-18T00:00:00Z',
    draftFinalizedAt: null,
    identityValidationStatus: null,
    signaturePending: false,
    canSubmit: false,
    prioritario: false,
    tenantId: CABEZA,
    companiaNombre: 'Cabeza SAS',
    subsanacionActiva: false,
    subsanacionCount: 0,
    ultimoRechazoMotivo: null,
    updatedAt: null,
    gestorNombre: null,
    fuente: 'dashboard',
    firmaVendedorEstado: 'pendiente',
    firmaCompradorEstado: 'pendiente',
    consolidadoAttachmentId: null,
    ...over,
  } as InstanceSummary;
}
const PROPIA_EN_RED = fila({ tenantName: 'Cabeza SAS', fromNetwork: true });
const DE_HIJA = fila({
  id: 'inst-hija',
  referenceNumber: 'TR-HIJA',
  placa: 'BBB222',
  tenantId: HIJO,
  tenantName: 'Concesionario Hijo SAS',
  fromNetwork: true,
});

const LOTE: LoteConsolidados = {
  id: '0f8c6a1e-0000-4000-8000-000000000301',
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

const json = (status: number, body: unknown) =>
  new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } });
const fetchMock = vi.fn();
function servidor(crear: () => Response, leido: LoteConsolidados = LOTE) {
  fetchMock.mockImplementation(async (url: string, init?: RequestInit) => {
    const path = new URL(url).pathname;
    if (path === '/api/v1/consolidados/lotes/actual') return new Response(null, { status: 204 });
    if (path === '/api/v1/tramites/consolidados/lotes' && init?.method === 'POST') return crear();
    if (path === `/api/v1/consolidados/lotes/${leido.id}`) return json(200, leido);
    throw new TypeError('Failed to fetch');
  });
}
const cuerpoCrear = () => {
  const llamada = fetchMock.mock.calls.find(
    ([url, init]) =>
      new URL(url as string).pathname === '/api/v1/tramites/consolidados/lotes' &&
      (init as RequestInit | undefined)?.method === 'POST',
  );
  return JSON.parse(String((llamada![1] as RequestInit).body));
};

function prefScope(value: Record<string, unknown>): void {
  prefs.get.mockImplementation(async (scope: string) => ({
    scope,
    value: scope === 'tramites.scope' ? value : {},
  }));
}

const montar = () =>
  render(
    <ToastProvider>
      <LoteDescargaTrackerProvider habilitado>
        <TramitesTable />
        <LoteDescargaTracker />
      </LoteDescargaTrackerProvider>
    </ToastProvider>,
  );
const casilla = (radicado: string) =>
  screen.getByRole('checkbox', { name: new RegExp(`Seleccionar el trámite ${radicado} para la descarga masiva`) });
const dialogo = () => screen.getByRole('dialog', { name: /Descargar consolidados en ZIP/ });

beforeEach(() => {
  vi.clearAllMocks();
  fetchMock.mockReset();
  vi.stubGlobal('fetch', fetchMock);
  mocks.searchInstances.mockResolvedValue({ items: [fila({})], total: 1 });
  mocks.listInstances.mockResolvedValue([fila({})]);
  mocks.searchEstadoCounts.mockResolvedValue({});
  mocks.searchNetworkInstances.mockResolvedValue({ items: [PROPIA_EN_RED, DE_HIJA], total: 2 });
  mocks.searchNetworkEstadoCounts.mockResolvedValue({});
  mocks.listFilterFields.mockResolvedValue([]);
  mocks.getConsultationConfig.mockResolvedValue({
    vehiclePlate: 'kyverum_runt',
    onlyOwnVehicles: false,
    blockProcedureFamily: { matriculas: false, traspaso: false, otros: false },
  });
  prefs.put.mockImplementation(async (scope: string, value: unknown) => ({ scope, value }));
  fetchNetworkChildren.mockResolvedValue(HIJOS);
  fetchNetworkDocumentos.mockResolvedValue(true);
  tokenCabeza();
});
afterEach(() => {
  vi.unstubAllGlobals();
  document.cookie = 'flit_token=; path=/; Max-Age=0';
});

describe('TramitesTable — descarga masiva desde la vista de red (HU #13419)', { timeout: 30_000 }, () => {
  it('AC1 — casillas a mano (una de una hija): alcanceRed "red" en la raíz y modo ids; aviso «Red»', async () => {
    const user = userEvent.setup();
    prefScope({ mode: 'network' });
    servidor(() => json(202, LOTE));
    montar();
    await screen.findByText('BBB222');
    await user.click(casilla('TR-PROPIO'));
    await user.click(casilla('TR-HIJA'));
    await user.click(screen.getByRole('button', { name: /Descargar ZIP/ }));
    await user.click(within(dialogo()).getByRole('button', { name: /Confirmar descarga/ }));

    const card = await screen.findByTestId('lote-descarga-card');
    const body = cuerpoCrear();
    expect(body.alcanceRed).toBe('red');
    expect(body.seleccion).toEqual({ modo: 'ids', ids: ['inst-propio', 'inst-hija'], excluidos: [], filtro: null });
    expect(within(card).getByTestId('lote-alcance-red')).toHaveTextContent(/Red$/);
  });

  it('AC2 — «Seleccionar todos» acotado a la hija: contador = total de la vista de red, uuid en la raíz y modo filtro', async () => {
    const user = userEvent.setup();
    prefScope({ mode: 'network', childTenantId: HIJO });
    mocks.searchNetworkInstances.mockResolvedValue({ items: [DE_HIJA], total: 57 });
    const creado: LoteConsolidados = { ...LOTE, total: 57, alcanceRed: 'hija', alcanceHijaId: HIJO };
    servidor(() => json(202, creado), creado);
    montar();
    await screen.findByText('BBB222');
    await waitFor(() =>
      expect(mocks.searchNetworkInstances).toHaveBeenCalledWith(expect.objectContaining({ childTenantId: HIJO })),
    );
    await user.click(screen.getByRole('checkbox', { name: /Seleccionar todos/ }));
    expect(screen.getByTestId('contador-seleccion-lote')).toHaveTextContent('57 trámites seleccionados (todos los del filtro)');
    await user.click(screen.getByRole('button', { name: /Descargar ZIP/ }));
    expect(within(dialogo()).getByText(/57 trámites seleccionados/)).toBeInTheDocument();
    expect(within(dialogo()).getByText(/Red · Concesionario Hijo SAS/)).toBeInTheDocument();
    await user.click(within(dialogo()).getByRole('button', { name: /Confirmar descarga/ }));

    const card = await screen.findByTestId('lote-descarga-card');
    const body = cuerpoCrear();
    expect(body.alcanceRed).toBe(HIJO);
    expect(body.seleccion.modo).toBe('filtro');
    expect(body.seleccion.filtro).not.toHaveProperty('alcanceRed');
    // AC2 — el total del lote creado coincide con el contador.
    expect(card).toHaveTextContent('0 / 57');
    // AC6 — acotado: «Red · nombre de la hija», resuelto por `alcanceHijaId` contra /network/children.
    await waitFor(() =>
      expect(within(card).getByTestId('lote-alcance-red')).toHaveTextContent('Red · Concesionario Hijo SAS'),
    );
  });

  it('AC3 — cambiar de toda la red a una hija, o desactivar la red, reinicia la selección', async () => {
    const user = userEvent.setup();
    prefScope({ mode: 'network' });
    servidor(() => json(202, LOTE));
    montar();
    await screen.findByText('BBB222');
    await user.click(casilla('TR-HIJA'));
    expect(screen.getByTestId('contador-seleccion-lote')).toHaveTextContent('1 trámite seleccionado');

    await user.selectOptions(screen.getByTestId('network-scope-select'), scopeToOptionValue({ mode: 'network', childTenantId: HIJO }));
    await waitFor(() =>
      expect(mocks.searchNetworkInstances).toHaveBeenCalledWith(expect.objectContaining({ childTenantId: HIJO })),
    );
    await waitFor(() => expect(casilla('TR-HIJA')).not.toBeChecked());
    expect(screen.queryByRole('button', { name: /Descargar ZIP/ })).not.toBeInTheDocument();

    await user.click(casilla('TR-HIJA'));
    expect(casilla('TR-HIJA')).toBeChecked();
    await user.selectOptions(screen.getByTestId('network-scope-select'), 'own');
    await screen.findByText('AAA111');
    await waitFor(() => expect(screen.queryByRole('button', { name: /Descargar ZIP/ })).not.toBeInTheDocument());
    expect(casilla('TR-PROPIO')).not.toBeChecked();
  });

  it('AC4/AC5 — 403 network_documents_disabled: mensaje en el modal, sin aviso de lote y «Descargar ZIP» deja de ofrecerse en la red', async () => {
    const user = userEvent.setup();
    prefScope({ mode: 'network' });
    servidor(() => json(403, { error: 'network_documents_disabled', detail: 'apagado' }));
    montar();
    await screen.findByText('BBB222');
    await user.click(casilla('TR-HIJA'));
    await user.click(screen.getByRole('button', { name: /Descargar ZIP/ }));
    await user.click(within(dialogo()).getByRole('button', { name: /Confirmar descarga/ }));
    expect(await within(dialogo()).findByText(MENSAJES_RECHAZO_RED.network_documents_disabled)).toBeInTheDocument();
    expect(screen.queryByTestId('lote-descarga-card')).not.toBeInTheDocument();

    await user.click(within(dialogo()).getByRole('button', { name: 'Cancelar' }));
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
    expect(screen.queryByRole('button', { name: /Descargar ZIP/ })).not.toBeInTheDocument();
    expect(screen.getByTestId('descarga-red-no-disponible')).toBeInTheDocument();

    // Con «Mi compañía» el botón vuelve: el interruptor solo afecta a la vista de red.
    await user.selectOptions(screen.getByTestId('network-scope-select'), 'own');
    await screen.findByText('AAA111');
    await user.click(casilla('TR-PROPIO'));
    expect(screen.getByRole('button', { name: /Descargar ZIP/ })).toBeInTheDocument();
  });

  it('borde — alcance propio (no cabeza activa en red): el cuerpo lleva alcanceRed null y el aviso no dice «Red»', async () => {
    const user = userEvent.setup();
    prefScope({});
    const propio: LoteConsolidados = { ...LOTE, total: 1, alcanceRed: undefined };
    servidor(() => json(202, propio), propio);
    montar();
    await screen.findByText('AAA111');
    await user.click(casilla('TR-PROPIO'));
    await user.click(screen.getByRole('button', { name: /Descargar ZIP/ }));
    await user.click(within(dialogo()).getByRole('button', { name: /Confirmar descarga/ }));
    const card = await screen.findByTestId('lote-descarga-card');
    expect(cuerpoCrear().alcanceRed).toBeNull();
    expect(within(card).queryByTestId('lote-alcance-red')).not.toBeInTheDocument();
  });
});

/** Promesa controlable: deja ver qué pinta la tabla mientras `documentosRed` aún no ha respondido. */
function diferido<T>() {
  let resolver!: (v: T) => void;
  let rechazar!: (e: unknown) => void;
  const promesa = new Promise<T>((res, rej) => {
    resolver = res;
    rechazar = rej;
  });
  return { promesa, resolver, rechazar };
}

describe('TramitesTable — «Descargar ZIP» según documentosRed de la vista de red (HU #13419 AC5)', { timeout: 30_000 }, () => {
  it('AC5 — documentosRed=false: «Descargar ZIP» no se ofrece desde el primer render (ni mientras se consulta)', async () => {
    const user = userEvent.setup();
    prefScope({ mode: 'network' });
    const consulta = diferido<boolean>();
    fetchNetworkDocumentos.mockReturnValue(consulta.promesa);
    servidor(() => json(202, LOTE));
    montar();
    await screen.findByText('BBB222');
    await user.click(casilla('TR-HIJA'));
    // Aún sin respuesta: fail-closed, nada que pulsar.
    expect(screen.queryByRole('button', { name: /Descargar ZIP/ })).not.toBeInTheDocument();

    consulta.resolver(false);
    expect(await screen.findByTestId('descarga-red-no-disponible')).toHaveTextContent(
      'La descarga en ZIP no está habilitada para la vista de red.',
    );
    expect(screen.queryByRole('button', { name: /Descargar ZIP/ })).not.toBeInTheDocument();
    // La consulta se hizo una vez, con señal de aborto; nunca se intentó crear el lote.
    expect(fetchNetworkDocumentos).toHaveBeenCalledTimes(1);
    expect(fetchNetworkDocumentos).toHaveBeenCalledWith(expect.any(AbortSignal));
    expect(fetchMock.mock.calls.some(([, init]) => (init as RequestInit | undefined)?.method === 'POST')).toBe(false);
  });

  it('AC5 — documentosRed=true: «Descargar ZIP» se ofrece y cambiar de toda la red a una hija no repite la consulta', async () => {
    const user = userEvent.setup();
    prefScope({ mode: 'network' });
    servidor(() => json(202, LOTE));
    montar();
    await screen.findByText('BBB222');
    await user.click(casilla('TR-HIJA'));
    expect(await screen.findByRole('button', { name: /Descargar ZIP/ })).toBeInTheDocument();

    await user.selectOptions(screen.getByTestId('network-scope-select'), scopeToOptionValue({ mode: 'network', childTenantId: HIJO }));
    await waitFor(() =>
      expect(mocks.searchNetworkInstances).toHaveBeenCalledWith(expect.objectContaining({ childTenantId: HIJO })),
    );
    await user.click(casilla('TR-HIJA'));
    expect(await screen.findByRole('button', { name: /Descargar ZIP/ })).toBeInTheDocument();
    expect(fetchNetworkDocumentos).toHaveBeenCalledTimes(1);
  });

  it.each([
    ['falla (500 / red)', () => Promise.reject(new MockTramitesApiError(500, 'boom'))],
    ['responde 403', () => Promise.reject(new MockTramitesApiError(403, 'network_scope_required'))],
  ])('AC5 borde — la consulta %s: sin «Descargar ZIP» en la red (fail-closed) y la tabla sigue funcionando', async (_c, impl) => {
    const user = userEvent.setup();
    prefScope({ mode: 'network' });
    fetchNetworkDocumentos.mockImplementation(impl);
    servidor(() => json(202, LOTE));
    montar();
    await screen.findByText('BBB222');
    await user.click(casilla('TR-HIJA'));
    expect(await screen.findByTestId('descarga-red-no-disponible')).toHaveTextContent(
      'No se pudo comprobar si la descarga en ZIP está habilitada para la vista de red.',
    );
    expect(screen.queryByRole('button', { name: /Descargar ZIP/ })).not.toBeInTheDocument();
    // El resto de la vista sigue viva: filas, selección y contador.
    expect(screen.getByText('AAA111')).toBeInTheDocument();
    expect(screen.getByTestId('contador-seleccion-lote')).toHaveTextContent('1 trámite seleccionado');
  });

  it('AC5 contrato — «Mi compañía»: el botón sigue igual y no se consulta documentosRed', async () => {
    const user = userEvent.setup();
    prefScope({});
    fetchNetworkDocumentos.mockResolvedValue(false);
    servidor(() => json(202, LOTE));
    montar();
    await screen.findByText('AAA111');
    await user.click(casilla('TR-PROPIO'));
    expect(screen.getByRole('button', { name: /Descargar ZIP/ })).toBeInTheDocument();
    expect(fetchNetworkDocumentos).not.toHaveBeenCalled();
  });
});
