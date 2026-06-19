import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

import type {
  CreateInstanceRequest,
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
}));

vi.mock('@/lib/api/tramites-client', () => ({
  tramitesClient: mocks,
  DEV_TENANT_ID: 'tenant-dev',
  DEV_USER_ID: 'user-dev',
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
    { index: 1, key: 'validacion', label: 'Validación', status: 'incomplete', reasons: ['validacion_pendiente'] },
    { index: 2, key: 'vendedor', label: 'Vendedor', status: 'incomplete', reasons: ['vendedor_incompleto'] },
    { index: 3, key: 'comprador', label: 'Comprador', status: 'locked', reasons: [] },
    { index: 4, key: 'comercial', label: 'Comercial', status: 'locked', reasons: [] },
    { index: 5, key: 'fur', label: 'FUR', status: 'locked', reasons: [] },
  ],
};

const MATRICULA_WIZARD: WizardState = {
  modalidad: 'matricula_inicial',
  tipologiaCodigo: 'matricula_inicial',
  totalSteps: 5,
  canSubmit: false,
  blockers: ['documentos_incompletos'],
  steps: [
    { index: 0, key: 'consulta_vin', label: 'Consulta VIN', status: 'complete', reasons: [] },
    { index: 1, key: 'documentos', label: 'Documentos', status: 'incomplete', reasons: ['documentos_incompletos'] },
    { index: 2, key: 'comprador', label: 'Comprador', status: 'incomplete', reasons: ['runt_comprador'] },
    { index: 3, key: 'identidad', label: 'Identidad', status: 'locked', reasons: [] },
    { index: 4, key: 'fur', label: 'FUR', status: 'locked', reasons: [] },
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
});

describe('M0 — chooser por modalidad (sin tipos publicados)', () => {
  it('muestra las dos modalidades y NO consulta tipos publicados', async () => {
    render(<OperacionView />);
    expect(await screen.findByRole('button', { name: /Iniciar Matrícula inicial/ })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /Iniciar Traspaso estándar/ })).toBeInTheDocument();
    // M0 desliga de Parametrización: ya no se listan tipos publicados.
    expect(mocks.listPublishedProcedureTypes).not.toHaveBeenCalled();
  });

  it('Matrícula inicial crea la instancia con modalidad (sin procedureTypeId)', async () => {
    mocks.getWizardState.mockResolvedValue(MATRICULA_WIZARD);
    const user = userEvent.setup();
    render(<OperacionView />);
    await user.click(await screen.findByRole('button', { name: /Iniciar Matrícula inicial/ }));

    await waitFor(() => expect(mocks.createInstance).toHaveBeenCalledTimes(1));
    const body = mocks.createInstance.mock.calls[0][0] as CreateInstanceRequest;
    expect(body.modalidad).toBe('matricula_inicial');
    expect(body.procedureTypeId).toBeUndefined();
    // tenantId/createdByUserId provienen de dev-constants (UUIDs fijos del seed),
    // no del mock del cliente: useProcedureInstance los importa directamente.
    expect(body.tenantId).toBe('11111111-1111-1111-1111-111111111111');
    expect(body.createdByUserId).toBe('22222222-2222-2222-2222-222222222222');

    // Wizard server-driven: 5 pasos de matrícula.
    const stepButtons = await screen.findAllByRole('button', { name: /^Paso \d+:/ });
    expect(stepButtons).toHaveLength(5);
  });

  it('Traspaso estándar crea la instancia con modalidad traspaso', async () => {
    const user = userEvent.setup();
    render(<OperacionView />);
    await user.click(await screen.findByRole('button', { name: /Iniciar Traspaso estándar/ }));

    await waitFor(() => expect(mocks.createInstance).toHaveBeenCalledTimes(1));
    const body = mocks.createInstance.mock.calls[0][0] as CreateInstanceRequest;
    expect(body.modalidad).toBe('traspaso');
    expect(body.procedureTypeId).toBeUndefined();

    await waitFor(() =>
      expect(mocks.getWizardState).toHaveBeenCalledWith('inst-1', expect.anything()),
    );
    const stepButtons = await screen.findAllByRole('button', { name: /^Paso \d+:/ });
    expect(stepButtons).toHaveLength(6);
  });
});
