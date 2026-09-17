import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

import type { InstanceSummary } from '@/lib/api/types/procedure-runtime';

/**
 * HU #12363 — Vista consolidada: selector de alcance y distinción visible de los trámites de la red.
 *
 * Uso de ejemplo: la cabeza de red (JWT `is_group_parent: true`) abre el listado; el selector
 * «Alcance» ofrece «Mi compañía», «Toda la red» y cada hijo por nombre. Con la red activa la tabla
 * consulta `searchNetworkInstances`/`searchNetworkEstadoCounts` (con `childTenantId` si se eligió
 * un hijo) y pinta la columna «Cliente». Un usuario que no es cabeza no ve nada de esto.
 *
 * AC1 — sin cabeza de grupo: ni selector ni columna, mismas llamadas que hoy.
 * AC2 — alcance propio por defecto: ninguna llamada `network/**` hasta que el usuario lo pide.
 * AC3 — con la red: columna «Cliente» + distintivo «Solo consulta» (texto e icono, no solo color).
 * AC4 — filtrar por un hijo envía `childTenantId`; el selector no ofrece clientes ajenos.
 * AC5 — preferencia por usuario en `tramites.scope` (servidor), nunca en localStorage.
 * AC6 — abrir un trámite de la red ⇒ consulta; al cerrar se conservan alcance y filtro.
 * AC7 — contrato: el cliente no manda `X-Tenant-Id` ni lista de tenants a las rutas de red.
 */

const mocks = vi.hoisted(() => ({
  listInstances: vi.fn(),
  searchInstances: vi.fn(),
  searchEstadoCounts: vi.fn(),
  searchNetworkInstances: vi.fn(),
  searchNetworkEstadoCounts: vi.fn(),
  getNetworkInstance: vi.fn(),
  getInstance: vi.fn(),
  listFilterFields: vi.fn(),
  getConsultationConfig: vi.fn(),
  setPriority: vi.fn(),
  getAttachments: vi.fn(),
  getChecklist: vi.fn(),
  getActors: vi.fn(),
  getCommercial: vi.fn(),
  getPrenda: vi.fn(),
  getPreflight: vi.fn(),
  fetchAttachmentPreviewUrl: vi.fn(),
  downloadAttachment: vi.fn(),
  downloadBiometricCertificado: vi.fn(),
  listBiometricExpediente: vi.fn(),
  startSubsanacion: vi.fn(),
  pauseInstance: vi.fn(),
  pauseInstancesMassive: vi.fn(),
  enviarAlOt: vi.fn(),
  adminListGestoresDisponibles: vi.fn(),
}));

/** Hijos de la red no-admin — HU #12555/#12556 (`GET /api/v1/tramites/network/children`). */
const fetchNetworkChildren = vi.hoisted(() => vi.fn());

const MockTramitesApiError = vi.hoisted(
  () =>
    class MockTramitesApiError extends Error {
      status: number;
      problem: Record<string, unknown> | null;
      constructor(status: number, message: string, problem: Record<string, unknown> | null = null) {
        super(message);
        this.name = 'TramitesApiError';
        this.status = status;
        this.problem = problem;
      }
    },
);

vi.mock('@/lib/api/tramites-client', () => ({
  tramitesClient: mocks,
  setActiveTramitesTenant: vi.fn(),
  fetchNetworkChildren,
  TramitesApiError: MockTramitesApiError,
  DEV_TENANT_ID: 'tenant-dev',
  DEV_USER_ID: 'user-dev',
}));

/** Preferencias por scope: `tramites.columns` y `tramites.scope` se responden por separado. */
const prefs = vi.hoisted(() => ({
  get: vi.fn(),
  put: vi.fn(),
}));
vi.mock('@/lib/api/ui-preferences', () => ({ uiPreferencesClient: prefs }));

vi.mock('@/hooks/useAccessibleModules', () => ({
  useAccessibleModules: () => ({ modules: [], loading: false, ready: true, error: null }),
}));

const routerPush = vi.hoisted(() => vi.fn());
vi.mock('next/navigation', () => ({
  useRouter: () => ({ push: routerPush, replace: vi.fn(), prefetch: vi.fn() }),
}));

