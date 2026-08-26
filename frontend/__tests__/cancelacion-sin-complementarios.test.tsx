import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen } from '@testing-library/react';

import type { WizardCapabilities, WizardState } from '@/lib/api/types/procedure-runtime';

/**
 * Cancelación de matrícula: el paso de Requisitos NO ofrece prenda ni trámites simultáneos.
 *
 * <p>El tipo es de la familia MATRICULAS, y esa familia acumula trámites complementarios
 * (art. 5.1.8): por eso el asistente le pintaba «Asignación de Prenda / Limitación a la Propiedad» y
 * «Trámites Simultáneos — Transformaciones del Vehículo». Pero acumular presupone un vehículo que
 * sigue inscrito, y la cancelación lo saca del registro: inscribirle una limitación a la propiedad o
 * declararle un cambio de color son contradicciones que el organismo devuelve.</p>
 *
 * <p>La regla la declara el tipo en su <c>gate_profile</c> (DDL 93) y de ahí la leen el servidor y
 * las capacidades. Estas pruebas montan a propósito el caso PEOR —un expediente cuyo snapshot es
 * ANTERIOR a esa declaración, es decir capacidades sin las llaves— para fijar que las secciones
 * tampoco aparecen ahí: un borrador abierto antes del cambio conserva el perfil congelado y de otro
 * modo las seguiría viendo hasta cerrarse.</p>
 */
const mocks = vi.hoisted(() => ({
  createInstance: vi.fn(),
  getInstance: vi.fn(),
  getWizardState: vi.fn(),
  patchFieldValues: vi.fn(),
  setCurrentStep: vi.fn(),
  runPreflight: vi.fn(),
  getPreflight: vi.fn(),
  getConsultationConfig: vi.fn(),
  getCommercial: vi.fn(),
  putCommercial: vi.fn(),
  getPrenda: vi.fn(),
  putPrenda: vi.fn(),
  submitInstance: vi.fn(),
  transitionInstance: vi.fn(),
  finalizeDraft: vi.fn(),
  getActors: vi.fn(),
  saveActors: vi.fn(),
  runtPersonLookup: vi.fn(),
  ruesPersonLookup: vi.fn(),
  actorContactLookup: vi.fn(),
  lookupLegalRepresentativeByNit: vi.fn(),
  listVehicleServiceTypes: vi.fn(),
  getChecklist: vi.fn(),
  getAttachments: vi.fn(),
  uploadAttachment: vi.fn(),
  deleteAttachment: vi.fn(),
  fetchAttachmentPreviewUrl: vi.fn(),
  downloadAttachment: vi.fn(),
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
  isTransitOfficeUnavailable: () => false,
  isVehicleBodyTypeMissing: () => false,
  isVehiclePrendaMissing: () => false,
}));

vi.mock('@/components/admin/Toast', () => ({ useToast: () => ({ show: vi.fn() }) }));
vi.mock('next/navigation', () => ({
  useRouter: () => ({ push: vi.fn(), replace: vi.fn(), prefetch: vi.fn() }),
}));

import { TramiteWizard } from '@/components/operacion/TramiteWizard';

/**
 * Capacidades tal como las trae un snapshot ANTERIOR al DDL 93: sin las llaves de complementarios.
 * Al faltar, se resuelven por familia — y MATRICULAS acumula. Es el caso que hacía aparecer las dos
 * secciones.
 */
const CAPS_SNAPSHOT_VIEJO: WizardCapabilities = {
  entryMode: 'PLATE',
  requiresSeller: false,
  requiresBuyer: true,
  allowsMultipleBuyer: false,
  requiresCommercialValue: false,
  requiresBiometrics: true,
  biometricActors: ['BUYER'],
  hasPrendaGate: false,
};

