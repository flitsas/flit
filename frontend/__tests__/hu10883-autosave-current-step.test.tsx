import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

import type {
  CommercialData,
  ProcedureConfiguration,
  WizardState,
} from '@/lib/api/types/procedure-runtime';

/**
 * HU #10883 — Autosave por paso y reposición al reabrir. Archivo de tests DEDICADO (no comparte
 * `tramite-wizard.test.tsx`) a propósito: otro agente trabaja en paralelo sobre ActorsForm/
 * Validaciones/BiometricStep (HU #10886) y comparte ese archivo; aislar aquí evita conflictos de
 * merge sin perder cobertura de las dos ACs de esta HU.
 *
 * AC1 — Al avanzar de paso (tras la consulta de vehículo), el frontend persiste automáticamente el
 * paso vía PATCH /instances/{id}/current-step (`tramitesClient.setCurrentStep`).
 * AC2 — Al reabrir un borrador con pasos incompletos, el wizard se ubica en el paso persistido
 * (`wizard.persistedCurrentStep`, HU #10879); sin paso persistido, cae al primer paso incompleto
 * derivado de los gates (fallback intacto).
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
  submitInstance: vi.fn(),
  transitionInstance: vi.fn(),
  finalizeDraft: vi.fn(),
  // Dependencias de los componentes embebidos por paso (documentos/actores/biométrica/FUR).
  getActors: vi.fn(),
  saveActors: vi.fn(),
  runtPersonLookup: vi.fn(),
  ruesPersonLookup: vi.fn(),
  getChecklist: vi.fn(),
  getAttachments: vi.fn(),
  uploadAttachment: vi.fn(),
  deleteAttachment: vi.fn(),
  listTransitOffices: vi.fn(),
  getBiometricState: vi.fn(),
  iniciarBiometric: vi.fn(),
  simulateBiometric: vi.fn(),
  ensureIdentity: vi.fn(),
  // HU #10875 — panel consolidado de identidad (IdentityStatusPanel), montado por el wizard siempre
  // que hay instanceId.
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

const toastShow = vi.hoisted(() => vi.fn());
vi.mock('@/components/admin/Toast', () => ({
  useToast: () => ({ show: toastShow }),
}));

const routerPush = vi.hoisted(() => vi.fn());
vi.mock('next/navigation', () => ({
  useRouter: () => ({ push: routerPush, replace: vi.fn(), prefetch: vi.fn() }),
}));

import { TramiteWizard } from '@/components/operacion/TramiteWizard';

const CONFIG: ProcedureConfiguration = {
  id: 'type-1',
  code: 'MATRICULA_NUEVA',
  name: 'Matrícula inicial',
  family: 'MATRICULAS',
  publishedAt: '2026-06-18T00:00:00Z',
  conformationRules: [],
  steps: [],
};

// Matrícula: consulta_vin ya completa (vehículo consultado), documentos/comprador incompletos.
const MATRICULA_WIZARD: WizardState = {
  modalidad: 'matricula_inicial',
  tipologiaCodigo: 'matricula_inicial',
  totalSteps: 5,
  canSubmit: false,
  blockers: ['documentos_incompletos'],
  status: 'borrador',
  allowedTransitions: ['anulado', 'preparado'],
  steps: [
    { index: 0, key: 'consulta_vin', label: 'Consulta VIN', status: 'complete', reasons: [] },
    { index: 1, key: 'documentos', label: 'Datos y Documentos del Trámite', status: 'incomplete', reasons: ['documentos_incompletos'] },
    { index: 2, key: 'comprador', label: 'Comprador', status: 'incomplete', reasons: ['runt_comprador'] },
    { index: 3, key: 'identidad', label: 'Identidad', status: 'locked', reasons: [] },
    { index: 4, key: 'fur', label: 'FUR', status: 'locked', reasons: [] },
  ],
};

const TRASPASO_VENDEDOR_FRONTIER: WizardState = {
  modalidad: 'traspaso',
  tipologiaCodigo: 'traspaso',
  totalSteps: 6,
  canSubmit: false,
  blockers: [],
  status: 'borrador',
  allowedTransitions: ['anulado', 'preparado'],
  steps: [
    { index: 0, key: 'consulta', label: 'Consulta', status: 'complete', reasons: [] },
    { index: 1, key: 'documentos', label: 'Datos y Documentos del Trámite', status: 'complete', reasons: [] },
    { index: 2, key: 'vendedor', label: 'Vendedor', status: 'incomplete', reasons: ['vendedor_incompleto'] },
    { index: 3, key: 'comprador', label: 'Comprador', status: 'locked', reasons: [] },
    { index: 4, key: 'comercial', label: 'Comercial', status: 'locked', reasons: [] },
    { index: 5, key: 'fur', label: 'FUR', status: 'locked', reasons: [] },
  ],
};
const TRASPASO_VENDEDOR_DONE: WizardState = {
  ...TRASPASO_VENDEDOR_FRONTIER,
  steps: TRASPASO_VENDEDOR_FRONTIER.steps.map((s) =>
    s.index === 2
      ? { ...s, status: 'complete', reasons: [] as string[] }
      : s.index === 3
        ? { ...s, status: 'incomplete', reasons: ['comprador_incompleto'] }
        : s,
  ),
};

const EMPTY_COMMERCIAL: CommercialData = {
  valorVenta: null,
  causal: null,
  tasaImpuesto: null,
  derechos: null,
  metodoPago: null,
};

beforeEach(() => {
  vi.clearAllMocks();
  mocks.createInstance.mockResolvedValue({ id: 'inst-1' });
  mocks.getInstance.mockResolvedValue({ id: 'inst-1', fieldValues: [] });
  mocks.getWizardState.mockResolvedValue(MATRICULA_WIZARD);
  mocks.patchFieldValues.mockResolvedValue({ id: 'inst-1', fieldValues: [] });
  mocks.setCurrentStep.mockResolvedValue({ id: 'inst-1', currentStep: 'documentos' });
  mocks.runPreflight.mockResolvedValue({ overall: 'green', checks: [], createdAt: '2026-06-18T00:00:00Z' });
  mocks.getPreflight.mockResolvedValue(null);
  mocks.getConsultationConfig.mockResolvedValue({
    vehicleVin: 'kyverum_runt',
    vehiclePlate: 'kyverum_runt',
    conductor: 'kyverum_runt_conductor',
  });
  mocks.getCommercial.mockResolvedValue(EMPTY_COMMERCIAL);
  mocks.putCommercial.mockResolvedValue(EMPTY_COMMERCIAL);
  mocks.submitInstance.mockResolvedValue({ id: 'inst-1' });
  mocks.transitionInstance.mockResolvedValue({ id: 'inst-1', status: 'preparado' });
  mocks.finalizeDraft.mockResolvedValue({ id: 'inst-1', status: 'borrador', draftFinalizedAt: '2026-06-24T12:00:00Z' });
  mocks.getActors.mockResolvedValue([]);
  mocks.saveActors.mockResolvedValue(undefined);
  mocks.runtPersonLookup.mockResolvedValue({
    found: true,
    fullName: 'Pedro Vendedor',
    firstName: 'Pedro',
    lastName: 'Vendedor',
    documentType: 'CC',
    documentNumber: '999',
    source: 'RUNT',
    mode: 'mock',
  });
  mocks.ruesPersonLookup.mockResolvedValue({
    found: true,
    razonSocial: 'Empresa SAS',
    documentNumber: '900',
    source: 'RUES',
    mode: 'mock',
  });
  mocks.getChecklist.mockResolvedValue({ items: [], faltanObligatorios: 0, completo: true });
  mocks.getAttachments.mockResolvedValue([]);
  mocks.listTransitOffices.mockResolvedValue([]);
  mocks.getBiometricState.mockResolvedValue({ validations: [], provider: 'mock' });
  mocks.getInstanceIdentityValidationAlerts.mockResolvedValue({ alerts: [], total: 0 });
  mocks.simulateBiometric.mockResolvedValue({ id: 'bio-1', status: 'aprobado' });
  mocks.iniciarBiometric.mockResolvedValue({ validation: { id: 'bio-1', status: 'en_proceso' } });
  mocks.listBiometric.mockResolvedValue([]);
  mocks.listFirmas.mockResolvedValue([]);
  mocks.listParticipantes.mockResolvedValue([]);
  mocks.ensureIdentity.mockResolvedValue({ outcome: 'ya_vigente' });
});

function renderMatricula() {
  return render(
    <TramiteWizard configuration={CONFIG} procedureTypeId="type-1" onExit={() => {}} />,
  );
}

describe('HU #10883 — AC1: autosave del paso al avanzar', () => {
  it('al pulsar Continuar desde Consulta VIN (tras consultar el vehículo), persiste el paso destino vía PATCH current-step', async () => {
    const user = userEvent.setup();
    renderMatricula();
    await screen.findByRole('button', { name: /^Paso 1: Consulta Vehículo/ });

    expect(mocks.setCurrentStep).not.toHaveBeenCalled();

    await user.click(screen.getByRole('button', { name: /^Continuar y guardar$/ }));

    await waitFor(() =>
      expect(mocks.setCurrentStep).toHaveBeenCalledWith('inst-1', 'documentos'),
    );
  });

  it('al navegar por el sidebar a un paso posterior (avance), también persiste el paso', async () => {
    const user = userEvent.setup();
    // Documentos y Comprador completos: Comprador queda navegable directamente desde el sidebar.
    mocks.getWizardState.mockResolvedValue({
      ...MATRICULA_WIZARD,
      steps: MATRICULA_WIZARD.steps.map((s) =>
        s.key === 'documentos' ? { ...s, status: 'complete' as const, reasons: [] } : s,
      ),
    });
    renderMatricula();
    await screen.findByRole('button', { name: /^Paso 1: Consulta Vehículo/ });

    await user.click(screen.getByRole('button', { name: /^Paso 3: Actores/ }));

    await waitFor(() =>
      expect(mocks.setCurrentStep).toHaveBeenCalledWith('inst-1', 'comprador'),
    );
  });

  it('al retroceder (Anterior) NO persiste el paso — solo se autoguarda al avanzar', async () => {
    const user = userEvent.setup();
    mocks.getWizardState.mockResolvedValue({
      ...MATRICULA_WIZARD,
      steps: MATRICULA_WIZARD.steps.map((s) =>
        s.key === 'documentos' ? { ...s, status: 'complete' as const, reasons: [] } : s,
      ),
    });
    renderMatricula();
    await screen.findByRole('button', { name: /^Paso 1: Consulta Vehículo/ });

    await user.click(screen.getByRole('button', { name: /^Continuar y guardar$/ }));
    await waitFor(() =>
      expect(mocks.setCurrentStep).toHaveBeenCalledWith('inst-1', 'documentos'),
    );
    mocks.setCurrentStep.mockClear();

    await user.click(screen.getByRole('button', { name: /Anterior/ }));
    await screen.findByRole('heading', { level: 2, name: 'Consulta Vehículo' });

    expect(mocks.setCurrentStep).not.toHaveBeenCalled();
  });

  it('un fallo del autosave (p.ej. vehículo aún no consultado) NO bloquea la navegación (silencioso)', async () => {
    const user = userEvent.setup();
    mocks.setCurrentStep.mockRejectedValue(new Error('vehiculo_no_consultado'));
    renderMatricula();
    await screen.findByRole('button', { name: /^Paso 1: Consulta Vehículo/ });

    await user.click(screen.getByRole('button', { name: /^Continuar y guardar$/ }));

    // El wizard avanza igual (heading del paso 2) y no aparece ningún error visible.
    expect(await screen.findByRole('heading', { level: 2, name: 'Requisitos' })).toBeInTheDocument();
    expect(screen.queryByRole('alert')).not.toBeInTheDocument();
  });

  it('"Guardar y continuar" en un paso con form embebido (vendedor) también persiste el paso siguiente', async () => {
    const user = userEvent.setup();
    let wizardCalls = 0;
    mocks.getWizardState.mockImplementation(async () => {
      wizardCalls += 1;
      return wizardCalls === 1 ? TRASPASO_VENDEDOR_FRONTIER : TRASPASO_VENDEDOR_DONE;
    });
    mocks.getActors.mockResolvedValue([
      { rol: 'vendedor', tipoDocumento: 'CC', numeroDocumento: '999', nombreCompleto: 'Pedro Vendedor', email: 'pedro@x.com' },
    ]);

    render(<TramiteWizard configuration={{ ...CONFIG, code: 'TRASPASO_STD' }} procedureTypeId="type-1" onExit={() => {}} />);
    await screen.findByRole('button', { name: /^Paso 1: Consulta Vehículo/ });

    await user.click(screen.getByRole('button', { name: /^Paso 3: Actores/ }));
    await screen.findByDisplayValue('Pedro Vendedor');
    // Sin consulta RUNT lista el footer queda deshabilitado (actorsConsultationReady).
    await screen.findByText(/Persona encontrada en RUNT/i);

    await user.click(screen.getByRole('button', { name: /Guardar y continuar/ }));

    await waitFor(() => expect(mocks.saveActors).toHaveBeenCalledTimes(1));
    await waitFor(() =>
      expect(mocks.setCurrentStep).toHaveBeenCalledWith('inst-1', 'comprador'),
    );
  });
});

// Matrícula con datos completos pero identidad diferida pendiente (mismo patrón que HU #10350): FUR
// es alcanzable pese a que Identidad quede incomplete (paso DIFERIDO, no bloquea la cascada — ver
// `wizard-navigation.ts`). El operador pudo avanzar de Identidad a FUR (`goToStep` ya exige
// `canNavigateToStep`, así que si llegó ahí es porque era navegable) y el autosave persistió 'fur'.
// Distingue de forma realista el punto de retoma persistido (fur) del frontier derivado ingenuo
// (primer paso NO 'complete' = identidad), sin violar la cascada al reabrir.
const MATRICULA_IDENTIDAD_DIFERIDA: WizardState = {
  modalidad: 'matricula_inicial',
  tipologiaCodigo: 'matricula_inicial',
  totalSteps: 5,
  canSubmit: true,
  blockers: [],
  status: 'borrador',
  allowedTransitions: ['anulado', 'preparado'],
  steps: [
    { index: 0, key: 'consulta_vin', label: 'Consulta VIN', status: 'complete', reasons: [] },
    { index: 1, key: 'documentos', label: 'Datos y Documentos del Trámite', status: 'complete', reasons: [] },
    { index: 2, key: 'comprador', label: 'Comprador', status: 'complete', reasons: [] },
    { index: 3, key: 'identidad', label: 'Identidad', status: 'incomplete', reasons: ['identidad_pendiente'] },
    { index: 4, key: 'fur', label: 'FUR', status: 'incomplete', reasons: ['fur_pendiente'] },
  ],
};

describe('HU #10883 — AC2: reposición en el paso persistido al reabrir', () => {
  it('con paso persistido (fur, alcanzable pese a Identidad diferida incompleta), abre ahí y NO en el frontier derivado (identidad)', async () => {
    mocks.getWizardState.mockResolvedValue({
      ...MATRICULA_IDENTIDAD_DIFERIDA,
      persistedCurrentStep: 'fur',
    });
    mocks.getInstance.mockResolvedValue({
      id: 'inst-1',
      status: 'borrador',
      draftFinalizedAt: null,
      fieldValues: [],
      actors: [],
      currentStep: 'fur',
    });

    render(<TramiteWizard existingInstanceId="inst-1" onExit={() => {}} />);

    expect(
      await screen.findByRole('heading', { level: 2, name: 'Resumen' }),
    ).toBeInTheDocument();
    expect(
      screen.queryByRole('heading', { level: 2, name: 'Validación de Identidad' }),
    ).not.toBeInTheDocument();
  });

  it('con paso persistido que YA NO es alcanzable por la cascada (gate retrocedió), cae al frontier derivado (no pelea con la corrección de navegación)', async () => {
    // 'comprador' quedó persistido, pero 'documentos' volvió a quedar incompleto (p.ej. se eliminó un
    // adjunto) antes de reabrir: comprador ya no es navegable por cascada → debe caer a Documentos.
    mocks.getWizardState.mockResolvedValue({
      ...MATRICULA_WIZARD,
      persistedCurrentStep: 'comprador',
    });
    mocks.getInstance.mockResolvedValue({
      id: 'inst-1',
      status: 'borrador',
      draftFinalizedAt: null,
      fieldValues: [],
      actors: [],
      currentStep: 'comprador',
    });

    render(<TramiteWizard existingInstanceId="inst-1" onExit={() => {}} />);

    expect(
      await screen.findByRole('heading', { level: 2, name: 'Requisitos' }),
    ).toBeInTheDocument();
    expect(
      screen.queryByRole('heading', { level: 2, name: 'Actores' }),
    ).not.toBeInTheDocument();
  });

  it('sin paso persistido, cae al primer paso incompleto derivado de los gates (fallback intacto)', async () => {
    mocks.getWizardState.mockResolvedValue({
      ...MATRICULA_WIZARD,
      persistedCurrentStep: null,
    });
    mocks.getInstance.mockResolvedValue({
      id: 'inst-1',
      status: 'borrador',
      draftFinalizedAt: null,
      fieldValues: [],
      actors: [],
      currentStep: null,
    });

    render(<TramiteWizard existingInstanceId="inst-1" onExit={() => {}} />);

    expect(
      await screen.findByRole('heading', { level: 2, name: 'Requisitos' }),
    ).toBeInTheDocument();
  });

  it('con paso persistido que ya no corresponde a un paso visible, cae al derivado (fallback)', async () => {
    mocks.getWizardState.mockResolvedValue({
      ...MATRICULA_WIZARD,
      persistedCurrentStep: 'paso_inexistente',
    });
    mocks.getInstance.mockResolvedValue({
      id: 'inst-1',
      status: 'borrador',
      draftFinalizedAt: null,
      fieldValues: [],
      actors: [],
      currentStep: 'paso_inexistente',
    });

    render(<TramiteWizard existingInstanceId="inst-1" onExit={() => {}} />);

    expect(
      await screen.findByRole('heading', { level: 2, name: 'Requisitos' }),
    ).toBeInTheDocument();
  });
});
