import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

import type { InstanceSummary } from '@/lib/api/types/procedure-runtime';
import { TOKEN_STORAGE_KEY } from '@/lib/auth/jwt';

// Píldora «Quipux · …» en la columna Marcas: texto por estado; enlace al LOG QX solo con permiso.

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
const push = vi.hoisted(() => vi.fn());

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
  useRouter: () => ({ push, replace: vi.fn(), prefetch: vi.fn() }),
}));

import { TramitesTable } from '@/components/operacion/TramitesTable';
import { ToastProvider } from '@/components/admin/Toast';

const TENANT = '11111111-1111-1111-1111-111111111111';

function makeToken(payload: Record<string, unknown>): string {
  const header = Buffer.from(JSON.stringify({ alg: 'none', typ: 'JWT' })).toString('base64url');
  const body = Buffer.from(JSON.stringify(payload)).toString('base64url');
  return `${header}.${body}.`;
}

function fila(num: string, quipuxEstado: InstanceSummary['quipuxEstado']): InstanceSummary {
  return {
    id: `inst-${num}`,
    referenceNumber: `TR-${num}`,
    modalidad: 'TRASPASO',
    estado: 'preparado',
    origin: 'dashboard',
    isPaused: false,
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
    quipuxEstado,
  } satisfies InstanceSummary;
}

const token = (permissions: string[]) =>
  window.localStorage.setItem(
    TOKEN_STORAGE_KEY,
    makeToken({ sub: 'u1', role: 'Gestor', tenant_id: TENANT, permissions }),
  );

const montar = () =>
  render(
    <ToastProvider>
      <TramitesTable />
    </ToastProvider>,
  );

describe('TramitesTable — píldora Quipux en Marcas', { timeout: 30_000 }, () => {
  beforeEach(() => {
    vi.clearAllMocks();
    sessionStorage.clear();
    mocks.searchInstances.mockImplementation(async (params?: unknown) => {
      const todos: InstanceSummary[] = (await mocks.listInstances(params)) ?? [];
      return { items: todos, total: todos.length };
    });
    mocks.searchEstadoCounts.mockResolvedValue({});
    mocks.listFilterFields.mockResolvedValue([]);
    mocks.getConsultationConfig.mockResolvedValue({
      vehiclePlate: 'kyverum_runt',
      onlyOwnVehicles: false,
      blockProcedureFamily: { matriculas: false, traspaso: false, otros: false },
    });
    mocks.listInstances.mockResolvedValue([
      fila('0001', 'en_cola'),
      fila('0002', 'radicado'),
      { ...fila('0003', 'fallido'), quipuxEnvioId: 'env-3' },
      fila('0004', 'rechazado'),
      fila('0005', 'aprobado'),
      fila('0006', null),
    ]);
  });

  afterEach(() => {
    window.localStorage.removeItem(TOKEN_STORAGE_KEY);
  });

  it('pinta solo en cola, radicado y falló; sin permiso no es enlace', async () => {
    token(['tramites.read']);
    montar();
    await screen.findByText('P0001');
    for (const t of ['QX · En cola', 'QX · Radicado', 'QX · Falló']) {
      const pildora = screen.getByLabelText(t);
      expect(pildora).toHaveTextContent(t);
      expect(pildora.tagName).toBe('SPAN');
      expect(pildora).not.toHaveAttribute('href');
    }
    expect(screen.getAllByTestId('tramite-marca-quipux')).toHaveLength(3);
    expect(screen.queryByText(/QX · (Aprobado|Rechazado)/)).not.toBeInTheDocument();
  });

  it('con logqx.read: con envío va a la trazabilidad; sin envío, a la bandeja filtrada; el clic no abre la fila', async () => {
    token(['tramites.read', 'logqx.read']);
    montar();
    await screen.findByText('P0001');
    const conEnvio = screen.getByLabelText('QX · Falló: ver la trazabilidad en el Log QX');
    expect(conEnvio).toHaveAttribute('href', '/log-qx/env-3?instanceId=inst-0003');
    expect(conEnvio).toHaveAttribute('title', expect.stringContaining('ver la trazabilidad en el Log QX'));
    const sinEnvio = screen.getByLabelText('QX · En cola: ver el Log QX');
    expect(sinEnvio).toHaveAttribute('href', '/?m=log-qx&instanceId=inst-0001');
    // React delega en el contenedor raíz: si el manejador corta la propagación, el clic no llega
    // a `document`, que es por donde seguiría hacia la fila.
    const alDocumento = vi.fn();
    document.addEventListener('click', alDocumento);
    await userEvent.click(sinEnvio);
    document.removeEventListener('click', alDocumento);
    expect(alDocumento).not.toHaveBeenCalled();
    expect(push).not.toHaveBeenCalled();
  });
});
