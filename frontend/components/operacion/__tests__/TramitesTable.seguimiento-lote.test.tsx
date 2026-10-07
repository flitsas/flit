import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

import type { InstanceSummary } from '@/lib/api/types/procedure-runtime';
import type { LoteConsolidados } from '@/lib/api/types-consolidado-lotes';
import { TOKEN_STORAGE_KEY } from '@/lib/auth/jwt';
import { LoteDescargaTracker, LoteDescargaTrackerProvider } from '@/components/shared/LoteDescargaTracker';

// Uso de ejemplo (lo que hace el Shell alrededor de cualquier página):
//   <LoteDescargaTrackerProvider habilitado={puedeDescargaMasiva}>
//     <TramitesTable />          {/* «Descargar ZIP» → onCreado / onLoteActivo → mostrarLote */}
//     <LoteDescargaTracker />
//   </LoteDescargaTrackerProvider>

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
vi.mock('next/navigation', () => ({
  useRouter: () => ({ push: vi.fn(), replace: vi.fn(), prefetch: vi.fn() }),
}));

import { TramitesTable } from '@/components/operacion/TramitesTable';
import { ToastProvider } from '@/components/admin/Toast';

/** Datos sintéticos según `LoteConsolidados` del contrato §5. */
const CREADO: LoteConsolidados = {
  id: 'lote-creado',
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
};
const ACTIVO: LoteConsolidados = { ...CREADO, id: 'lote-activo', estado: 'en_proceso', total: 120, procesados: 45, incluidos: 45 };

function makeToken(payload: Record<string, unknown>): string {
  const header = Buffer.from(JSON.stringify({ alg: 'none', typ: 'JWT' })).toString('base64url');
  const body = Buffer.from(JSON.stringify(payload)).toString('base64url');
  return `${header}.${body}.`;
}
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

const json = (status: number, body: unknown) =>
  new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } });
const fetchMock = vi.fn();
function servidor(crear: () => Response) {
  fetchMock.mockImplementation(async (url: string, init?: RequestInit) => {
    const path = new URL(url).pathname;
    if (path === '/api/v1/consolidados/lotes/actual') return new Response(null, { status: 204 });
    if (path === '/api/v1/tramites/consolidados/lotes' && init?.method === 'POST') return crear();
    if (path === `/api/v1/consolidados/lotes/${CREADO.id}`) return json(200, CREADO);
    if (path === `/api/v1/consolidados/lotes/${ACTIVO.id}`) return json(200, ACTIVO);
    throw new TypeError('Failed to fetch');
  });
}

describe('TramitesTable → seguimiento global del lote — HU #13382', { timeout: 30_000 }, () => {
  beforeEach(() => {
    vi.clearAllMocks();
    fetchMock.mockReset();
    vi.stubGlobal('fetch', fetchMock);
    sessionStorage.clear();
    mocks.searchInstances.mockImplementation(async () => {
      const todos = makeInstances(3);
      return { items: todos, total: todos.length };
    });
    mocks.listInstances.mockResolvedValue(makeInstances(3));
    mocks.searchEstadoCounts.mockResolvedValue({});
    mocks.listFilterFields.mockResolvedValue([]);
    mocks.getConsultationConfig.mockResolvedValue({
      vehiclePlate: 'kyverum_runt',
      onlyOwnVehicles: false,
      blockProcedureFamily: { matriculas: false, traspaso: false, otros: false },
    });
    window.localStorage.setItem(
      TOKEN_STORAGE_KEY,
      makeToken({ sub: 'u1', role: 'Gestor', permissions: ['consolidado-masivo.download'] }),
    );
  });
  afterEach(() => {
    vi.unstubAllGlobals();
    window.localStorage.clear();
  });

  const montar = () =>
    render(
      <ToastProvider>
        <LoteDescargaTrackerProvider habilitado>
          <TramitesTable />
          <LoteDescargaTracker />
        </LoteDescargaTrackerProvider>
      </ToastProvider>,
    );

  async function crearDesdeLaBarra() {
    const user = userEvent.setup();
    montar();
    await screen.findByText('P0001');
    await user.click(await screen.findByRole('checkbox', { name: /TR-0001/ }));
    await user.click(screen.getByRole('checkbox', { name: /TR-0002/ }));
    await user.click(screen.getByRole('button', { name: /Descargar ZIP/ }));
    const dialogo = screen.getByRole('dialog', { name: /Descargar consolidados en ZIP/ });
    await user.click(within(dialogo).getByRole('button', { name: /Confirmar descarga/ }));
  }

  it('AC1 — al crear el lote (onCreado) el aviso global lo muestra «En cola» con 0 / total', async () => {
    servidor(() => json(202, CREADO));
    await crearDesdeLaBarra();
    const card = await screen.findByTestId('lote-descarga-card');
    expect(card).toHaveTextContent('0 / 2');
    expect(within(card).getByRole('status')).toHaveTextContent('En cola');
  });

  it('409 lote_activo (onLoteActivo) — el aviso global sigue el lote que ya corre', async () => {
    servidor(() => json(409, { error: 'lote_activo', loteActivoId: ACTIVO.id }));
    await crearDesdeLaBarra();
    const card = await screen.findByTestId('lote-descarga-card');
    expect(card).toHaveTextContent('45 / 120');
  });
});
