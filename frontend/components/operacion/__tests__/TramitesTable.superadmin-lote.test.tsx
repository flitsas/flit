import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

import type { InstanceSummary } from '@/lib/api/types/procedure-runtime';
import type { LoteConsolidados } from '@/lib/api/types-consolidado-lotes';
import { TOKEN_STORAGE_KEY, canDescargarConsolidadosMasivo, decodeJwtPayload } from '@/lib/auth/jwt';
import { LoteDescargaTracker, LoteDescargaTrackerProvider } from '@/components/shared/LoteDescargaTracker';

// HU #13387 (épica #13216, Feature #13307) — descarga masiva del Super Admin desde /tramites.
//
// Uso de ejemplo (lo que hace TramitesTable para un Super Admin):
//   <BotonDescargaMasivaZip selectorTipo seleccion={lote.modelo} contador={lote.contador}
//     crear={(sel, tipo) => consolidadoLotesClient.crearLote({ seleccion: sel, tipoDocumento: tipo, cabecerasDelListado: {} })}
//     onCreado={alCrearLote} onLoteActivo={mostrarLote} />
//
// `tenantHeader` del doble devuelve un X-Tenant-Id «interno» a propósito: es lo que el cliente
// real enviaría por el JWT o el tenant activo. El listado (searchInstances) no lo lleva; la
// creación del Super Admin tampoco debe llevarlo (AC2) y la del Gestor sigue igual que antes.

const TENANT_INTERNO = '22222222-2222-2222-2222-222222222222';
const TOTAL_SERVIDOR = 120;

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
  tenantHeader: () => ({ 'X-Tenant-Id': '22222222-2222-2222-2222-222222222222' }),
}));
vi.mock('@/lib/api/ui-preferences', () => ({
  uiPreferencesClient: {
    get: vi.fn().mockResolvedValue({ scope: 'tramites.columns', value: {} }),
    put: vi.fn().mockResolvedValue({ scope: 'tramites.columns', value: { visible: [] } }),
  },
}));
vi.mock('next/navigation', () => ({
  useRouter: () => ({ push: vi.fn(), replace: vi.fn(), prefetch: vi.fn() }),
}));

import { TramitesTable } from '@/components/operacion/TramitesTable';
import { ToastProvider } from '@/components/admin/Toast';

function makeToken(payload: Record<string, unknown>): string {
  const header = Buffer.from(JSON.stringify({ alg: 'none', typ: 'JWT' })).toString('base64url');
  const body = Buffer.from(JSON.stringify(payload)).toString('base64url');
  return `${header}.${body}.`;
}
/** Trámites sintéticos (sin PII) de la compañía B. */
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
      tenantId: '55555555-5555-5555-5555-55555555555b',
      companiaNombre: 'Compañía B',
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

const json = (status: number, body: unknown) =>
  new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } });
const fetchMock = vi.fn();
/** Doble del motor (#13383 aún no existe): el total del lote es lo que congela el servidor. */
function servidor() {
  fetchMock.mockImplementation(async (url: string, init?: RequestInit) => {
    const path = new URL(url).pathname;
    if (path === '/api/v1/consolidados/lotes/actual') return new Response(null, { status: 204 });
    if (path === '/api/v1/tramites/consolidados/lotes' && init?.method === 'POST') {
      const body = JSON.parse(String(init.body));
      const total =
        body.seleccion.modo === 'filtro' ? TOTAL_SERVIDOR - body.seleccion.excluidos.length : body.seleccion.ids.length;
      const lote: LoteConsolidados = {
        id: 'lote-sa',
        estado: 'en_cola',
        tipoDocumento: body.tipoDocumento,
        total,
        procesados: 0,
        incluidos: 0,
        omitidos: 0,
        creadoEn: '2026-10-07T15:00:00Z',
        partes: [],
      };
      return json(202, lote);
    }
    if (path === '/api/v1/consolidados/lotes/lote-sa') return new Response(null, { status: 404 });
    throw new TypeError('Failed to fetch');
  });
}
const llamadaCrear = () =>
  fetchMock.mock.calls.find(
    ([url, init]) =>
      new URL(url as string).pathname === '/api/v1/tramites/consolidados/lotes' &&
      (init as RequestInit | undefined)?.method === 'POST',
  ) as [string, RequestInit] | undefined;

const SA = { sub: 'sa', role: 'SuperAdmin', tenant_id: TENANT_INTERNO };
const GESTOR = { sub: 'g', role: 'Gestor', tenant_id: TENANT_INTERNO, permissions: ['consolidado-masivo.download'] };
const SIN_PERMISO = { sub: 'r', role: 'Gestor', tenant_id: TENANT_INTERNO, permissions: ['tramites.read'] };

