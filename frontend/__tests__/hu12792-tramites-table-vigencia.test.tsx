/**
 * HU #12792 (Épica #12760) — indicador de vigencia del consolidado en la FILA del listado del
 * gestor (`TramitesTable`, variante compacta). Mismo arnés que la HU #12786.
 *
 * Uso de ejemplo:
 *   // InstanceSummary.consolidadoWizard = { estado: 'vigente', generadoEn: '…', … }
 *   <TramitesTable />  // → celda Estado: «Consolidado vigente» + 23/09/2026 10:05
 */
import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { render, screen, within } from '@testing-library/react';

import type { ConsolidadoVigencia, InstanceSummary } from '@/lib/api/types/procedure-runtime';

const mocks = vi.hoisted(() => ({
  listInstances: vi.fn(),
  searchInstances: vi.fn(),
  searchEstadoCounts: vi.fn(),
  listFilterFields: vi.fn(),
  listInstanceEstadoCounts: vi.fn(),
  setPriority: vi.fn(),
  getAttachments: vi.fn(),
  fetchAttachmentPreviewUrl: vi.fn(),
  downloadAttachment: vi.fn(),
  entregarConsolidado: vi.fn(),
  generarConsolidado: vi.fn(),
  getInstance: vi.fn(),
  getStatusHistory: vi.fn(),
  listBiometricExpediente: vi.fn(),
  pauseInstance: vi.fn(),
  pauseInstancesMassive: vi.fn(),
  enviarAlOt: vi.fn(),
  getConsultationConfig: vi.fn(),
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

import { TramitesTable } from '@/components/operacion/TramitesTable';
import { ToastProvider } from '@/components/admin/Toast';

// ── Fixtures (datos ficticios) ──────────────────────────────────────
const INSTANCE_ID = 'inst-0001';

function instancia(overrides: Partial<InstanceSummary> = {}): InstanceSummary {
  return {
    id: INSTANCE_ID,
    referenceNumber: 'TR-0001',
    modalidad: 'TRASPASO',
    estado: 'radicado',
    placa: 'ABC123',
    vin: 'VIN-0001',
    vehiculoMarca: 'Marca',
    vehiculoLinea: 'Linea',
    compradorNombre: 'Comprador',
    compradorDocumento: '1000',
    vendedorNombre: 'Vendedor',
    vendedorDocumento: '2000',
    organismoTransito: null,
    pasoActual: 6,
    totalPasos: 6,
    createdAt: '2026-09-01T00:00:00Z',
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
    consolidadoAttachmentId: 'att-viejo',
    ...overrides,
  } as InstanceSummary;
}

const createObjectURL = vi.fn(() => 'blob:consolidado');
const revokeObjectURL = vi.fn();

beforeEach(() => {
  vi.clearAllMocks();
  sessionStorage.clear();
  mocks.searchInstances.mockImplementation(async () => {
    const items: InstanceSummary[] = (await mocks.listInstances()) ?? [];
    return { items, total: items.length };
  });
  mocks.searchEstadoCounts.mockResolvedValue({});
  mocks.listInstanceEstadoCounts.mockResolvedValue({});
  mocks.listFilterFields.mockResolvedValue([]);
  mocks.getStatusHistory.mockResolvedValue({ items: [], total: 0, page: 1, pageSize: 50 });
  mocks.getConsultationConfig.mockResolvedValue({
    vehiclePlate: 'kyverum_runt',
    onlyOwnVehicles: false,
    blockProcedureFamily: { matriculas: false, traspaso: false, otros: false },
  });
  mocks.fetchAttachmentPreviewUrl.mockResolvedValue({
    url: 'https://s3.local/consolidado.pdf',
    expiresAt: '2026-09-23T00:10:00Z',
  });
  mocks.downloadAttachment.mockResolvedValue({
    blob: new Blob(['%PDF'], { type: 'application/pdf' }),
    filename: 'expediente-consolidado-TR-0001.pdf',
  });
  // Solo la URL prefirmada del PDF responde; el resto (p. ej. /security/modules) falla como sin red.
  vi.stubGlobal(
    'fetch',
    vi.fn(async (input: RequestInfo | URL) =>
      String(input).startsWith('https://s3.local/')
        ? new Response(new Blob(['%PDF'], { type: 'application/pdf' }), { status: 200 })
        : Promise.reject(new TypeError('network')),
    ),
  );
  Object.defineProperty(URL, 'createObjectURL', { value: createObjectURL, configurable: true });
  Object.defineProperty(URL, 'revokeObjectURL', { value: revokeObjectURL, configurable: true });
});

afterEach(() => {
  vi.unstubAllGlobals();
});

function vigencia(overrides: Partial<ConsolidadoVigencia> = {}): ConsolidadoVigencia {
  return {
    estado: 'vigente',
    generadoEn: '2026-09-23T15:05:00Z',
    origen: 'system',
    definitivo: false,
    modo: null,
    ...overrides,
  };
}

async function renderFila(item: InstanceSummary) {
  mocks.listInstances.mockResolvedValue([item]);
  render(
    <ToastProvider>
      <TramitesTable />
    </ToastProvider>,
  );
  const placa = await screen.findByText('ABC123');
  return placa.closest('tr') as HTMLElement;
}

describe('HU #12792 — indicador en la fila del listado del gestor (compacta)', () => {
  it('AC1 — vigente: verde #70CF3A con fecha y hora', async () => {
    const fila = await renderFila(instancia({ consolidadoWizard: vigencia() }));
    const ind = within(fila).getByTestId('vigencia-consolidado');
    expect(ind).toHaveAttribute('data-variante', 'compacta');
    expect(ind).toHaveAttribute('data-estado', 'vigente');
    expect(ind).toHaveTextContent('Consolidado vigente');
    expect(ind).toHaveTextContent('23/09/2026 10:05');
    expect(within(ind).getByTestId('vigencia-consolidado-punto')).toHaveStyle({
      background: '#70CF3A',
    });
  });

  it('AC1 — estado final: vigente + «Definitivo»', async () => {
    const fila = await renderFila(
      instancia({
        estado: 'aprobado',
        consolidadoWizard: vigencia({ definitivo: true, modo: 'definitivo_estado_final' }),
      }),
    );
    expect(within(fila).getByTestId('vigencia-consolidado-definitivo')).toHaveTextContent(
      'Definitivo',
    );
  });

  it('AC2 — desactualizado: gris #59677D con leyenda de pendiente de regenerar', async () => {
    const fila = await renderFila(
      instancia({ consolidadoWizard: vigencia({ estado: 'desactualizado' }) }),
    );
    const ind = within(fila).getByTestId('vigencia-consolidado');
    expect(ind).toHaveTextContent('Pendiente de regenerar');
    expect(within(ind).getByTestId('vigencia-consolidado-punto')).toHaveStyle({
      background: '#59677D',
    });
  });

  it('AC3 — inexistente: aún no se ha generado, sin fecha', async () => {
    const fila = await renderFila(
      instancia({
        consolidadoAttachmentId: null,
        consolidadoWizard: vigencia({ estado: 'inexistente', generadoEn: null, origen: null }),
      }),
    );
    const ind = within(fila).getByTestId('vigencia-consolidado');
    expect(ind).toHaveTextContent('Aún no se ha generado');
    expect(ind.textContent).not.toMatch(/\d{2}\/\d{2}\/\d{4}/);
  });

  it('AC5 — el estado tiene nombre accesible textual en la fila', async () => {
    const fila = await renderFila(instancia({ consolidadoWizard: vigencia() }));
    expect(
      within(fila).getByRole('group', { name: /consolidado: vigente, generado el 23\/09\/2026 10:05/i }),
    ).toBeInTheDocument();
  });

  it.each([undefined, null])('backend sin el campo (%s): la fila no muestra indicador', async (valor) => {
    const fila = await renderFila(instancia({ consolidadoWizard: valor }));
    expect(within(fila).queryByTestId('vigencia-consolidado')).toBeNull();
  });
});
