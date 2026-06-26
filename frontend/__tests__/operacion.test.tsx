import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

import type {
  InstanceSummary,
  WizardState,
} from '@/lib/api/types/procedure-runtime';

// ── Mock del cliente HTTP (sin red real) ───────────────────────────
const mocks = vi.hoisted(() => ({
  listPublishedProcedureTypes: vi.fn(),
  getConfiguration: vi.fn(),
  createInstance: vi.fn(),
  getInstance: vi.fn(),
  patchFieldValues: vi.fn(),
  submitInstance: vi.fn(),
  runConsultation: vi.fn(),
  // wizard server-driven (Slice 4b)
  getWizardState: vi.fn(),
  runPreflight: vi.fn(),
  getPreflight: vi.fn(),
  getCommercial: vi.fn(),
  putCommercial: vi.fn(),
  getActors: vi.fn(),
  saveActors: vi.fn(),
  getChecklist: vi.fn(),
  getAttachments: vi.fn(),
  // Slice M6 — listado de instancias para la tabla "Trámites en curso".
  listInstances: vi.fn(),
}));

vi.mock('@/lib/api/tramites-client', () => ({
  tramitesClient: mocks,
  DEV_TENANT_ID: 'tenant-dev',
  DEV_USER_ID: 'user-dev',
}));

// La tabla embebida (TramitesTable) usa useRouter para abrir cada fila; en
// jsdom no hay router de Next montado, así que lo stubeamos.
const routerPush = vi.hoisted(() => vi.fn());
vi.mock('next/navigation', () => ({
  useRouter: () => ({ push: routerPush, replace: vi.fn(), prefetch: vi.fn() }),
}));

import { OperacionView } from '@/components/operacion/OperacionView';

const TRASPASO_WIZARD: WizardState = {
  modalidad: 'traspaso',
  tipologiaCodigo: 'traspaso',
  totalSteps: 6,
  canSubmit: false,
  blockers: ['documentos_incompletos'],
  steps: [
    { index: 0, key: 'consulta', label: 'Consulta', status: 'complete', reasons: [] },
    { index: 1, key: 'documentos', label: 'Documentos', status: 'incomplete', reasons: ['documentos_incompletos'] },
    { index: 2, key: 'vendedor', label: 'Vendedor', status: 'incomplete', reasons: ['vendedor_incompleto'] },
    { index: 3, key: 'comprador', label: 'Comprador', status: 'locked', reasons: [] },
    { index: 4, key: 'comercial', label: 'Comercial', status: 'locked', reasons: [] },
    { index: 5, key: 'fur', label: 'FUR', status: 'locked', reasons: [] },
  ],
};

beforeEach(() => {
  vi.clearAllMocks();
  mocks.createInstance.mockResolvedValue({
    id: 'inst-1',
    referenceNumber: 'TR-001',
    status: 'draft',
    procedureTypeId: null,
    tenantId: 'tenant-dev',
    createdAt: '2026-06-18T00:00:00Z',
  });
  mocks.getInstance.mockResolvedValue({ id: 'inst-1', fieldValues: [] });
  mocks.patchFieldValues.mockResolvedValue({ id: 'inst-1', fieldValues: [] });
  mocks.getWizardState.mockResolvedValue(TRASPASO_WIZARD);
  mocks.runPreflight.mockResolvedValue({
    overall: 'yellow',
    createdAt: '2026-06-18T00:00:00Z',
    checks: [
      { key: 'runt', label: 'RUNT', status: 'ok', source: 'RUNT', message: 'ok' },
    ],
  });
  mocks.getPreflight.mockResolvedValue(null);
  mocks.getCommercial.mockResolvedValue({
    valorVenta: null, causal: null, tasaImpuesto: null, derechos: null, metodoPago: null,
  });
  mocks.getActors.mockResolvedValue([]);
  mocks.getChecklist.mockResolvedValue({ items: [], faltanObligatorios: 0, completo: true });
  mocks.getAttachments.mockResolvedValue([]);
  // Por defecto la tabla de "Trámites en curso" está vacía.
  mocks.listInstances.mockResolvedValue([]);
});

const INSTANCE_DRAFT: InstanceSummary = {
  id: 'inst-1',
  referenceNumber: 'TR-001',
  modalidad: 'traspaso',
  estado: 'draft',
  placa: 'ABC123',
  vin: 'VIN-XYZ-001',
  vehiculoMarca: 'Toyota',
  vehiculoLinea: 'Corolla',
  compradorNombre: 'Carlos Mendoza',
  compradorDocumento: '12345678',
  organismoTransito: 'Secretaría de Movilidad Bogotá',
  pasoActual: 2,
  totalPasos: 6,
  createdAt: '2026-06-18T00:00:00Z',
  draftFinalizedAt: null,
  identityValidationStatus: null,
  signaturePending: false,
  canSubmit: false,
  tenantId: '11111111-1111-1111-1111-111111111111',
  companiaNombre: null,
};

