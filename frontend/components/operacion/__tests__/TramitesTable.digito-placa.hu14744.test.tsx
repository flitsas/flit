import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

import type { InstanceSummary } from '@/lib/api/types/procedure-runtime';
import { TOKEN_STORAGE_KEY } from '@/lib/auth/jwt';
import { DEFAULT_TRAMITES_VISIBLE_COLUMNS, TRAMITES_COLUMN_KEYS } from '@/lib/tramites/tramites-table-columns';

// HU #14744 — columna «Dígito de placa» del listado del gestor: «Todos» y «Matrículas».

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
const prefs = vi.hoisted(() => ({ get: vi.fn(), put: vi.fn() }));

vi.mock('@/lib/api/tramites-client', () => ({
  tramitesClient: mocks,
  DEV_TENANT_ID: 'tenant-dev',
  DEV_USER_ID: 'user-dev',
}));
vi.mock('@/lib/api/ui-preferences', () => ({
  uiPreferencesClient: { get: prefs.get, put: prefs.put },
}));
vi.mock('next/navigation', () => ({
  useRouter: () => ({ push: vi.fn(), replace: vi.fn(), prefetch: vi.fn() }),
}));

import { TramitesTable } from '@/components/operacion/TramitesTable';
import { ToastProvider } from '@/components/admin/Toast';

const TENANT = '11111111-1111-1111-1111-111111111111';

function makeToken(payload: Record<string, unknown>): string {
  const header = Buffer.from(JSON.stringify({ alg: 'none', typ: 'JWT' })).toString('base64url');
  const body = Buffer.from(JSON.stringify(payload)).toString('base64url');
  return `${header}.${body}.`;
}

function fila(num: string, extra: Partial<InstanceSummary>): InstanceSummary {
  return {
    id: `inst-${num}`,
    referenceNumber: `TR-${num}`,
    modalidad: 'MATRICULAS',
    estado: 'preparado',
    origin: 'dashboard',
    isPaused: false,
    placa: `P${num}`,
    vin: `VIN-${num}`,
    vehiculoMarca: 'Marca',
    vehiculoLinea: 'Linea',
    compradorNombre: `Comprador ${num}`,
    compradorDocumento: `100${num}`,
    vendedorNombre: null,
    vendedorDocumento: null,
    organismoTransito: null,
    pasoActual: 6,
    totalPasos: 6,
    createdAt: '2026-06-18T00:00:00Z',
    draftFinalizedAt: null,
    identityValidationStatus: null,
    signaturePending: false,
    canSubmit: false,
    prioritario: false,
    tenantId: TENANT,
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
    ...extra,
  } satisfies InstanceSummary;
}

const montar = () =>
  render(
    <ToastProvider>
      <TramitesTable />
    </ToastProvider>,
  );

const cabecera = () => screen.queryByRole('columnheader', { name: /Dígito de placa/ });

const conColumna = () =>
  prefs.get.mockResolvedValue({
    scope: 'tramites.columns',
    value: { visible: [...DEFAULT_TRAMITES_VISIBLE_COLUMNS, 'digitoPlaca'], known: TRAMITES_COLUMN_KEYS },
  });

