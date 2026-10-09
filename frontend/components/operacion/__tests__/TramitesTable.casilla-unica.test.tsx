import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

import type { InstanceSummary } from '@/lib/api/types/procedure-runtime';
import { TOKEN_STORAGE_KEY } from '@/lib/auth/jwt';

// HU #13380 (épica #13216) — UNA casilla por fila y UNA barra. Antes un borrador ICT llevaba dos
// casillas idénticas (descarga masiva y pausa en lote) y el usuario confundía la de pausa con la
// marca «Pausado». Ahora la pausa en lote sale de la misma selección que la descarga.
//
// Uso de ejemplo:
//   <TramitesTable /> con el permiso `consolidado-masivo.download` → cada fila una casilla
//   «Seleccionar el trámite TR-0001»; la barra «Acciones sobre los trámites seleccionados» ofrece
//   «Descargar ZIP» y, si hay borradores ICT propios, «Pausar»/«Reanudar».

const mocks = vi.hoisted(() => ({
  listInstances: vi.fn(),
  searchInstances: vi.fn(),
  searchEstadoCounts: vi.fn(),
  listFilterFields: vi.fn(),
  listInstanceEstadoCounts: vi.fn().mockResolvedValue({}),
  getConsultationConfig: vi.fn(),
  getInstance: vi.fn(),
  getStatusHistory: vi.fn().mockResolvedValue({ items: [], total: 0, page: 1, pageSize: 50 }),
  pauseInstancesMassive: vi.fn(),
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
vi.mock('next/navigation', () => ({
  useRouter: () => ({ push: vi.fn(), replace: vi.fn(), prefetch: vi.fn() }),
}));

import { TramitesTable, etiquetaCasillaFila } from '@/components/operacion/TramitesTable';
import { ToastProvider } from '@/components/admin/Toast';

const TENANT = '11111111-1111-1111-1111-111111111111';
const REGION = 'Acciones sobre los trámites seleccionados';

function makeToken(payload: Record<string, unknown>): string {
  const header = Buffer.from(JSON.stringify({ alg: 'none', typ: 'JWT' })).toString('base64url');
  const body = Buffer.from(JSON.stringify(payload)).toString('base64url');
  return `${header}.${body}.`;
}

/** Datos sintéticos, sin PII. `ict` = índices (base 0) que son borradores ICT. */
function makeInstances(n: number, ict: number[] = [], pausados: number[] = []): InstanceSummary[] {
  return Array.from({ length: n }, (_, i) => {
    const num = String(i + 1).padStart(4, '0');
    const esIct = ict.includes(i);
    return {
      id: `inst-${num}`,
      referenceNumber: `TR-${num}`,
      modalidad: 'TRASPASO',
      estado: esIct ? 'borrador' : 'preparado',
      origin: esIct ? 'ict' : 'dashboard',
      isPaused: pausados.includes(i),
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
    } satisfies InstanceSummary;
  });
}

const fila = (placa: string) => screen.getByText(placa).closest('tr') as HTMLElement;
const barra = () => screen.getByRole('region', { name: REGION });

function conPermiso() {
  window.localStorage.setItem(
    TOKEN_STORAGE_KEY,
    makeToken({ sub: 'u1', role: 'Gestor', tenant_id: TENANT, permissions: ['consolidado-masivo.download'] }),
  );
}
function sinPermiso() {
  window.localStorage.setItem(
    TOKEN_STORAGE_KEY,
    makeToken({ sub: 'u1', role: 'Gestor', tenant_id: TENANT, permissions: ['tramites.read'] }),
  );
}

const montar = () =>
  render(
    <ToastProvider>
      <TramitesTable />
    </ToastProvider>,
  );

describe('etiquetaCasillaFila — HU #13380 (accesibilidad)', () => {
  it('con permiso es neutra; sin permiso habla de la pausa; en consulta dice que queda excluida', () => {
    expect(etiquetaCasillaFila('TR-1', { puedeLote: true, soloConsultaIct: false })).toBe(
      'Seleccionar el trámite TR-1',
    );
    expect(etiquetaCasillaFila('TR-1', { puedeLote: false, soloConsultaIct: false })).toBe(
      'Seleccionar el trámite TR-1 para pausar/reanudar en lote',
    );
    expect(etiquetaCasillaFila('TR-1', { puedeLote: true, soloConsultaIct: true })).toBe(
      'Seleccionar el trámite TR-1 (solo consulta: queda excluido de la pausa en lote)',
    );
    // Nunca menciona la descarga masiva.
    for (const puedeLote of [true, false]) {
      expect(etiquetaCasillaFila('TR-1', { puedeLote, soloConsultaIct: false })).not.toMatch(/descarga/);
    }
  });
});

describe('TramitesTable — una sola casilla por fila (HU #13380)', { timeout: 30_000 }, () => {
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
    mocks.pauseInstancesMassive.mockResolvedValue({ total: 1, processed: 1, detail: [] });
  });

  afterEach(() => {
    window.localStorage.removeItem(TOKEN_STORAGE_KEY);
  });

  it('con permiso, el borrador ICT tiene UNA sola casilla y no existe la barra de pausa separada', async () => {
    conPermiso();
    mocks.listInstances.mockResolvedValue(makeInstances(3, [0]));
    montar();
    await screen.findByText('P0001');
    expect(within(fila('P0001')).getAllByRole('checkbox')).toHaveLength(1);
    expect(within(fila('P0002')).getAllByRole('checkbox')).toHaveLength(1);
    expect(screen.queryByRole('region', { name: 'Acciones masivas de pausa' })).not.toBeInTheDocument();
    expect(screen.getAllByRole('region', { name: REGION })).toHaveLength(1);
  });

  it('con permiso y un borrador ICT marcado, la barra ofrece «Descargar ZIP» y «Pausar»; pausar limpia la selección', async () => {
    conPermiso();
    mocks.listInstances.mockResolvedValue(makeInstances(3, [0]));
    montar();
    await screen.findByText('P0001');
    await userEvent.click(screen.getByRole('checkbox', { name: 'Seleccionar el trámite TR-0001' }));
    await userEvent.click(screen.getByRole('checkbox', { name: 'Seleccionar el trámite TR-0002' }));

    expect(within(barra()).getByRole('button', { name: /Descargar ZIP/ })).toBeInTheDocument();
    expect(barra()).toHaveTextContent('1 excluido de la pausa: no son borradores ICT');
    await userEvent.click(within(barra()).getByRole('button', { name: 'Pausar' }));

    await waitFor(() => expect(mocks.pauseInstancesMassive).toHaveBeenCalledTimes(1));
    // Solo el borrador ICT viaja; el radicado normal no se pausa.
    expect(mocks.pauseInstancesMassive).toHaveBeenCalledWith(['inst-0001'], true, null, undefined);
    await waitFor(() =>
      expect(screen.getByTestId('contador-seleccion-lote')).toHaveTextContent('0 trámites seleccionados'),
    );
  });

  it('selección sin borradores ICT: solo la descarga, sin «Pausar» ni textos de pausa', async () => {
    conPermiso();
    mocks.listInstances.mockResolvedValue(makeInstances(3, [0]));
    montar();
    await screen.findByText('P0002');
    await userEvent.click(screen.getByRole('checkbox', { name: 'Seleccionar el trámite TR-0002' }));
    expect(within(barra()).getByRole('button', { name: /Descargar ZIP/ })).toBeInTheDocument();
    expect(within(barra()).queryByRole('button', { name: 'Pausar' })).not.toBeInTheDocument();
    expect(barra()).not.toHaveTextContent(/pausa/i);
  });

  it('todos los ICT marcados pausados → «Reanudar»', async () => {
    conPermiso();
    mocks.listInstances.mockResolvedValue(makeInstances(3, [0, 1], [0, 1]));
    montar();
    await screen.findByText('P0001');
    await userEvent.click(screen.getByRole('checkbox', { name: 'Seleccionar el trámite TR-0001' }));
    await userEvent.click(screen.getByRole('checkbox', { name: 'Seleccionar el trámite TR-0002' }));
    expect(within(barra()).queryByRole('button', { name: 'Pausar' })).not.toBeInTheDocument();
    await userEvent.click(within(barra()).getByRole('button', { name: 'Reanudar' }));
    await waitFor(() =>
      expect(mocks.pauseInstancesMassive).toHaveBeenCalledWith(['inst-0001', 'inst-0002'], false, null, undefined),
    );
  });

  it('«Seleccionar todos (del filtro)»: no hay pausa en lote y la barra lo explica', async () => {
    conPermiso();
    mocks.listInstances.mockResolvedValue(makeInstances(3, [0]));
    montar();
    await screen.findByText('P0001');
    await userEvent.click(screen.getByRole('checkbox', { name: /Seleccionar todos \(del filtro\)/ }));
    expect(barra()).toHaveTextContent('La pausa en lote aplica a trámites marcados uno a uno');
    expect(within(barra()).queryByRole('button', { name: 'Pausar' })).not.toBeInTheDocument();
    expect(within(barra()).getByRole('button', { name: /Descargar ZIP/ })).toBeInTheDocument();
  });

  it('el borrador ICT marcado en otra página sigue contando para la pausa', async () => {
    conPermiso();
    mocks.listInstances.mockResolvedValue(makeInstances(15, [0]));
    montar();
    await screen.findByText('P0001');
    await userEvent.click(screen.getByRole('checkbox', { name: 'Seleccionar el trámite TR-0001' }));
    await userEvent.click(screen.getByRole('button', { name: 'Página siguiente' }));
    await screen.findByText('P0011');
    await userEvent.click(within(barra()).getByRole('button', { name: 'Pausar' }));
    await waitFor(() =>
      expect(mocks.pauseInstancesMassive).toHaveBeenCalledWith(['inst-0001'], true, null, undefined),
    );
  });

  it('sin permiso: solo los borradores ICT llevan casilla y la barra no menciona la descarga', async () => {
    sinPermiso();
    mocks.listInstances.mockResolvedValue(makeInstances(3, [0]));
    montar();
    await screen.findByText('P0001');
    expect(within(fila('P0002')).queryByRole('checkbox')).not.toBeInTheDocument();
    // Sin selección no hay barra.
    expect(screen.queryByRole('region', { name: REGION })).not.toBeInTheDocument();

    const casilla = within(fila('P0001')).getByRole('checkbox', {
      name: 'Seleccionar el trámite TR-0001 para pausar/reanudar en lote',
    });
    await userEvent.click(casilla);
    expect(within(barra()).getByRole('button', { name: 'Pausar' })).toBeInTheDocument();
    expect(within(barra()).queryByRole('button', { name: /Descargar ZIP/ })).not.toBeInTheDocument();
    expect(within(barra()).queryByRole('checkbox', { name: /Seleccionar todos/ })).not.toBeInTheDocument();
    expect(barra()).not.toHaveTextContent(/descarga/i);
  });
});