const INSTANCE_SUBMITTED: InstanceSummary = {
  id: 'inst-2',
  referenceNumber: 'MA-002',
  modalidad: 'matricula_inicial',
  estado: 'submitted',
  placa: null,
  vin: 'VIN-NEW-002',
  vehiculoMarca: 'Mazda',
  vehiculoLinea: 'CX-30',
  compradorNombre: 'María Restrepo',
  compradorDocumento: '87654321',
  organismoTransito: 'Cali — STTMP',
  pasoActual: 5,
  totalPasos: 5,
  createdAt: '2026-06-19T00:00:00Z',
  draftFinalizedAt: null,
  identityValidationStatus: null,
  signaturePending: false,
  canSubmit: false,
  tenantId: '11111111-1111-1111-1111-111111111111',
  companiaNombre: null,
};

describe('M6 — tabla de trámites en curso', () => {
  it('muestra el estado vacío cuando no hay instancias', async () => {
    render(<OperacionView onStartTramite={vi.fn()} />);
    expect(await screen.findByText('Aún no hay trámites')).toBeInTheDocument();
    expect(mocks.listInstances).toHaveBeenCalledTimes(1);
  });

  it('renderiza una fila por instancia con placa, comprador, VIN, paso y chip de estado', async () => {
    mocks.listInstances.mockResolvedValue([INSTANCE_DRAFT, INSTANCE_SUBMITTED]);
    render(<OperacionView onStartTramite={vi.fn()} />);

    const list = await screen.findByRole('list', { name: /Trámites en curso/ });
    const rows = within(list).getAllByRole('listitem');
    expect(rows).toHaveLength(2);

    // Fila draft: placa real, comprador, VIN, vehículo concatenado, paso, chip ámbar.
    expect(within(rows[0]).getByText('ABC123')).toBeInTheDocument();
    expect(within(rows[0]).getByText('TR-001')).toBeInTheDocument();
    expect(within(rows[0]).getByText('Carlos Mendoza')).toBeInTheDocument();
    expect(within(rows[0]).getByText('VIN-XYZ-001')).toBeInTheDocument();
    expect(within(rows[0]).getByText('Toyota Corolla')).toBeInTheDocument();
    expect(within(rows[0]).getByText('2/6')).toBeInTheDocument();
    expect(within(rows[0]).getByText('En preparación')).toBeInTheDocument();

    // Fila submitted: placa nula -> "—", chip azul "Enviado a tránsito".
    expect(within(rows[1]).getByText('—')).toBeInTheDocument();
    expect(within(rows[1]).getByText('Enviado a tránsito')).toBeInTheDocument();
    expect(within(rows[1]).getByText('5/5')).toBeInTheDocument();
  });

  it('al hacer clic en una fila navega al wizard de esa instancia', async () => {
    mocks.listInstances.mockResolvedValue([INSTANCE_DRAFT]);
    const user = userEvent.setup();
    render(<OperacionView onStartTramite={vi.fn()} />);

    const row = await screen.findByRole('button', { name: /Abrir trámite TR-001/ });
    await user.click(row);
    expect(routerPush).toHaveBeenCalledWith('/tramites/inst-1');
  });
});

describe('M0 — chooser por modalidad (Track B: delega en la ruta)', () => {
  it('muestra las dos modalidades y NO consulta tipos publicados', async () => {
    render(<OperacionView onStartTramite={vi.fn()} />);
    expect(await screen.findByRole('button', { name: /Iniciar Matrícula inicial/ })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /Iniciar Traspaso estándar/ })).toBeInTheDocument();
    // M0 desliga de Parametrización: ya no se listan tipos publicados.
    expect(mocks.listPublishedProcedureTypes).not.toHaveBeenCalled();
  });

  it('Matrícula inicial dispara onStartTramite y NO crea instancia inline', async () => {
    const onStart = vi.fn();
    const user = userEvent.setup();
    render(<OperacionView onStartTramite={onStart} />);
    await user.click(await screen.findByRole('button', { name: /Iniciar Matrícula inicial/ }));

    expect(onStart).toHaveBeenCalledWith('matricula_inicial');
    // Track B: el listado ya no crea la instancia; eso lo hace la ruta /nuevo.
    expect(mocks.createInstance).not.toHaveBeenCalled();
  });

  it('Traspaso estándar dispara onStartTramite con la modalidad traspaso', async () => {
    const onStart = vi.fn();
    const user = userEvent.setup();
    render(<OperacionView onStartTramite={onStart} />);
    await user.click(await screen.findByRole('button', { name: /Iniciar Traspaso estándar/ }));

    expect(onStart).toHaveBeenCalledWith('traspaso');
    expect(mocks.createInstance).not.toHaveBeenCalled();
  });
});