describe('TramitesTable — descarga masiva del Super Admin (HU #13387)', { timeout: 30_000 }, () => {
  beforeEach(() => {
    vi.clearAllMocks();
    fetchMock.mockReset();
    vi.stubGlobal('fetch', fetchMock);
    sessionStorage.clear();
    servidor();
    const todos = makeInstances(TOTAL_SERVIDOR);
    mocks.searchInstances.mockImplementation(async (params?: { skip?: number; take?: number }) => {
      const skip = params?.skip ?? 0;
      const take = params?.take ?? 10;
      return { items: todos.slice(skip, skip + take), total: TOTAL_SERVIDOR };
    });
    mocks.listInstances.mockResolvedValue(todos);
    mocks.searchEstadoCounts.mockResolvedValue({});
    mocks.listFilterFields.mockResolvedValue([]);
    mocks.getConsultationConfig.mockResolvedValue({
      vehiclePlate: 'kyverum_runt',
      onlyOwnVehicles: false,
      blockProcedureFamily: { matriculas: false, traspaso: false, otros: false },
    });
  });
  afterEach(() => {
    vi.unstubAllGlobals();
    window.localStorage.clear();
  });

  const token = (payload: Record<string, unknown>) =>
    window.localStorage.setItem(TOKEN_STORAGE_KEY, makeToken(payload));
  const montar = () =>
    render(
      <ToastProvider>
        <LoteDescargaTrackerProvider habilitado>
          <TramitesTable />
          <LoteDescargaTracker />
        </LoteDescargaTrackerProvider>
      </ToastProvider>,
    );
  const contador = () => screen.getByTestId('contador-seleccion-lote');
  const dialogo = () => screen.getByRole('dialog', { name: /Descargar consolidados en ZIP/ });

  it('AC1 + AC2 — «Seleccionar todos» (120) menos 2 = 118; el lote creado tiene 118 y va sin X-Tenant-Id', async () => {
    const user = userEvent.setup();
    token(SA);
    montar();
    await screen.findByText('P0001');
    await user.click(screen.getByRole('checkbox', { name: /Seleccionar todos \(del filtro\)/ }));
    await user.click(screen.getByRole('checkbox', { name: /TR-0001/ }));
    await user.click(screen.getByRole('checkbox', { name: /TR-0002/ }));
    expect(contador()).toHaveTextContent('118 trámites seleccionados');

    await user.click(screen.getByRole('button', { name: /Descargar ZIP/ }));
    await user.click(within(dialogo()).getByRole('radio', { name: /^Consolidado maestro$/ }));
    await user.click(within(dialogo()).getByRole('button', { name: /Confirmar descarga/ }));

    const card = await screen.findByTestId('lote-descarga-card');
    expect(card).toHaveTextContent('0 / 118');
    const [, init] = llamadaCrear()!;
    const headers = init.headers as Record<string, string>;
    expect(Object.keys(headers).map((k) => k.toLowerCase())).not.toContain('x-tenant-id');
    const body = JSON.parse(String(init.body));
    expect(body.tipoDocumento).toBe('consolidado_maestro');
    expect(body.seleccion.modo).toBe('filtro');
    expect([...body.seleccion.excluidos].sort()).toEqual(['inst-0001', 'inst-0002']);
  });

  it('AC2 / AC3 (Gestor sin cambios) — sin selector, tipo consolidado y las mismas cabeceras de antes', async () => {
    const user = userEvent.setup();
    token(GESTOR);
    montar();
    await screen.findByText('P0001');
    await user.click(screen.getByRole('checkbox', { name: /TR-0001/ }));
    await user.click(screen.getByRole('button', { name: /Descargar ZIP/ }));
    expect(within(dialogo()).queryByRole('radio')).not.toBeInTheDocument();
    await user.click(within(dialogo()).getByRole('button', { name: /Confirmar descarga/ }));
    await waitFor(() => expect(llamadaCrear()).toBeDefined());
    const [, init] = llamadaCrear()!;
    expect((init.headers as Record<string, string>)['X-Tenant-Id']).toBe(TENANT_INTERNO);
    expect(JSON.parse(String(init.body)).tipoDocumento).toBe('consolidado');
  });

  it('AC5 — Super Admin sin el claim ve las casillas y «Descargar ZIP»', async () => {
    const user = userEvent.setup();
    token(SA);
    expect(canDescargarConsolidadosMasivo(decodeJwtPayload(makeToken(SA)))).toBe(true);
    montar();
    await screen.findByText('P0001');
    await user.click(screen.getByRole('checkbox', { name: /TR-0001/ }));
    expect(screen.getByRole('button', { name: /Descargar ZIP/ })).toBeInTheDocument();
  });

  it('AC5 — un usuario no Super Admin sin el permiso no ve casillas ni botón', async () => {
    token(SIN_PERMISO);
    expect(canDescargarConsolidadosMasivo(decodeJwtPayload(makeToken(SIN_PERMISO)))).toBe(false);
    montar();
    await screen.findByText('P0001');
    expect(screen.queryByRole('checkbox', { name: /TR-0001/ })).not.toBeInTheDocument();
    expect(screen.queryByRole('checkbox', { name: /Seleccionar todos/ })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /Descargar ZIP/ })).not.toBeInTheDocument();
  });

  it('AC6 — 403 al crear: mensaje de reintento y la selección se conserva', async () => {
    const user = userEvent.setup();
    token(SA);
    const base = fetchMock.getMockImplementation()!;
    fetchMock.mockImplementation(async (url: string, init?: RequestInit) => {
      if (new URL(url).pathname === '/api/v1/tramites/consolidados/lotes' && init?.method === 'POST') {
        return json(403, { error: 'forbidden' });
      }
      return base(url, init);
    });
    montar();
    await screen.findByText('P0001');
    await user.click(screen.getByRole('checkbox', { name: /TR-0003/ }));
    await user.click(screen.getByRole('button', { name: /Descargar ZIP/ }));
    await user.click(within(dialogo()).getByRole('button', { name: /Confirmar descarga/ }));
    expect(await within(dialogo()).findByRole('alert')).toHaveTextContent(
      'No se pudo completar la descarga, intente de nuevo',
    );
    expect(contador()).toHaveTextContent('1 trámite seleccionado');
    expect(screen.getByRole('checkbox', { name: /TR-0003/ })).toBeChecked();
  });
});
