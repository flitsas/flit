import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, waitFor, within } from '@testing-library/react';

import type { ProcedureConfiguration, WizardState } from '@/lib/api/types/procedure-runtime';

/**
 * Franja azul del asistente: además de ID, canal, tipo y estado, muestra los datos del expediente:
 * preasignación (solo matrícula sin placa), placa, organismo de tránsito, VIN y fecha (la de
 * radicación o, sin radicar, la de hoy). Mismo harness que wizard-canal14322.test.tsx.
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


const fv = (fieldKey: string, valueText: string) => ({ fieldKey, valueText });

describe('Franja azul — datos del expediente', () => {
  it('matrícula sin placa: preasignación con el dígito, placa por asignar, organismo, VIN y fecha de hoy', async () => {
    detalle({
      submittedAt: null,
      fieldValues: [
        fv('vin', '9FKRG2222T2042405'),
        fv('transit_office_name', 'STRIA TTOyTTE MCPAL SABANETA'),
        fv('plate_preferred_last_digit', '1'),
      ],
    });
    const strip = await abrir();

    await waitFor(() =>
      expect(within(strip).getByTestId('tramite-preasignacion')).toHaveTextContent('Preasignación: Dígito 1'),
    );
    expect(within(strip).getByTestId('tramite-placa')).toHaveTextContent('Placa: Por asignar');
    expect(within(strip).getByTestId('tramite-organismo')).toHaveTextContent(
      'Organismo de tránsito: STRIA TTOyTTE MCPAL SABANETA',
    );
    expect(within(strip).getByTestId('tramite-vin')).toHaveTextContent('VIN: 9FKRG2222T2042405');
    // Sin radicar: la fecha de hoy en el formato de la plataforma (DD/MM/YYYY HH:mm).
    expect(within(strip).getByTestId('tramite-fecha').textContent).toMatch(
      /^Fecha: \d{2}\/\d{2}\/\d{4} \d{2}:\d{2}$/,
    );
  });

  it('con placa no hay preasignación y la fecha es la de radicación', async () => {
    detalle({
      submittedAt: '2026-11-05T15:30:00Z',
      fieldValues: [fv('plate', 'JNH38H'), fv('vin', 'VIN123'), fv('plate_preferred_last_digit', '1')],
    });
    const strip = await abrir();

    await waitFor(() => expect(within(strip).getByTestId('tramite-placa')).toHaveTextContent('Placa: JNH38H'));
    expect(within(strip).queryByTestId('tramite-preasignacion')).toBeNull();
    expect(within(strip).getByTestId('tramite-organismo')).toHaveTextContent(
      'Organismo de tránsito: Sin definir',
    );
    // 15:30 UTC = 10:30 en Colombia.
    expect(within(strip).getByTestId('tramite-fecha')).toHaveTextContent('Fecha: 05/11/2026 10:30');
  });
});
