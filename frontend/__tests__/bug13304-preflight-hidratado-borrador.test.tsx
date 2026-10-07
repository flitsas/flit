import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';

import type { WizardCapabilities, WizardState } from '@/lib/api/types/procedure-runtime';

// Uso de ejemplo:
//   <TramiteWizard existingInstanceId="inst-ict" /> con un borrador que abre en «Requisitos»
//   → hidrata `preflight` con GET /instances/{id}/preflight (una vez) y PrendaForm pinta
//     «RUNT reporta gravamen o prenda» si el check `gravamenes` viene en warn.

/**
 * Bug #13304 (numeral 3) — un borrador creado por ICT abre en la frontera (paso posterior a la
 * consulta). El `preflight` solo se traía al entrar a «consulta»/«consulta_vin», así que el check
 * `gravamenes` nunca llegaba y el aviso de gravamen/prenda del RUNT no se veía aunque el snapshot
 * existiera en BD. La hidratación lee el snapshot persistido y NUNCA re-ejecuta la consulta.
 */
const mocks = vi.hoisted(() => ({
  getCamaraComercioRequirements: vi.fn(() => Promise.resolve([])),
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
  getOrganismoRuntNoHabilitado: () => null,
  isVehicleBodyTypeMissing: () => false,
  isVehiclePrendaMissing: () => false,
}));

vi.mock('@/components/admin/Toast', () => ({ useToast: () => ({ show: vi.fn() }) }));
vi.mock('next/navigation', () => ({
  useRouter: () => ({ push: vi.fn(), replace: vi.fn(), prefetch: vi.fn() }),
}));

import { TramiteWizard } from '@/components/operacion/TramiteWizard';

const CAPS_TRASPASO: WizardCapabilities = {
  entryMode: 'PLATE',
  requiresSeller: true,
  requiresBuyer: true,
  allowsMultipleBuyer: false,
  requiresCommercialValue: true,
  requiresBiometrics: true,
  biometricActors: ['SELLER', 'BUYER'],
  hasPrendaGate: true,
};

/** Borrador ICT: la consulta ya está completa y el expediente abre en «Requisitos». */
const BORRADOR_ICT: WizardState = {
  modalidad: 'TRASPASOS',
  tipologiaCodigo: 'TRASPASO',
  typeName: 'Traspaso',
  capabilities: CAPS_TRASPASO,
  totalSteps: 4,
  canSubmit: false,
  blockers: [],
  status: 'borrador',
  allowedTransitions: [],
  persistedCurrentStep: null,
  steps: [
    { index: 0, key: 'consulta', label: 'Consulta placa', status: 'complete', reasons: [] },
    { index: 1, key: 'documentos', label: 'Requisitos', status: 'incomplete', reasons: [] },
    { index: 2, key: 'identidad', label: 'Identidad', status: 'locked', reasons: [] },
    { index: 3, key: 'fur', label: 'FUR', status: 'locked', reasons: [] },
  ],
} as unknown as WizardState;

const MENSAJE_GRAVAMEN = 'Prenda vigente a favor de BANCO DE PRUEBA S.A.';

const SNAPSHOT_CON_GRAVAMEN = {
  overall: 'yellow',
  checks: [
    {
      key: 'gravamenes',
      label: 'Gravámenes',
      status: 'warn',
      source: 'RUNT',
      message: MENSAJE_GRAVAMEN,
    },
  ],
  createdAt: '2026-10-07T00:00:00Z',
};