const CANCELACION_WIZARD: WizardState = {
  modalidad: 'MATRICULAS',
  tipologiaCodigo: 'CANCELACION_MATRICULA',
  typeName: 'Cancelación de matrícula',
  capabilities: CAPS_SNAPSHOT_VIEJO,
  totalSteps: 4,
  canSubmit: false,
  blockers: [],
  status: 'borrador',
  allowedTransitions: [],
  persistedCurrentStep: 'documentos',
  steps: [
    { index: 0, key: 'consulta', label: 'Consulta placa', status: 'complete', reasons: [] },
    { index: 1, key: 'documentos', label: 'Requisitos', status: 'incomplete', reasons: [] },
    { index: 2, key: 'identidad', label: 'Identidad', status: 'locked', reasons: [] },
    { index: 3, key: 'fur', label: 'FUR', status: 'locked', reasons: [] },
  ],
};

beforeEach(() => {
  vi.clearAllMocks();
  mocks.getInstance.mockResolvedValue({
    id: 'inst-1',
    status: 'borrador',
    draftFinalizedAt: null,
    fieldValues: [],
    actors: [],
    currentStep: 'documentos',
  });
  mocks.getWizardState.mockResolvedValue(CANCELACION_WIZARD);
  mocks.patchFieldValues.mockResolvedValue({ id: 'inst-1', fieldValues: [] });
  mocks.setCurrentStep.mockResolvedValue({ id: 'inst-1', currentStep: 'documentos' });
  mocks.runPreflight.mockResolvedValue({
    overall: 'green',
    checks: [],
    createdAt: '2026-08-26T00:00:00Z',
  });
  mocks.getPreflight.mockResolvedValue(null);
  mocks.getConsultationConfig.mockResolvedValue({ vehiclePlate: 'kyverum_runt' });
  mocks.getCommercial.mockResolvedValue(null);
  mocks.getPrenda.mockResolvedValue(null);
  mocks.getActors.mockResolvedValue([]);
  mocks.listVehicleServiceTypes.mockResolvedValue([]);
  mocks.getChecklist.mockResolvedValue({ items: [], faltanObligatorios: 0, completo: true });
  mocks.getAttachments.mockResolvedValue([]);
  mocks.listTransitOffices.mockResolvedValue([]);
  mocks.getBiometricState.mockResolvedValue({ validations: [], provider: 'mock' });
  mocks.getInstanceIdentityValidationAlerts.mockResolvedValue({ alerts: [], total: 0 });
  mocks.listBiometric.mockResolvedValue([]);
  mocks.listFirmas.mockResolvedValue([]);
  mocks.listParticipantes.mockResolvedValue([]);
});

function renderCancelacion() {
  return render(<TramiteWizard existingInstanceId="inst-1" onExit={() => {}} />);
}

describe('Cancelación de matrícula — Requisitos sin trámites complementarios', () => {
  it('no ofrece asignar prenda ni limitación a la propiedad', async () => {
    renderCancelacion();

    await screen.findByRole('heading', { level: 2, name: 'Requisitos' });

    expect(
      screen.queryByText('Asignación de Prenda / Limitación a la Propiedad'),
    ).not.toBeInTheDocument();
  });

  it('no ofrece trámites simultáneos ni transformaciones del vehículo', async () => {
    renderCancelacion();

    await screen.findByRole('heading', { level: 2, name: 'Requisitos' });

    expect(
      screen.queryByText('Trámites Simultáneos — Transformaciones del Vehículo'),
    ).not.toBeInTheDocument();
  });

  it('sí pregunta la causal, que es lo que este trámite tiene que declarar', async () => {
    renderCancelacion();

    await screen.findByRole('heading', { level: 2, name: 'Requisitos' });

    // Por rol: el rótulo aparece dos veces —el acordeón y la tarjeta de dentro—, y buscar por texto
    // suelto choca con las dos.
    expect(
      await screen.findByRole('heading', { level: 3, name: 'Causal de cancelación' }),
    ).toBeInTheDocument();
  });
});