vi.mock('@/components/operacion/TramiteWizard', () => ({
  TramiteWizard: () => <div data-testid="tramite-wizard">asistente</div>,
}));

import { TramitesTable } from '@/components/operacion/TramitesTable';
import { NetworkScopeSelector } from '@/components/operacion/NetworkScopeSelector';
import { ToastProvider } from '@/components/admin/Toast';
import {
  DEFAULT_TRAMITES_VISIBLE_COLUMNS,
  TRAMITES_COLUMNS,
  TRAMITES_SELECTABLE_COLUMNS,
  applyNetworkScopeColumns,
  tramitesExportFields,
} from '@/lib/tramites/tramites-table-columns';
import {
  ETIQUETA_ALCANCE_PROPIO,
  ETIQUETA_ALCANCE_RED,
  ETIQUETA_CLIENTE_HIJO,
  ETIQUETA_CLIENTE_PROPIO,
  ETIQUETA_SOLO_CONSULTA,
  optionValueToScope,
  parseNetworkScopePreference,
  scopeToOptionValue,
} from '@/lib/tramites/network-scope';

const CABEZA = '11111111-1111-1111-1111-111111111111';
const HIJO = '22222222-2222-2222-2222-222222222222';
const HIJO_2 = '33333333-3333-3333-3333-333333333333';
const AJENO = '99999999-9999-9999-9999-999999999999';

function b64(o: unknown): string {
  return btoa(JSON.stringify(o)).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '');
}

function setToken(payload: Record<string, unknown>): void {
  const token = `${b64({ alg: 'none' })}.${b64(payload)}.`;
  document.cookie = `flit_token=${token}; path=/`;
}

function tokenCabeza(): void {
  setToken({
    sub: 'user-cabeza',
    role: 'AdminCompany',
    tenant_id: CABEZA,
    tenant_type: 'CONCESION',
    is_group_parent: true,
    permissions: [],
  });
}

function tokenSinJerarquia(): void {
  setToken({
    sub: 'user-solo',
    role: 'AdminCompany',
    tenant_id: CABEZA,
    tenant_type: 'B2B',
    permissions: [],
  });
}

function clearToken(): void {
  document.cookie = 'flit_token=; path=/; Max-Age=0';
}

