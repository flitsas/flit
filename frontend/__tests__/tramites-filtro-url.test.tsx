import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { render, waitFor } from '@testing-library/react';

// HU #14481 / #14480 (Epic #14471) — /tramites entra con filtro desde la URL: las cifras de Inicio y el buscador
// Ctrl K llegan con ?placa=, ?estado= o ?atajo=. El listado lo siembra UNA vez al montar; sin parámetros, arranca
// igual que siempre, y un valor desconocido se ignora.

const mocks = vi.hoisted(() => ({
  listInstances: vi.fn(),
  searchInstances: vi.fn(),
  searchEstadoCounts: vi.fn(),
  listFilterFields: vi.fn(),
  listInstanceEstadoCounts: vi.fn().mockResolvedValue({}),
  setPriority: vi.fn(),
  getAttachments: vi.fn(),
  fetchAttachmentPreviewUrl: vi.fn(),
  downloadAttachment: vi.fn(),
  entregarConsolidado: vi.fn(),
  getInstance: vi.fn(),
  getStatusHistory: vi.fn().mockResolvedValue({ items: [], total: 0, page: 1, pageSize: 50 }),
  listBiometricExpediente: vi.fn(),
  pauseInstance: vi.fn(),
  pauseInstancesMassive: vi.fn(),
  enviarAlOt: vi.fn(),
  analyzeDocument: vi.fn(),
  uploadAttachment: vi.fn(),
  persistOcrFields: vi.fn(),
  validateSoatViaRunt: vi.fn(),
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

type Consulta = { busqueda?: string; estado?: string; busquedaRapida?: string; condiciones?: unknown[] };

function irA(search: string) {
  window.history.replaceState({}, '', `/tramites${search}`);
}

/** La última consulta que el listado mandó al servidor. */
function ultimaConsulta(): Consulta {
  const calls = mocks.searchInstances.mock.calls;
  return (calls[calls.length - 1]?.[0] ?? {}) as Consulta;
}

beforeEach(() => {
  vi.clearAllMocks();
  sessionStorage.clear();
  mocks.searchInstances.mockResolvedValue({ items: [], total: 0 });
  mocks.searchEstadoCounts.mockResolvedValue({});
  mocks.listFilterFields.mockResolvedValue([]);
  mocks.getConsultationConfig.mockResolvedValue({
    vehiclePlate: 'kyverum_runt',
    onlyOwnVehicles: false,
    blockProcedureFamily: { matriculas: false, traspaso: false, otros: false },
  });
});

afterEach(() => irA(''));

function montar() {
  return render(
    <ToastProvider>
      <TramitesTable />
    </ToastProvider>,
  );
}

describe('TramitesTable — filtro sembrado desde la URL (HU #14481)', () => {
  it('sin parámetros arranca sin filtros, como siempre', async () => {
    irA('');
    montar();
    await waitFor(() => expect(mocks.searchInstances).toHaveBeenCalled());
    const q = ultimaConsulta();
    expect(q.busqueda).toBeUndefined();
    expect(q.estado).toBeUndefined();
  });

  it('?placa= siembra la búsqueda, normalizada en mayúsculas y sin espacios', async () => {
    irA('?placa=kyu%20631');
    const { container } = montar();
    await waitFor(() => expect(ultimaConsulta().busqueda).toBe('KYU631'));
    expect(container.querySelector('input[value="KYU631"]')).not.toBeNull();
  });

  it('?estado= con un estado real filtra por ese estado', async () => {
    irA('?estado=entregado');
    montar();
    await waitFor(() => expect(ultimaConsulta().estado).toBe('entregado'));
  });

  it('?estado= con un valor desconocido se ignora', async () => {
    irA('?estado=inventado');
    montar();
    await waitFor(() => expect(mocks.searchInstances).toHaveBeenCalled());
    expect(ultimaConsulta().estado).toBeUndefined();
  });

  it('?atajo= aplica el atajo con su estado, igual que elegirlo en la búsqueda rápida', async () => {
    irA('?atajo=sin_firmas');
    montar();
    await waitFor(() => expect(ultimaConsulta().estado).toBe('borrador'));
    expect(ultimaConsulta().busquedaRapida).toBe('sin_firmas');
  });

  it('?atajo= desconocido se ignora y no pisa un ?estado= válido', async () => {
    irA('?atajo=inventado&estado=aprobado');
    montar();
    await waitFor(() => expect(ultimaConsulta().estado).toBe('aprobado'));
    expect(ultimaConsulta().busquedaRapida).toBeUndefined();
  });
});