describe('TramitesTable — columna «Dígito de placa» (HU #14744)', { timeout: 30_000 }, () => {
  beforeEach(() => {
    vi.clearAllMocks();
    sessionStorage.clear();
    window.localStorage.setItem(
      TOKEN_STORAGE_KEY,
      makeToken({
        sub: 'u1',
        role: 'Gestor',
        tenant_id: TENANT,
        permissions: ['tramites.read', 'matriculas.read', 'traspasos.read', 'otros-tramites.read'],
      }),
    );
    mocks.searchInstances.mockImplementation(async (params?: unknown) => {
      const q = params as { modalidad?: string } | undefined;
      const todos: InstanceSummary[] = ((await mocks.listInstances(params)) ?? []).filter(
        (f: InstanceSummary) => !q?.modalidad || f.modalidad === q.modalidad,
      );
      return { items: todos, total: todos.length };
    });
    mocks.searchEstadoCounts.mockResolvedValue({});
    mocks.listFilterFields.mockResolvedValue([]);
    mocks.getConsultationConfig.mockResolvedValue({
      vehiclePlate: 'kyverum_runt',
      onlyOwnVehicles: false,
      blockProcedureFamily: { matriculas: false, traspaso: false, otros: false },
    });
    prefs.put.mockResolvedValue({ scope: 'tramites.columns', value: {} });
    mocks.listInstances.mockResolvedValue([
      fila('0001', { plateDigitoPreferido: '7', plateDigitoDeclarado: true, clase: 'MOTOCICLETA', placaRutaCorta: false }),
      fila('0002', { plateDigitoPreferido: '3', plateDigitoDeclarado: true, clase: null, placaRutaCorta: false }),
      fila('0003', { plateDigitoPreferido: null, plateDigitoDeclarado: true, clase: 'AUTOMOVIL', placaRutaCorta: false }),
      fila('0004', { plateDigitoPreferido: null, plateDigitoDeclarado: null, clase: 'AUTOMOVIL', placaRutaCorta: true }),
      fila('0005', { modalidad: 'TRASPASO', plateDigitoPreferido: '9', plateDigitoDeclarado: true, clase: 'AUTOMOVIL' }),
    ]);
  });

  afterEach(() => {
    window.localStorage.removeItem(TOKEN_STORAGE_KEY);
  });

  it('AC6: con la preferencia por defecto la columna no se ve, ni en Todos ni en Matrículas', async () => {
    prefs.get.mockResolvedValue({ scope: 'tramites.columns', value: {} });
    montar();
    await screen.findByText('P0001');
    expect(cabecera()).not.toBeInTheDocument();
    await userEvent.click(screen.getByRole('tab', { name: 'Matrículas' }));
    await screen.findByText('P0001');
    expect(cabecera()).not.toBeInTheDocument();
  });

  it('AC1-AC4: en Matrículas, con la columna marcada, cada fila dice lo que corresponde', async () => {
    conColumna();
    montar();
    await screen.findByText('P0001');
    await userEvent.click(screen.getByRole('tab', { name: 'Matrículas' }));
    expect(await screen.findByRole('columnheader', { name: /Dígito de placa/ })).toBeInTheDocument();
    const celdas = screen.getAllByTestId('tramite-digito-placa').map((c) => c.textContent);
    expect(celdas).toEqual(['Inicia en 7', 'Termina en 3', 'Sin preferencia', '—']);
  });

  it('Todos: la columna se ofrece y, marcada, muestra el valor en matrículas y vacío en un traspaso', async () => {
    conColumna();
    montar();
    await screen.findByText('P0001');
    expect(await screen.findByRole('columnheader', { name: /Dígito de placa/ })).toBeInTheDocument();
    const celdas = screen.getAllByTestId('tramite-digito-placa').map((c) => c.textContent);
    expect(celdas).toEqual(['Inicia en 7', 'Termina en 3', 'Sin preferencia', '—', '']);
    await userEvent.click(screen.getByRole('button', { name: /Columnas/ }));
    expect(await screen.findByRole('checkbox', { name: /Dígito de placa/ })).toBeChecked();
  });

  it('AC5: desaparece en Traspaso (tabla y panel Columnas) y reaparece al volver a Matrículas', async () => {
    conColumna();
    montar();
    await screen.findByText('P0001');

    await userEvent.click(screen.getByRole('tab', { name: 'Matrículas' }));
    expect(await screen.findByRole('columnheader', { name: /Dígito de placa/ })).toBeInTheDocument();

    await userEvent.click(screen.getByRole('tab', { name: 'Traspaso' }));
    await screen.findByText('P0005');
    expect(cabecera()).not.toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: /Columnas/ }));
    expect(await screen.findAllByRole('checkbox')).not.toHaveLength(0);
    expect(screen.queryByRole('checkbox', { name: /Dígito de placa/ })).not.toBeInTheDocument();
    await userEvent.keyboard('{Escape}');

    await userEvent.click(screen.getByRole('tab', { name: 'Matrículas' }));
    expect(await screen.findByRole('columnheader', { name: /Dígito de placa/ })).toBeInTheDocument();
  });

  it('AC5/AC6: en Matrículas y en Todos el panel Columnas la ofrece sin marcar por defecto', async () => {
    prefs.get.mockResolvedValue({ scope: 'tramites.columns', value: {} });
    montar();
    await screen.findByText('P0001');
    await userEvent.click(screen.getByRole('button', { name: /Columnas/ }));
    expect(await screen.findByRole('checkbox', { name: /Dígito de placa/ })).not.toBeChecked();
    await userEvent.keyboard('{Escape}');
    await userEvent.click(screen.getByRole('tab', { name: 'Matrículas' }));
    await userEvent.click(screen.getByRole('button', { name: /Columnas/ }));
    expect(await screen.findByRole('checkbox', { name: /Dígito de placa/ })).not.toBeChecked();
  });
});