beforeEach(() => {
  vi.clearAllMocks();
  mocks.getInstance.mockResolvedValue({
    id: 'inst-ict',
    status: 'borrador',
    draftFinalizedAt: null,
    fieldValues: [],
    actors: [],
    currentStep: null,
  });
  mocks.getWizardState.mockResolvedValue(BORRADOR_ICT);
  mocks.patchFieldValues.mockResolvedValue({ id: 'inst-ict', fieldValues: [] });
  mocks.setCurrentStep.mockResolvedValue({ id: 'inst-ict', currentStep: 'documentos' });
  mocks.runPreflight.mockResolvedValue({ overall: 'green', checks: [], createdAt: '2026-10-07T00:00:00Z' });
  mocks.getPreflight.mockResolvedValue(SNAPSHOT_CON_GRAVAMEN);
  mocks.getConsultationConfig.mockResolvedValue({ vehiclePlate: 'kyverum_runt' });
  mocks.getCommercial.mockResolvedValue(null);
  mocks.getPrenda.mockResolvedValue([]);
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

function renderBorradorIct() {
  return render(<TramiteWizard existingInstanceId="inst-ict" onExit={() => {}} />);
}

describe('Bug #13304 — preflight hidratado desde el snapshot persistido', () => {
  it('happy: borrador abierto en Requisitos pinta el aviso RUNT de gravamen y prenda (getPreflight 1 vez)', async () => {
    renderBorradorIct();

    await screen.findByRole('heading', { level: 2, name: 'Requisitos' });

    expect(await screen.findByText('RUNT reporta gravamen o prenda')).toBeInTheDocument();
    expect(screen.getByText(MENSAJE_GRAVAMEN)).toBeInTheDocument();
    expect(mocks.getPreflight).toHaveBeenCalledTimes(1);
    expect(mocks.getPreflight).toHaveBeenCalledWith('inst-ict');
  });

  it('contrato: la hidratación NO re-ejecuta la consulta a proveedores (runPreflight no se llama)', async () => {
    renderBorradorIct();

    await screen.findByRole('heading', { level: 2, name: 'Requisitos' });
    await waitFor(() => expect(mocks.getPreflight).toHaveBeenCalledTimes(1));
    await screen.findByText('RUNT reporta gravamen o prenda');

    expect(mocks.runPreflight).not.toHaveBeenCalled();
  });

  it('edge: un 404 (snapshot inexistente → null) o un error no rompe el paso ni muestra el aviso', async () => {
    mocks.getPreflight.mockRejectedValueOnce(new Error('404 Not Found'));
    renderBorradorIct();

    await screen.findByRole('heading', { level: 2, name: 'Requisitos' });
    await waitFor(() => expect(mocks.getPreflight).toHaveBeenCalledTimes(1));

    expect(screen.getByText('Asignación de Prenda / Limitación a la Propiedad')).toBeInTheDocument();
    expect(screen.queryByText('RUNT reporta gravamen o prenda')).not.toBeInTheDocument();
    expect(mocks.runPreflight).not.toHaveBeenCalled();
  });

  it('contrato: al cambiar de expediente en un paso posterior hidrata el snapshot del nuevo (sin el aviso del anterior)', async () => {
    mocks.getPreflight.mockImplementation(async (id: string) =>
      id === 'inst-ict' ? SNAPSHOT_CON_GRAVAMEN : null,
    );
    const { rerender } = render(<TramiteWizard existingInstanceId="inst-otro" onExit={() => {}} />);

    await screen.findByRole('heading', { level: 2, name: 'Requisitos' });
    await waitFor(() => expect(mocks.getPreflight).toHaveBeenCalledWith('inst-otro'));
    expect(screen.queryByText('RUNT reporta gravamen o prenda')).not.toBeInTheDocument();

    rerender(<TramiteWizard existingInstanceId="inst-ict" onExit={() => {}} />);

    expect(await screen.findByText('RUNT reporta gravamen o prenda')).toBeInTheDocument();
    expect(mocks.getPreflight).toHaveBeenCalledWith('inst-ict');
    expect(mocks.getPreflight.mock.calls.filter(([id]) => id === 'inst-ict')).toHaveLength(1);
    expect(mocks.runPreflight).not.toHaveBeenCalled();
  });

  it('edge: getPreflight devuelve null (404 mapeado por el cliente) sin romper el paso', async () => {
    mocks.getPreflight.mockResolvedValueOnce(null);
    renderBorradorIct();

    await screen.findByRole('heading', { level: 2, name: 'Requisitos' });
    await waitFor(() => expect(mocks.getPreflight).toHaveBeenCalledTimes(1));

    expect(screen.queryByText('RUNT reporta gravamen o prenda')).not.toBeInTheDocument();
    expect(mocks.runPreflight).not.toHaveBeenCalled();
  });
});