function makeInstance(over: Partial<InstanceSummary> & Record<string, unknown> = {}): InstanceSummary {
  return {
    id: 'inst-propio',
    referenceNumber: 'TR-PROPIO',
    modalidad: 'TRASPASO',
    estado: 'entregado',
    placa: 'AAA111',
    vin: 'VIN-PROPIO',
    vehiculoMarca: 'Toyota',
    vehiculoLinea: 'Corolla',
    compradorNombre: 'Comprador',
    compradorDocumento: '1000001',
    vendedorNombre: 'Vendedor',
    vendedorDocumento: '2000001',
    organismoTransito: null,
    pasoActual: 2,
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

/** Fila de un cliente HIJO tal como la devuelve `searchNetworkInstances`. */
function makeRed(over: Partial<InstanceSummary> & Record<string, unknown> = {}): InstanceSummary {
  return makeInstance({
    id: 'inst-red',
    referenceNumber: 'TR-RED',
    placa: 'BBB222',
    vin: 'VIN-RED',
    tenantId: HIJO,
    tenantName: 'Concesionario Hijo SAS',
    fromNetwork: true,
    ...over,
  });
}

/** Fila PROPIA de la cabeza tal como la devuelve la ruta de red (procedencia `network/**`). */
function makePropiaEnRed(): InstanceSummary {
  return makeInstance({ tenantName: 'Cabeza SAS', fromNetwork: true });
}

// HU #12555/#12556 — `GET /api/v1/tramites/network/children` ya entrega id+nombre directamente.
const HIJOS = [
  { id: HIJO, nombre: 'Concesionario Hijo SAS' },
  { id: HIJO_2, nombre: 'Autos del Norte SAS' },
];

function prefScope(value: Record<string, unknown>): void {
  prefs.get.mockImplementation(async (scope: string) => ({
    scope,
    value: scope === 'tramites.scope' ? value : {},
  }));
}

function renderTable(): ReturnType<typeof render> {
  return render(
    <ToastProvider>
      <TramitesTable />
    </ToastProvider>,
  );
}

function selector(): HTMLSelectElement {
  return screen.getByTestId('network-scope-select') as HTMLSelectElement;
}

function llamadasHechas(): string[] {
  return (Object.keys(mocks) as (keyof typeof mocks)[])
    .filter((k) => mocks[k].mock.calls.length > 0)
    .sort();
}

function cabeceras(): string[] {
  return screen.getAllByRole('columnheader').map((th) => th.textContent?.trim() ?? '');
}

beforeEach(() => {
  vi.clearAllMocks();
  mocks.searchInstances.mockImplementation(async () => {
    const items = (await mocks.listInstances()) ?? [];
    return { items, total: items.length };
  });
  mocks.listInstances.mockResolvedValue([makeInstance()]);
  mocks.searchEstadoCounts.mockResolvedValue({});
  mocks.searchNetworkInstances.mockResolvedValue({
    items: [makePropiaEnRed(), makeRed()],
    total: 2,
  });
  mocks.searchNetworkEstadoCounts.mockResolvedValue({});
  mocks.listFilterFields.mockResolvedValue([]);
  mocks.getConsultationConfig.mockResolvedValue({
    vehiclePlate: 'kyverum_runt',
    onlyOwnVehicles: false,
    blockProcedureFamily: { matriculas: false, traspaso: false, otros: false },
  });
  const detalle = {
    id: 'inst-red',
    referenceNumber: 'TR-RED',
    status: 'entregado',
    tenantId: HIJO,
    tenantName: 'Concesionario Hijo SAS',
    createdAt: '2026-06-18T00:00:00Z',
    statusHistory: [],
    fieldValues: [],
    actors: [],
  };
  mocks.getInstance.mockResolvedValue({ ...detalle, id: 'inst-propio', tenantId: CABEZA });
  mocks.getNetworkInstance.mockResolvedValue({ ...detalle, fromNetwork: true });
  mocks.getAttachments.mockResolvedValue([]);
  mocks.getChecklist.mockResolvedValue({ items: [] });
  mocks.listBiometricExpediente.mockResolvedValue({
    validations: [],
    firmaBaulPartes: [],
    firmaBaulActores: [],
  });
  prefScope({});
  prefs.put.mockImplementation(async (scope: string, value: unknown) => ({ scope, value }));
  fetchNetworkChildren.mockResolvedValue(HIJOS);
});

afterEach(() => {
  clearToken();
});

// ─────────────────────────────────────────────────────────────────────────────────────────────
describe('HU #12363 — helpers de alcance (contrato del selector y de la preferencia)', () => {
  it('la preferencia se lee de forma defensiva: solo `mode: network` (con o sin hijo) abre la red', () => {
    expect(parseNetworkScopePreference({})).toEqual({ mode: 'own' });
    expect(parseNetworkScopePreference(null)).toEqual({ mode: 'own' });
    expect(parseNetworkScopePreference({ mode: 'todo' })).toEqual({ mode: 'own' });
    expect(parseNetworkScopePreference({ mode: 'network' })).toEqual({ mode: 'network' });
    expect(parseNetworkScopePreference({ mode: 'network', childTenantId: ` ${HIJO} ` })).toEqual({
      mode: 'network',
      childTenantId: HIJO,
    });
    // Un hijo sin modo red no abre nada.
    expect(parseNetworkScopePreference({ childTenantId: HIJO })).toEqual({ mode: 'own' });
  });

  it('el valor del <select> y el alcance son biyectivos', () => {
    for (const scope of [
      { mode: 'own' as const },
      { mode: 'network' as const },
      { mode: 'network' as const, childTenantId: HIJO },
    ]) {
      expect(optionValueToScope(scopeToOptionValue(scope))).toEqual(scope);
    }
    expect(optionValueToScope('cualquier-cosa')).toEqual({ mode: 'own' });
  });

  it('«Cliente» solo existe en el alcance de red y nunca entra al selector de columnas ni al default', () => {
    expect(TRAMITES_COLUMNS.find((c) => c.key === 'cliente')?.networkOnly).toBe(true);
    expect(TRAMITES_SELECTABLE_COLUMNS.map((c) => c.key)).not.toContain('cliente');
    expect(DEFAULT_TRAMITES_VISIBLE_COLUMNS).not.toContain('cliente');
    expect(applyNetworkScopeColumns(['radicado', 'placa'], false)).toEqual(['radicado', 'placa']);
    expect(applyNetworkScopeColumns(['radicado', 'placa'], true)).toEqual(['radicado', 'placa', 'cliente']);
    // Una preferencia vieja que la trajera no la resucita fuera de la red.
    expect(applyNetworkScopeColumns(['radicado', 'cliente'], false)).toEqual(['radicado']);
    // El export del alcance de red lleva el cliente; el del alcance propio, exactamente lo de hoy.
    const conRed = tramitesExportFields(applyNetworkScopeColumns(DEFAULT_TRAMITES_VISIBLE_COLUMNS, true));
    expect(conRed.map((c) => c.id)).toContain('cliente');
    expect(conRed.find((c) => c.id === 'cliente')?.value(makeRed())).toBe('Concesionario Hijo SAS');
    expect(tramitesExportFields(DEFAULT_TRAMITES_VISIBLE_COLUMNS).map((c) => c.id)).not.toContain('cliente');
  });

  it('el control es accesible: <select> nativo con <label> asociado y opciones por nombre', () => {
    const onChange = vi.fn();
    render(
      <NetworkScopeSelector
        scope={{ mode: 'own' }}
        onChange={onChange}
        hijos={[{ id: HIJO, nombre: 'Concesionario Hijo SAS' }]}
        childrenStatus="ready"
      />,
    );
    const select = screen.getByRole('combobox', { name: /Alcance/ });
    expect(select).toBeInTheDocument();
    expect(within(select).getAllByRole('option').map((o) => o.textContent)).toEqual([
      ETIQUETA_ALCANCE_PROPIO,
      ETIQUETA_ALCANCE_RED,
      'Concesionario Hijo SAS',
    ]);
  });
});

// ─────────────────────────────────────────────────────────────────────────────────────────────
describe('HU #12363 — AC1: el selector solo existe para una cabeza de grupo', () => {
  it('un usuario sin jerarquía no ve selector ni columna «Cliente» y hace las mismas llamadas de hoy', async () => {
    tokenSinJerarquia();
    renderTable();
    await screen.findByText('AAA111');
    expect(screen.queryByTestId('network-scope-select')).not.toBeInTheDocument();
    expect(cabeceras()).not.toContain('Cliente');
    // Exactamente las llamadas del listado de hoy: nada de red, nada de hijos, nada de alcance.
    expect(llamadasHechas()).toEqual([
      'getConsultationConfig',
      'listFilterFields',
      'listInstances',
      'searchEstadoCounts',
      'searchInstances',
    ]);
    expect(fetchNetworkChildren).not.toHaveBeenCalled();
    expect(prefs.get.mock.calls.map(([s]) => s)).not.toContain('tramites.scope');
    expect(prefs.get.mock.calls.map(([s]) => s)).toContain('tramites.columns');
  });

  it('sin filtro por hijo el cuerpo enviado al servidor no lleva `childTenantId`', async () => {
    tokenSinJerarquia();
    renderTable();
    await screen.findByText('AAA111');
    const body = mocks.searchInstances.mock.calls[0][0] as Record<string, unknown>;
    expect(body).not.toHaveProperty('childTenantId');
  });
});

// ─────────────────────────────────────────────────────────────────────────────────────────────
describe('HU #12363 — AC2: alcance propio por defecto', () => {
  it('la cabeza sin preferencia guardada arranca en «Mi compañía» y no consulta la red', async () => {
    tokenCabeza();
    renderTable();
    await screen.findByText('AAA111');
    expect(selector().value).toBe('own');
    expect(mocks.searchInstances).toHaveBeenCalledTimes(1);
    expect(mocks.searchNetworkInstances).not.toHaveBeenCalled();
    expect(mocks.searchNetworkEstadoCounts).not.toHaveBeenCalled();
    expect(cabeceras()).not.toContain('Cliente');
    // La preferencia de alcance sí se consultó (para restaurarla si existiera): AC5.
    expect(prefs.get).toHaveBeenCalledWith('tramites.scope');
  });

  it('la red solo se consulta cuando el usuario la pide desde el selector', async () => {
    tokenCabeza();
    renderTable();
    await screen.findByText('AAA111');
    await userEvent.selectOptions(selector(), 'network');
    await screen.findByText('BBB222');
    expect(mocks.searchNetworkInstances).toHaveBeenCalledTimes(1);
    expect(mocks.searchNetworkEstadoCounts).toHaveBeenCalledTimes(1);
    // Y el cuerpo es el mismo que el propio (sin hijo): el servidor decide el alcance.
    const body = mocks.searchNetworkInstances.mock.calls[0][0] as Record<string, unknown>;
    expect(body.childTenantId).toBeUndefined();
    expect(body).not.toHaveProperty('filterTenantId');
  });
});

// ─────────────────────────────────────────────────────────────────────────────────────────────
describe('HU #12363 — AC3: distinción visible de los trámites de la red', () => {
  it('con la red activa cada fila dice su cliente, y la del hijo lleva el distintivo «Solo consulta»', async () => {
    tokenCabeza();
    prefScope({ mode: 'network' });
    renderTable();
    await screen.findByText('BBB222');
    expect(cabeceras()).toContain('Cliente');

    const celdaRed = screen.getByTestId('tramite-cliente-inst-red');
    expect(celdaRed).toHaveTextContent('Concesionario Hijo SAS');
    expect(celdaRed).toHaveTextContent(ETIQUETA_CLIENTE_HIJO);

    const celdaPropia = screen.getByTestId('tramite-cliente-inst-propio');
    expect(celdaPropia).toHaveTextContent('Cabeza SAS');
    expect(celdaPropia).toHaveTextContent(ETIQUETA_CLIENTE_PROPIO);

    // El distintivo es texto + icono con nombre accesible, no un color.
    const badges = screen.getAllByRole('status', { name: new RegExp(ETIQUETA_SOLO_CONSULTA) });
    expect(badges.length).toBeGreaterThan(0);
    expect(badges[0]).toHaveTextContent(ETIQUETA_SOLO_CONSULTA);
  });

  it('al volver a «Mi compañía» la columna «Cliente» desaparece y se vuelve a la ruta propia', async () => {
    tokenCabeza();
    prefScope({ mode: 'network' });
    renderTable();
    await screen.findByText('BBB222');
    await userEvent.selectOptions(selector(), 'own');
    await waitFor(() => expect(mocks.searchInstances).toHaveBeenCalled());
    await waitFor(() => expect(cabeceras()).not.toContain('Cliente'));
  });
});

// ─────────────────────────────────────────────────────────────────────────────────────────────
describe('HU #12363 — AC4: filtrar por un cliente hijo concreto', () => {
  it('elegir un hijo envía `childTenantId` en el listado y en los conteos', async () => {
    tokenCabeza();
    renderTable();
    await screen.findByText('AAA111');
    await waitFor(() =>
      expect(within(selector()).getAllByRole('option')).toHaveLength(4),
    );
    mocks.searchNetworkInstances.mockResolvedValue({ items: [makeRed()], total: 1 });
    await userEvent.selectOptions(selector(), `child:${HIJO}`);
    await screen.findByText('BBB222');
    expect(mocks.searchNetworkInstances).toHaveBeenLastCalledWith(
      expect.objectContaining({ childTenantId: HIJO }),
    );
    expect(mocks.searchNetworkEstadoCounts).toHaveBeenLastCalledWith(
      expect.objectContaining({ childTenantId: HIJO }),
    );
  });

  it('el selector ofrece SOLO los hijos de la red (ordenados por nombre) y nunca un cliente ajeno', async () => {
    tokenCabeza();
    renderTable();
    await screen.findByText('AAA111');
    await waitFor(() => expect(fetchNetworkChildren).toHaveBeenCalled());
    await waitFor(() =>
      expect(within(selector()).getAllByRole('option').map((o) => (o as HTMLOptionElement).value)).toEqual([
        'own',
        'network',
        `child:${HIJO_2}`,
        `child:${HIJO}`,
      ]),
    );
    expect(selector().innerHTML).not.toContain(AJENO);
  });

  it('una preferencia con un hijo que ya no está en la red se lee como «Toda la red» (sin filtro)', async () => {
    tokenCabeza();
    prefScope({ mode: 'network', childTenantId: AJENO });
    renderTable();
    await screen.findByText('BBB222');
    await waitFor(() => expect(selector().value).toBe('network'));
    await waitFor(() =>
      expect(mocks.searchNetworkInstances).toHaveBeenLastCalledWith(
        expect.objectContaining({ childTenantId: undefined }),
      ),
    );
  });

  it('si la lista de hijos no está disponible por un 5xx/error de red el selector degrada a «Propio | Red» sin error', async () => {
    tokenCabeza();
    fetchNetworkChildren.mockRejectedValue(new MockTramitesApiError(500, '500 Internal Server Error', null));
    renderTable();
    await screen.findByText('AAA111');
    await waitFor(() => expect(fetchNetworkChildren).toHaveBeenCalled());
    expect(within(selector()).getAllByRole('option')).toHaveLength(2);
    expect(screen.queryByText(/Internal Server Error/)).not.toBeInTheDocument();
    // Y la red sigue pudiéndose pedir.
    await userEvent.selectOptions(selector(), 'network');
    await screen.findByText('BBB222');
  });

  it('HU #12556 AC2 — un 403 (`network_scope_required`) del endpoint de hijos oculta el selector entero, no lo degrada', async () => {
    tokenCabeza();
    fetchNetworkChildren.mockRejectedValue(
      new MockTramitesApiError(403, '403 Forbidden', { error: 'network_scope_required' }),
    );
    renderTable();
    await screen.findByText('AAA111');
    await waitFor(() => expect(fetchNetworkChildren).toHaveBeenCalled());
    // El claim del JWT decía cabeza de grupo, pero el servidor lo desmiente: nada de selector ni
    // columna «Cliente», y las llamadas quedan idénticas a las de un usuario sin jerarquía (AC1).
    await waitFor(() => expect(screen.queryByTestId('network-scope-select')).not.toBeInTheDocument());
    expect(cabeceras()).not.toContain('Cliente');
    expect(mocks.searchNetworkInstances).not.toHaveBeenCalled();
    expect(screen.queryByText(/network_scope_required|Forbidden/)).not.toBeInTheDocument();
  });
});

// ─────────────────────────────────────────────────────────────────────────────────────────────
describe('HU #12363 — AC5: preferencia por usuario', () => {
  it('cambiar el alcance lo persiste en `tramites.scope` del servidor, nunca en localStorage', async () => {
    tokenCabeza();
    const setItem = vi.spyOn(Storage.prototype, 'setItem');
    renderTable();
    await screen.findByText('AAA111');
    await userEvent.selectOptions(selector(), `child:${HIJO}`);
    await waitFor(() =>
      expect(prefs.put).toHaveBeenCalledWith('tramites.scope', { mode: 'network', childTenantId: HIJO }),
    );
    await userEvent.selectOptions(selector(), 'own');
    await waitFor(() => expect(prefs.put).toHaveBeenCalledWith('tramites.scope', { mode: 'own' }));
    expect(setItem.mock.calls.filter(([k]) => String(k).includes('scope'))).toEqual([]);
    setItem.mockRestore();
  });

  it('al volver a abrir el listado se recupera la última selección (red + hijo) sin pasar por «propio»', async () => {
    tokenCabeza();
    prefScope({ mode: 'network', childTenantId: HIJO });
    mocks.searchNetworkInstances.mockResolvedValue({ items: [makeRed()], total: 1 });
    renderTable();
    await screen.findByText('BBB222');
    await waitFor(() => expect(selector().value).toBe(`child:${HIJO}`));
    expect(mocks.searchNetworkInstances).toHaveBeenCalledWith(expect.objectContaining({ childTenantId: HIJO }));
    // No hubo una primera carga «propia» que luego se reemplazara.
    expect(mocks.searchInstances).not.toHaveBeenCalled();
  });

  it('si el guardado falla se revierte al alcance anterior sin dejar la tabla en blanco', async () => {
    tokenCabeza();
    prefs.put.mockRejectedValue(new Error('boom'));
    renderTable();
    await screen.findByText('AAA111');
    await userEvent.selectOptions(selector(), 'network');
    await waitFor(() => expect(selector().value).toBe('own'));
    expect(await screen.findByText('AAA111')).toBeInTheDocument();
  });
});

// ─────────────────────────────────────────────────────────────────────────────────────────────
describe('HU #12363 — AC6: coherencia con el modo consulta', () => {
  it('abrir un trámite del hijo desde la red lo muestra en consulta y, al cerrar, se conservan alcance y filtro', async () => {
    tokenCabeza();
    prefScope({ mode: 'network', childTenantId: HIJO });
    mocks.searchNetworkInstances.mockResolvedValue({ items: [makeRed()], total: 1 });
    renderTable();
    await screen.findByText('BBB222');
    await waitFor(() => expect(selector().value).toBe(`child:${HIJO}`));
    const llamadasAntes = mocks.searchNetworkInstances.mock.calls.length;

    await userEvent.click(screen.getByRole('button', { name: 'Abrir trámite TR-RED' }));
    const dialog = await screen.findByRole('dialog');
    expect(within(dialog).getByRole('status', { name: /Solo consulta/ })).toBeInTheDocument();
    await waitFor(() => expect(mocks.getNetworkInstance).toHaveBeenCalledWith('inst-red'));
    expect(routerPush).not.toHaveBeenCalled();

    await userEvent.click(within(dialog).getByRole('button', { name: 'Cerrar' }));
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
    // Mismo alcance, mismo hijo, sin recargar con otro alcance ni reescribir la preferencia.
    expect(selector().value).toBe(`child:${HIJO}`);
    expect(mocks.searchNetworkInstances.mock.calls.length).toBe(llamadasAntes);
    expect(mocks.searchInstances).not.toHaveBeenCalled();
    expect(prefs.put).not.toHaveBeenCalledWith('tramites.scope', expect.anything());
    expect(screen.getByText('BBB222')).toBeInTheDocument();
  });
});

// ─────────────────────────────────────────────────────────────────────────────────────────────
describe('HU #12363 — AC7: el listado no es el control de acceso (contrato del cliente HTTP)', () => {
  /**
   * El rechazo real de un alcance mayor lo prueban los tests backend de #12358 (`GroupHeadRead` +
   * intersección de `childTenantId` con `TenantScope`). Aquí se fija lo que el cliente PUEDE enviar:
   * solo `childTenantId` en el cuerpo — ni `X-Tenant-Id`, ni `filterTenantId`, ni lista de tenants.
   */
  it('las rutas de red no llevan X-Tenant-Id ni lista de tenants: solo `childTenantId` en el cuerpo', async () => {
    tokenCabeza();
    const real = await vi.importActual<typeof import('@/lib/api/tramites-client')>(
      '@/lib/api/tramites-client',
    );
    const fetchMock = vi.fn(async () =>
      new Response(JSON.stringify({ items: [], total: 0 }), {
        status: 200,
        headers: { 'content-type': 'application/json' },
      }),
    );
    vi.stubGlobal('fetch', fetchMock);
    try {
      await real.tramitesClient.searchNetworkInstances({
        placa: 'BBB222',
        childTenantId: HIJO,
        // Un intento de «pedir otra compañía» por el camino del SuperAdmin se descarta.
        filterTenantId: AJENO,
        take: 10,
        skip: 0,
      });
      await real.tramitesClient.searchNetworkEstadoCounts({ childTenantId: HIJO, filterTenantId: AJENO });

      expect(fetchMock).toHaveBeenCalledTimes(2);
      for (const [url, init] of fetchMock.mock.calls as unknown as [string, RequestInit][]) {
        expect(String(url)).toMatch(/\/api\/v1\/tramites\/network\/instances\/(search|estado-counts)$/);
        const headers = init.headers as Record<string, string>;
        expect(Object.keys(headers).map((h) => h.toLowerCase())).not.toContain('x-tenant-id');
        expect(headers.Authorization).toMatch(/^Bearer /);
        const body = JSON.parse(String(init.body)) as Record<string, unknown>;
        expect(body.childTenantId).toBe(HIJO);
        expect(body).not.toHaveProperty('filterTenantId');
        expect(body).not.toHaveProperty('tenantIds');
        expect(body).not.toHaveProperty('tenantId');
        expect(JSON.stringify(body)).not.toContain(AJENO);
      }
    } finally {
      vi.unstubAllGlobals();
    }
  });
});
