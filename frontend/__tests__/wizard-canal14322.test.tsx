import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, waitFor, within } from '@testing-library/react';

import type { ProcedureConfiguration, WizardState } from '@/lib/api/types/procedure-runtime';

/**
 * HU #14322 (Épica #14296) mostraba el canal en la barra de identidad del asistente. Decisión del
 * usuario (2026-11-11): el canal NO va en la franja; sigue en su columna de la tabla de trámites.
 * Estas pruebas cuidan que no vuelva a aparecer. Mismo harness que hu10874-subsanacion-wizard.test.tsx.
 */
const mocks = vi.hoisted(() => ({
  getCamaraComercioRequirements: vi.fn(() => Promise.resolve([])),
  createInstance: vi.fn(),
  getInstance: vi.fn(),
  getWizardState: vi.fn(),
  startSubsanacion: vi.fn(),
  cancelSubsanacion: vi.fn(),
  patchFieldValues: vi.fn(),
  setCurrentStep: vi.fn(),
  runPreflight: vi.fn(),
  getPreflight: vi.fn(),
  getConsultationConfig: vi.fn(),
  getCommercial: vi.fn(),
  putCommercial: vi.fn(),
  submitInstance: vi.fn(),
  transitionInstance: vi.fn(),
  finalizeDraft: vi.fn(),
  getActors: vi.fn(),
  saveActors: vi.fn(),
  getChecklist: vi.fn(),
  getAttachments: vi.fn(),
  uploadAttachment: vi.fn(),
  deleteAttachment: vi.fn(),
  listTransitOffices: vi.fn(),
  getBiometricState: vi.fn(),
  iniciarBiometric: vi.fn(),
  simulateBiometric: vi.fn(),
  ensureIdentity: vi.fn(),
  getInstanceIdentityValidationAlerts: vi.fn(),
  listBiometric: vi.fn(),
  listFirmas: vi.fn(),
  listParticipantes: vi.fn(),
}));

vi.mock('@/lib/api/tramites-client', () => ({
  tramitesClient: mocks,
  DEV_TENANT_ID: 'tenant-dev',
  DEV_USER_ID: 'user-dev',
  getDuplicateActiveProcedureId: () => null,
  getVehicleStateBlock: () => null,
}));

vi.mock('@/components/admin/Toast', () => ({
  useToast: () => ({ show: vi.fn() }),
}));

vi.mock('next/navigation', () => ({
  useRouter: () => ({ push: vi.fn(), replace: vi.fn(), prefetch: vi.fn() }),
}));

import { TramiteWizard } from '@/components/operacion/TramiteWizard';
import { CANAL_LABEL } from '@/lib/tramites/tramites-row-labels';

const CONFIG: ProcedureConfiguration = {
  id: 'type-1',
  code: 'TRASPASO_STD',
  name: 'Traspaso estándar',
  family: 'TRASPASO',
  publishedAt: '2026-06-18T00:00:00Z',
  conformationRules: [],
  steps: [],
};

const WIZARD: WizardState = {
  modalidad: 'matricula_inicial',
  tipologiaCodigo: 'matricula_inicial',
  totalSteps: 5,
  canSubmit: false,
  blockers: [],
  status: 'borrador',
  allowedTransitions: [],
  steps: [
    { index: 0, key: 'consulta_vin', label: 'Consulta VIN', status: 'incomplete', reasons: [] },
    { index: 1, key: 'documentos', label: 'Documentos', status: 'incomplete', reasons: [] },
    { index: 2, key: 'comprador', label: 'Comprador', status: 'incomplete', reasons: [] },
    { index: 3, key: 'identidad', label: 'Identidad', status: 'incomplete', reasons: [] },
    { index: 4, key: 'fur', label: 'FUR', status: 'incomplete', reasons: [] },
  ],
};

function detalle(extra: Record<string, unknown>) {
  mocks.getInstance.mockResolvedValue({
    id: 'inst-1',
    referenceNumber: 'RAD-77',
    status: 'borrador',
    fieldValues: [],
    statusHistory: [],
    ...extra,
  });
}

async function abrir() {
  render(<TramiteWizard existingInstanceId="inst-1" onExit={() => {}} />);
  return screen.findByTestId('tramite-identity-strip');
}

beforeEach(() => {
  vi.clearAllMocks();
  mocks.createInstance.mockResolvedValue({ id: 'inst-1' });
  mocks.getWizardState.mockResolvedValue(WIZARD);
  mocks.patchFieldValues.mockResolvedValue({ id: 'inst-1', fieldValues: [] });
  mocks.setCurrentStep.mockResolvedValue({ id: 'inst-1', currentStep: null });
  mocks.runPreflight.mockResolvedValue({ overall: 'green', checks: [], createdAt: '2026-06-18T00:00:00Z' });
  mocks.getPreflight.mockResolvedValue(null);
  mocks.getConsultationConfig.mockResolvedValue({
    vehicleVin: 'kyverum_runt',
    vehiclePlate: 'kyverum_runt',
    conductor: 'kyverum_runt_conductor',
  });
  mocks.getCommercial.mockResolvedValue({ valorVenta: null, causal: null, tasaImpuesto: null, derechos: null, metodoPago: null });
  mocks.getActors.mockResolvedValue([]);
  mocks.getChecklist.mockResolvedValue({ items: [], faltanObligatorios: 0, completo: true });
  mocks.getAttachments.mockResolvedValue([]);
  mocks.listTransitOffices.mockResolvedValue([]);
  mocks.getBiometricState.mockResolvedValue({ validations: [], provider: 'mock' });
  mocks.getInstanceIdentityValidationAlerts.mockResolvedValue({ alerts: [], total: 0 });
  mocks.listBiometric.mockResolvedValue([]);
  mocks.listFirmas.mockResolvedValue([]);
  mocks.listParticipantes.mockResolvedValue([]);
  mocks.ensureIdentity.mockResolvedValue({ outcome: 'ya_vigente' });
});

describe('Franja del asistente sin canal (decisión 2026-11-11, revierte la barra de HU #14322)', () => {
  it.each(['tramites', 'pn_web', 'integracion', 'aplicacion', 'pn_punto'] as const)(
    'canal «%s»: la franja no lo muestra (ni el OT del punto) y conserva ID y estado',
    async (codigo) => {
      detalle({ channel: codigo, channelTransitOfficeName: 'Tránsito de Envigado' });
      const strip = await abrir();
      await waitFor(() => expect(strip).toHaveTextContent('RAD-77'));
      expect(strip).toHaveTextContent('En borrador');
      expect(strip).not.toHaveTextContent('Canal:');
      expect(strip).not.toHaveTextContent(CANAL_LABEL[codigo]);
      expect(strip).not.toHaveTextContent('Tránsito de Envigado');
      expect(within(strip).queryByTestId('tramite-canal')).not.toBeInTheDocument();
    },
  );

  it('sin trámite creado (nuevo) no consulta el detalle', async () => {
    mocks.getInstance.mockResolvedValue({});
    render(<TramiteWizard configuration={CONFIG} procedureTypeId="type-1" onExit={() => {}} />);
    await screen.findByRole('button', { name: /Cancelar/ });
    expect(screen.queryByTestId('tramite-canal')).not.toBeInTheDocument();
    expect(mocks.getInstance).not.toHaveBeenCalled();
  });
});