describe('Track A — toolbar de filtros y acciones del listado', () => {
  it('renderiza los chips de filtro de modalidad y estado', async () => {
    mocks.listInstances.mockResolvedValue([INSTANCE_DRAFT, INSTANCE_SUBMITTED]);
    render(<OperacionView onStartTramite={vi.fn()} />);

    // Espera a que cargue el listado (sale del estado "Cargando…").
    await screen.findByRole('list', { name: /Trámites en curso/ });

    expect(screen.getByRole('button', { name: 'Matrícula inicial' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Traspaso' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'En preparación' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Enviado a tránsito' })).toBeInTheDocument();
  });

  it('la búsqueda por placa reduce las filas visibles', async () => {
    mocks.listInstances.mockResolvedValue([INSTANCE_DRAFT, INSTANCE_SUBMITTED]);
    const user = userEvent.setup();
    render(<OperacionView onStartTramite={vi.fn()} />);

    const list = await screen.findByRole('list', { name: /Trámites en curso/ });
    expect(within(list).getAllByRole('listitem')).toHaveLength(2);

    await user.type(screen.getByRole('searchbox', { name: /Buscar trámites/ }), 'ABC123');

    const rows = within(
      screen.getByRole('list', { name: /Trámites en curso/ }),
    ).getAllByRole('listitem');
    expect(rows).toHaveLength(1);
    expect(within(rows[0]).getByText('ABC123')).toBeInTheDocument();
  });

  it('la búsqueda por VIN reduce las filas visibles', async () => {
    mocks.listInstances.mockResolvedValue([INSTANCE_DRAFT, INSTANCE_SUBMITTED]);
    const user = userEvent.setup();
    render(<OperacionView onStartTramite={vi.fn()} />);

    await screen.findByRole('list', { name: /Trámites en curso/ });
    await user.type(screen.getByRole('searchbox', { name: /Buscar trámites/ }), 'VIN-NEW-002');

    const rows = within(
      screen.getByRole('list', { name: /Trámites en curso/ }),
    ).getAllByRole('listitem');
    expect(rows).toHaveLength(1);
    expect(within(rows[0]).getByText('Enviado a tránsito')).toBeInTheDocument();
  });

  it('el botón Continuar de una fila draft navega al wizard de esa instancia', async () => {
    mocks.listInstances.mockResolvedValue([INSTANCE_DRAFT]);
    const user = userEvent.setup();
    render(<OperacionView onStartTramite={vi.fn()} />);

    const btn = await screen.findByRole('button', { name: /Continuar trámite TR-001/ });
    await user.click(btn);
    expect(routerPush).toHaveBeenCalledWith('/tramites/inst-1');
  });

  it('el botón Ver de una fila submitted navega al wizard de esa instancia', async () => {
    mocks.listInstances.mockResolvedValue([INSTANCE_SUBMITTED]);
    const user = userEvent.setup();
    render(<OperacionView onStartTramite={vi.fn()} />);

    const btn = await screen.findByRole('button', { name: /Ver trámite MA-002/ });
    await user.click(btn);
    expect(routerPush).toHaveBeenCalledWith('/tramites/inst-2');
  });

  it('el estado vacío con filtros activos muestra "Limpiar filtros" y al limpiar reaparecen las filas', async () => {
    mocks.listInstances.mockResolvedValue([INSTANCE_DRAFT, INSTANCE_SUBMITTED]);
    const user = userEvent.setup();
    render(<OperacionView onStartTramite={vi.fn()} />);

    await screen.findByRole('list', { name: /Trámites en curso/ });
    await user.type(screen.getByRole('searchbox', { name: /Buscar trámites/ }), 'ZZZ-SIN-MATCH');

    // Ya no hay lista de resultados; aparece el vacío "Sin resultados".
    expect(screen.queryByRole('list', { name: /Trámites en curso/ })).not.toBeInTheDocument();
    expect(screen.getAllByText('Sin resultados').length).toBeGreaterThan(0);

    const clearButtons = screen.getAllByRole('button', { name: 'Limpiar filtros' });
    expect(clearButtons.length).toBeGreaterThan(0);
    await user.click(clearButtons[0]);

    // Tras limpiar, vuelven las dos filas.
    const rows = within(
      await screen.findByRole('list', { name: /Trámites en curso/ }),
    ).getAllByRole('listitem');
    expect(rows).toHaveLength(2);
  });
});
