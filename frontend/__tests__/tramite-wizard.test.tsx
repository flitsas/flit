import { StrictMode } from 'react';
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

import type {
  CommercialData,
  PreflightSnapshot,
  ProcedureConfiguration,
  WizardState,
} from '@/lib/api/types/procedure-runtime';

// AC1 (HU #10882) — error mínimo con la misma forma `{ status, problem }` que
// TramitesApiError (lib/api/tramites-client.ts) para simular el 409 DUPLICATE_ACTIVE_PROCEDURE
// que devuelve el preflight (HU #10876). getDuplicateActiveProcedureId detecta por forma (duck
// typing), no por `instanceof`, así que esta clase local basta sin depender de la real.
class FakeTramitesApiError extends Error {
  constructor(
    public readonly status: number,
    message: string,
    public readonly problem: Record<string, unknown> | null,
  ) {
    super(message);
  }
}

// ── Mock del cliente HTTP (sin red real) ───────────────────────────
const mocks = vi.hoisted(() => ({
  createInstance: vi.fn(),
  getInstance: vi.fn(),
  getWizardState: vi.fn(),
  patchFieldValues: vi.fn(),
  // HU #10883 — autosave del paso (PATCH current-step). Mockeado aquí también porque el wizard lo
  // dispara al avanzar de paso (goToStep/handleContinue), invocado por varios tests de este archivo
  // que no son específicos de HU #10883 (ver el archivo dedicado hu10883-autosave-current-step.test.tsx).
  setCurrentStep: vi.fn(),
  runPreflight: vi.fn(),
  getPreflight: vi.fn(),
  getConsultationConfig: vi.fn(),
  getCommercial: vi.fn(),
  putCommercial: vi.fn(),
  submitInstance: vi.fn(),
  transitionInstance: vi.fn(),
  finalizeDraft: vi.fn(),
  // dependencias de los componentes embebidos
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
  runtPersonLookup: vi.fn(),
  ruesPersonLookup: vi.fn(),
  lookupLegalRepresentativeByNit: vi.fn(),
  actorContactLookup: vi.fn(),
  // HU #10875 — panel consolidado de identidad (IdentityStatusPanel), montado por el wizard siempre
  // que hay instanceId.
  getInstanceIdentityValidationAlerts: vi.fn(),
  // dependencias del paso FUR (FirmaFurStep)
  listBiometric: vi.fn(),
  listFirmas: vi.fn(),
  listParticipantes: vi.fn(),
  // Feature #11066 — Preparar dispara FUR + impronta + consolidado.
  generarFur: vi.fn(),
  generarImpronta: vi.fn(),
  generarConsolidado: vi.fn(),
  getPrenda: vi.fn(),
  listMandateSigners: vi.fn(),
  setMandateSigner: vi.fn(),
}));

// AC1 (HU #10882) — el wizard también importa `getDuplicateActiveProcedureId` de este módulo; se
// reimplementa aquí (idéntica a lib/api/tramites-client.ts: duck-typing sobre `{status, problem}`,
// sin red) porque el módulo real está mockeado por completo más abajo.
function getDuplicateActiveProcedureId(err: unknown): string | null {
  if (!err || typeof err !== 'object') return null;
  const { status, problem } = err as { status?: unknown; problem?: unknown };
  if (status !== 409 || !problem || typeof problem !== 'object') return null;
  const { title, procedureInstanceId } = problem as { title?: unknown; procedureInstanceId?: unknown };
  if (title !== 'DUPLICATE_ACTIVE_PROCEDURE' || typeof procedureInstanceId !== 'string') return null;
  return procedureInstanceId;
}

// AC1/AC2 (HU #10884) — mismo patrón: reimplementación local (duck-typing sobre `{status, problem}`)
// de `getVehicleStateBlock` (lib/api/tramites-client.ts), porque el módulo real está mockeado abajo.
function getVehicleStateBlock(
  err: unknown,
): { vehicleStatus: string; procedureType: string } | null {
  if (!err || typeof err !== 'object') return null;
  const { status, problem } = err as { status?: unknown; problem?: unknown };
  if (status !== 422 || !problem || typeof problem !== 'object') return null;
  const { title, vehicleStatus, procedureType } = problem as {
    title?: unknown;
    vehicleStatus?: unknown;
    procedureType?: unknown;
  };
  if (title !== 'VEHICLE_STATE_INVALID_FOR_TYPE' || typeof vehicleStatus !== 'string') return null;
  return { vehicleStatus, procedureType: typeof procedureType === 'string' ? procedureType : '' };
}

// HU #11199/#11200 — misma reimplementación local de `isTransitOfficeUnavailable`, por la misma razón.
function isTransitOfficeUnavailable(err: unknown): boolean {
  if (!err || typeof err !== 'object') return false;
  const { status, problem } = err as { status?: unknown; problem?: unknown };
  if (status !== 422 || !problem || typeof problem !== 'object') return false;
  return (problem as { title?: unknown }).title === 'TRANSIT_OFFICE_NOT_AVAILABLE';
}

vi.mock('@/lib/api/tramites-client', () => ({
  tramitesClient: mocks,
  DEV_TENANT_ID: 'tenant-dev',
  DEV_USER_ID: 'user-dev',
  getDuplicateActiveProcedureId,
  getVehicleStateBlock,
  isTransitOfficeUnavailable,
}));

// El wizard usa useToast() para el aviso de "enviado a tránsito"; se stubea para
// no exigir <ToastProvider> en cada render y poder asertar el mensaje.
const toastShow = vi.hoisted(() => vi.fn());
vi.mock('@/components/admin/Toast', () => ({
  useToast: () => ({ show: toastShow }),
}));

// HU #10539 — el paso de consulta usa useRouter() para el CTA "Iniciar traspaso".
const routerPush = vi.hoisted(() => vi.fn());
vi.mock('next/navigation', () => ({
  useRouter: () => ({ push: routerPush, replace: vi.fn(), prefetch: vi.fn() }),
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

const TRASPASO_WIZARD: WizardState = {
  modalidad: 'traspaso',
  tipologiaCodigo: 'traspaso',
  totalSteps: 6,
  canSubmit: false,
  blockers: ['preflight_red'],
  status: 'borrador',
  allowedTransitions: ['anulado', 'preparado'],
  steps: [
    { index: 0, key: 'consulta', label: 'Consulta', status: 'complete', reasons: [] },
    { index: 1, key: 'documentos', label: 'Datos y Documentos del Trámite', status: 'complete', reasons: [] },
    { index: 2, key: 'vendedor', label: 'Vendedor', status: 'complete', reasons: [] },
    { index: 3, key: 'comprador', label: 'Comprador', status: 'complete', reasons: [] },
    { index: 4, key: 'comercial', label: 'Comercial', status: 'complete', reasons: [] },
    { index: 5, key: 'fur', label: 'FUR', status: 'incomplete', reasons: ['fur_pendiente'] },
  ],
};

// Wizard con TODOS los pasos completos: el caso típico de un trámite ya enviado
// a tránsito (submitted), navegable de extremo a extremo en solo lectura.
const SUBMITTED_WIZARD: WizardState = {
  modalidad: 'matricula_inicial',
  tipologiaCodigo: 'matricula_inicial',
  totalSteps: 5,
  canSubmit: true,
  blockers: [],
  status: 'entregado',
  allowedTransitions: ['aprobado', 'rechazado'],
  steps: [
    { index: 0, key: 'consulta_vin', label: 'Consulta VIN', status: 'complete', reasons: [] },
    { index: 1, key: 'documentos', label: 'Datos y Documentos del Trámite', status: 'complete', reasons: [] },
    { index: 2, key: 'comprador', label: 'Comprador', status: 'complete', reasons: [] },
    { index: 3, key: 'identidad', label: 'Identidad', status: 'complete', reasons: [] },
    { index: 4, key: 'fur', label: 'FUR', status: 'complete', reasons: [] },
  ],
};

const GREEN_PREFLIGHT: PreflightSnapshot = {
  overall: 'green',
  checks: [
    { key: 'soat', label: 'SOAT', status: 'ok', source: 'RUNT', message: 'Vigente' },
  ],
  createdAt: '2026-06-18T00:00:00Z',
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
  mocks.setCurrentStep.mockResolvedValue({ id: 'inst-1', currentStep: null });
  mocks.runPreflight.mockResolvedValue(GREEN_PREFLIGHT);
  mocks.getPreflight.mockResolvedValue(null);
  // HU #10478 — por defecto Kyverum-first (el wizard oculta el tipo de documento en traspaso).
  mocks.getConsultationConfig.mockResolvedValue({
    vehicleVin: 'kyverum_runt',
    vehiclePlate: 'kyverum_runt',
    conductor: 'kyverum_runt_conductor',
  });
  mocks.getCommercial.mockResolvedValue(EMPTY_COMMERCIAL);
  mocks.putCommercial.mockResolvedValue(EMPTY_COMMERCIAL);
  mocks.submitInstance.mockResolvedValue({ id: 'inst-1' });
  mocks.transitionInstance.mockImplementation((_id: string, status: string) => Promise.resolve({ id: 'inst-1', status }));
  mocks.finalizeDraft.mockResolvedValue({ id: 'inst-1', status: 'borrador', draftFinalizedAt: '2026-06-24T12:00:00Z' });
  mocks.getActors.mockResolvedValue([]);
  mocks.saveActors.mockResolvedValue(undefined);
  mocks.lookupLegalRepresentativeByNit.mockResolvedValue(null);
  mocks.actorContactLookup.mockResolvedValue({ found: false });
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
  mocks.generarFur.mockResolvedValue({ documents: [] });
  mocks.generarImpronta.mockResolvedValue({
    attachmentId: 'imp-1',
    filename: 'impronta.pdf',
    sha256: 'imp',
    radicado: 'R-1',
    hash: 'h',
  });
  mocks.getPrenda.mockResolvedValue(null);
  mocks.listMandateSigners.mockResolvedValue({ opciones: [], elegidoId: null, editable: true });
  mocks.setMandateSigner.mockResolvedValue(undefined);
  mocks.generarConsolidado.mockResolvedValue({
    document: { attachmentId: 'c-1', tipo: 'consolidado', filename: 'c.pdf', sha256: 'abc' },
    regenerado: true,
  });
  // Por defecto la identidad ya está vigente (no dispara nueva validación al guardar la parte).
  mocks.ensureIdentity.mockResolvedValue({ outcome: 'ya_vigente' });
});

function renderWizard() {
  return render(
    <TramiteWizard configuration={CONFIG} procedureTypeId="type-1" onExit={() => {}} />,
  );
}

function renderWizardStrict() {
  return render(
    <StrictMode>
      <TramiteWizard configuration={CONFIG} procedureTypeId="type-1" onExit={() => {}} />
    </StrictMode>,
  );
}

describe('TramiteWizard — sidebar server-driven por modalidad', () => {
  it('matrícula pinta 5 pasos (VIN-first)', async () => {
    renderWizard();
    // 5 pasos en el sidebar, etiquetados por aria-label único.
    const stepButtons = await screen.findAllByRole('button', { name: /^Paso \d+:/ });
    expect(stepButtons).toHaveLength(5);
    expect(screen.getByRole('button', { name: /^Paso 1: Consulta VIN/ })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /^Paso 2: Datos y Documentos/ })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /^Paso 3: Comprador/ })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /^Paso 4: Identidad/ })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /^Paso 5: FUR/ })).toBeInTheDocument();
  });

  it('traspaso pinta 6 pasos (placa-first)', async () => {
    mocks.getWizardState.mockResolvedValue(TRASPASO_WIZARD);
    renderWizard();
    const stepButtons = await screen.findAllByRole('button', { name: /^Paso \d+:/ });
    expect(stepButtons).toHaveLength(6);
    expect(screen.getByText('Datos y Documentos del Trámite')).toBeInTheDocument();
    expect(screen.getByText('Comercial')).toBeInTheDocument();
  });
});

describe('TramiteWizard — instancia existente (Track B)', () => {
  it('con existingInstanceId NO crea instancia y carga el wizard de ese id', async () => {
    render(<TramiteWizard existingInstanceId="inst-99" onExit={() => {}} />);

    // El wizard server-driven se hidrata con el id de la URL...
    const stepButtons = await screen.findAllByRole('button', { name: /^Paso \d+:/ });
    expect(stepButtons).toHaveLength(5);
    // El tenant se resuelve dentro de tramitesClient (tenant activo del `?t=` → JWT), no se fuerza
    // desde el hook; por eso el 2º arg es undefined (antes se pasaba DEV_TENANT_ID hardcodeado).
    expect(mocks.getWizardState).toHaveBeenCalledWith('inst-99', undefined);
    // ...y NO dispara un POST /instances (F5 reabre, no re-crea).
    expect(mocks.createInstance).not.toHaveBeenCalled();
  });

  it('reanuda en la frontera (primer paso incompleto), no en el paso 1', async () => {
    // MATRICULA_WIZARD: paso 1 (Consulta VIN) completo, paso 2 (Documentos) incompleto.
    // Al abrir la instancia existente, el cuerpo debe arrancar en Documentos.
    render(<TramiteWizard existingInstanceId="inst-99" onExit={() => {}} />);

    // El título del paso activo (h2 del cuerpo) es "Datos y Documentos del Trámite", no "Consulta VIN".
    expect(
      await screen.findByRole('heading', { level: 2, name: 'Datos y Documentos del Trámite' }),
    ).toBeInTheDocument();
    expect(
      screen.queryByRole('heading', { level: 2, name: 'Consulta VIN' }),
    ).not.toBeInTheDocument();
  });

  it('un trámite sin avance (paso 1 = frontera) abre en el paso 1', async () => {
    // Todos los pasos incompletos: la frontera es el paso 1 (Consulta VIN).
    mocks.getWizardState.mockResolvedValue({
      ...MATRICULA_WIZARD,
      steps: [
        { index: 0, key: 'consulta_vin', label: 'Consulta VIN', status: 'incomplete', reasons: [] },
        { index: 1, key: 'documentos', label: 'Datos y Documentos del Trámite', status: 'locked', reasons: [] },
        { index: 2, key: 'comprador', label: 'Comprador', status: 'locked', reasons: [] },
        { index: 3, key: 'identidad', label: 'Identidad', status: 'locked', reasons: [] },
        { index: 4, key: 'fur', label: 'FUR', status: 'locked', reasons: [] },
      ],
    });
    render(<TramiteWizard existingInstanceId="inst-77" onExit={() => {}} />);

    expect(
      await screen.findByRole('heading', { level: 2, name: 'Consulta VIN' }),
    ).toBeInTheDocument();
  });
});

describe('TramiteWizard — solo lectura (Track C)', () => {
  beforeEach(() => {
    mocks.getWizardState.mockResolvedValue(SUBMITTED_WIZARD);
    // El estado submitted activa el modo solo lectura.
    mocks.getInstance.mockResolvedValue({
      id: 'inst-sub',
      status: 'entregado',
      fieldValues: [],
    });
  });

  it('muestra el banner de solo lectura y oculta Continuar/Finalizar', async () => {
    render(<TramiteWizard existingInstanceId="inst-sub" onExit={() => {}} />);

    expect(await screen.findByText(/solo visualización/i)).toBeInTheDocument();
    // El botón de salida pasa a "Volver al listado".
    expect(
      screen.getByRole('button', { name: /Volver al listado/ }),
    ).toBeInTheDocument();
    // Sin acciones de edición en el footer.
    expect(screen.queryByRole('button', { name: 'Finalizar' })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /Continuar/ })).not.toBeInTheDocument();
    expect(
      screen.queryByRole('button', { name: /Guardar y continuar/ }),
    ).not.toBeInTheDocument();
  });

  it('los inputs del paso de consulta están deshabilitados y sin Consultar RUNT', async () => {
    const user = userEvent.setup();
    render(<TramiteWizard existingInstanceId="inst-sub" onExit={() => {}} />);

    // Consulta VIN es un paso completo → navegable en solo lectura.
    const consultaTab = await screen.findByRole('button', { name: /^Paso 1: Consulta VIN/ });
    await user.click(consultaTab);

    const vin = await screen.findByLabelText('Número VIN');
    expect(vin).toBeDisabled();
    expect(
      screen.queryByRole('button', { name: 'Consultar RUNT' }),
    ).not.toBeInTheDocument();
  });
});

// HU #11053 — el aviso superior describía siempre un envío a tránsito, incluso en trámites aprobados,
// rechazados o anulados, y ofrecía generar documentación que desde la HU #11051 el backend rechaza en
// estado final.
describe('TramiteWizard — aviso acorde al estado real (HU #11053)', () => {
  const renderEnEstado = (status: string) => {
    mocks.getWizardState.mockResolvedValue({ ...SUBMITTED_WIZARD, status });
    mocks.getInstance.mockResolvedValue({ id: 'inst-sub', status, fieldValues: [] });
    render(<TramiteWizard existingInstanceId="inst-sub" onExit={() => {}} />);
  };

  it('entregado: anuncia el envío a tránsito y que aún puede generar el consolidado', async () => {
    renderEnEstado('entregado');
    expect(await screen.findByText(/Enviado a tránsito — solo visualización/i)).toBeInTheDocument();
    expect(screen.getByText(/aún puedes generar o descargar el expediente consolidado/i)).toBeInTheDocument();
  });

  it('aprobado: anuncia la aprobación y que la documentación ya no se regenera', async () => {
    renderEnEstado('aprobado');
    expect(await screen.findByText(/Trámite aprobado — solo visualización/i)).toBeInTheDocument();
    expect(screen.getByText(/ya no se regenera/i)).toBeInTheDocument();
    // Lo que el estado NO permite no debe anunciarse.
    expect(screen.queryByText(/Enviado a tránsito/i)).not.toBeInTheDocument();
  });

  it('rechazado sin subsanación: anuncia el rechazo y remite al motivo', async () => {
    renderEnEstado('rechazado');
    expect(await screen.findByText(/Trámite rechazado — solo visualización/i)).toBeInTheDocument();
    expect(screen.queryByText(/Enviado a tránsito/i)).not.toBeInTheDocument();
  });

  it('anulado: anuncia que quedó sin efecto y no ofrece generar', async () => {
    renderEnEstado('anulado');
    expect(await screen.findByText(/Trámite anulado — solo visualización/i)).toBeInTheDocument();
    expect(screen.getByText(/no editarlo ni regenerarlo/i)).toBeInTheDocument();
    expect(screen.queryByText(/Enviado a tránsito/i)).not.toBeInTheDocument();
  });
});

describe('TramiteWizard — status y reasons traducidos', () => {
  it('traduce los códigos de reason a copy amigable', async () => {
    renderWizard();
    await screen.findByRole('button', { name: /^Paso 1: Consulta VIN/ });
    expect(screen.getByText(/Faltan documentos obligatorios/)).toBeInTheDocument();
    expect(screen.getByText(/Consulta RUNT del comprador/)).toBeInTheDocument();
  });

  it('los pasos locked no son clickables', async () => {
    renderWizard();
    await screen.findByRole('button', { name: /^Paso 1: Consulta VIN/ });
    const locked = screen.getByRole('button', { name: /^Paso 4: Identidad \(locked\)/ });
    expect(locked).toBeDisabled();
  });
});

describe('TramiteWizard — navegación en cascada (frontera)', () => {
  it('Identidad no es clickeable cuando Comprador (paso previo) está incompleto', async () => {
    // Defensa de frontend: aunque el backend devolviera Identidad como 'incomplete'
    // (no 'locked'), solo se navega a pasos completos o a la frontera (primer
    // incompleto = Comprador). Identidad queda fuera de alcance → no clickeable.
    mocks.getWizardState.mockResolvedValue({
      ...MATRICULA_WIZARD,
      steps: [
        { index: 0, key: 'consulta_vin', label: 'Consulta VIN', status: 'complete', reasons: [] },
        { index: 1, key: 'documentos', label: 'Datos y Documentos del Trámite', status: 'complete', reasons: [] },
        { index: 2, key: 'comprador', label: 'Comprador', status: 'incomplete', reasons: ['runt_comprador'] },
        { index: 3, key: 'identidad', label: 'Identidad', status: 'incomplete', reasons: ['identidad_pendiente'] },
        { index: 4, key: 'fur', label: 'FUR', status: 'incomplete', reasons: ['fur_pendiente'] },
      ],
    });
    renderWizard();
    await screen.findByRole('button', { name: /^Paso 1: Consulta VIN/ });
    // Comprador es la frontera → navegable.
    expect(screen.getByRole('button', { name: /^Paso 3: Comprador/ })).toBeEnabled();
    // Identidad está más allá de la frontera → NO navegable, pese a no estar 'locked'.
    expect(screen.getByRole('button', { name: /^Paso 4: Identidad/ })).toBeDisabled();
  });
});

describe('TramiteWizard — Continuar', () => {
  it('Continuar habilitado en step complete', async () => {
    renderWizard();
    await screen.findByRole('button', { name: /^Paso 1: Consulta VIN/ });
    // Paso activo inicial = consulta_vin (complete) → Continuar habilitado.
    expect(screen.getByRole('button', { name: /Continuar/ })).toBeEnabled();
  });

  it('Continuar deshabilitado al navegar a un step incompleto', async () => {
    const user = userEvent.setup();
    renderWizard();
    await screen.findByRole('button', { name: /^Paso 1: Consulta VIN/ });
    // Navega al paso "Datos y Documentos del Trámite" (incomplete).
    await user.click(screen.getByRole('button', { name: /^Paso 2: Datos y Documentos/ }));
    expect(screen.getByRole('button', { name: /Continuar/ })).toBeDisabled();
  });
});

describe('TramiteWizard — Finalizar y blockers', () => {
  it('Finalizar deshabilitado con blockers y los muestra traducidos', async () => {
    // Wizard donde el último paso es alcanzable y canSubmit=false.
    mocks.getWizardState.mockResolvedValue(TRASPASO_WIZARD);
    const user = userEvent.setup();
    renderWizard();
    await screen.findByRole('button', { name: /^Paso 1: Consulta/ });
    // Navega al último paso (FUR, índice 5).
    await user.click(screen.getByRole('button', { name: /^Paso 6: FUR/ }));
    expect(screen.getByRole('button', { name: /Finalizar/ })).toBeDisabled();
    expect(screen.getByText(/Hay bloqueos críticos en el pre-vuelo/)).toBeInTheDocument();
  });

  it('N 03 dos pasos — con identidad aprobada el botón es "Radicar trámite" (borrador→preparado) y sale al listado (Feature #11211)', async () => {
    // Todos los pasos completos (incl. la biométrica → sin pendiente_biometria) ⇒ identidad
    // aprobada ⇒ el botón terminal es "Radicar trámite" (no "Radicar trámite" ni "Finalizar").
    const BORRADOR_COMPLETO: WizardState = {
      ...TRASPASO_WIZARD,
      canSubmit: true,
      blockers: [],
      steps: TRASPASO_WIZARD.steps.map((s) => ({ ...s, status: 'complete', reasons: [] as string[] })),
    };
    mocks.getWizardState.mockResolvedValue(BORRADOR_COMPLETO);
    // Organismo presente → al entrar al paso FUR se pre-genera el paquete.
    mocks.getInstance.mockResolvedValue({
      id: 'inst-1',
      status: 'borrador',
      fieldValues: [
        {
          formFieldId: null,
          fieldKey: 'transit_office_code',
          valueText: '11001',
          valueJson: null,
          source: 'runt',
        },
        {
          formFieldId: null,
          fieldKey: 'transit_office_name',
          valueText: 'OT Bogotá',
          valueJson: null,
          source: 'runt',
        },
      ],
      actors: [],
      statusHistory: [],
    });
    const onExit = vi.fn();
    const user = userEvent.setup();
    render(<TramiteWizard existingInstanceId="inst-1" onExit={onExit} />);
    await screen.findByRole('button', { name: /^Paso 1: Consulta/ });
    await user.click(screen.getByRole('button', { name: /^Paso 6: FUR/ }));

    const radicar = await waitFor(() => {
      const btn = screen.getByRole('button', { name: /^Radicar trámite$/ });
      expect(btn).toBeEnabled();
      return btn;
    });
    expect(screen.queryByRole('button', { name: /^Finalizar$/ })).not.toBeInTheDocument();
    await user.click(radicar);

    await waitFor(() =>
      expect(mocks.transitionInstance).toHaveBeenCalledWith('inst-1', 'preparado'),
    );
    await waitFor(() =>
      expect(mocks.transitionInstance).toHaveBeenCalledWith('inst-1', 'entregado'),
    );
    expect(mocks.submitInstance).not.toHaveBeenCalled();
    expect(mocks.finalizeDraft).not.toHaveBeenCalled();
    expect(toastShow).toHaveBeenCalledWith(
      expect.stringMatching(/enviado a tránsito/i),
      'success',
    );
    await waitFor(() => expect(onExit).toHaveBeenCalled());
  });

  it('N 03 — fallo de generación de docs tras preparado bloquea la entrega', async () => {
    const BORRADOR_COMPLETO: WizardState = {
      ...TRASPASO_WIZARD,
      canSubmit: true,
      blockers: [],
      steps: TRASPASO_WIZARD.steps.map((s) => ({ ...s, status: 'complete', reasons: [] as string[] })),
    };
    mocks.getWizardState.mockResolvedValue(BORRADOR_COMPLETO);
    mocks.getInstance.mockResolvedValue({
      id: 'inst-1',
      status: 'borrador',
      fieldValues: [
        {
          formFieldId: null,
          fieldKey: 'transit_office_code',
          valueText: '11001',
          valueJson: null,
          source: 'runt',
        },
      ],
      actors: [],
      statusHistory: [],
    });
    // Pre-gen falla; al radicar: preparado sí, pero sin consolidado no se entrega.
    mocks.generarFur.mockRejectedValue(new Error('fur_unavailable'));
    mocks.generarConsolidado.mockRejectedValue(new Error('fur_unavailable'));

    const onExit = vi.fn();
    const user = userEvent.setup();
    render(
      <TramiteWizard configuration={CONFIG} procedureTypeId="type-1" onExit={onExit} />,
    );
    await screen.findByRole('button', { name: /^Paso 1: Consulta/ });
    await user.click(screen.getByRole('button', { name: /^Paso 6: FUR/ }));

    const radicar = await screen.findByRole('button', { name: /^Radicar trámite$/ });
    expect(radicar).toBeEnabled();
    await user.click(radicar);

    await waitFor(() =>
      expect(mocks.transitionInstance).toHaveBeenCalledWith('inst-1', 'preparado'),
    );
    expect(mocks.transitionInstance).not.toHaveBeenCalledWith('inst-1', 'entregado');
    expect(
      await screen.findByText(/No se puede radicar/i),
    ).toBeInTheDocument();
    expect(onExit).not.toHaveBeenCalled();
  });

  it('N 03 dos pasos — en `preparado` el botón "Radicar trámite" transiciona a entregado, avisa y sale', async () => {
    // Instancia existente ya preparada: wizard en solo lectura con la acción de radicar.
    const PREPARADO: WizardState = {
      ...TRASPASO_WIZARD,
      canSubmit: true,
      blockers: [],
      status: 'preparado',
      allowedTransitions: ['entregado'],
      steps: TRASPASO_WIZARD.steps.map((s) => ({ ...s, status: 'complete', reasons: [] as string[] })),
    };
    mocks.getWizardState.mockResolvedValue(PREPARADO);
    mocks.getInstance.mockResolvedValue({ id: 'inst-1', status: 'preparado', draftFinalizedAt: null, fieldValues: [], actors: [] });
    mocks.transitionInstance.mockResolvedValue({ id: 'inst-1', status: 'entregado' });
    const onExit = vi.fn();
    const user = userEvent.setup();
    render(<TramiteWizard existingInstanceId="inst-1" onExit={onExit} />);

    // Reanuda en el paso de decisión (todo completo → frontera = último paso).
    const radicar = await screen.findByRole('button', { name: /Radicar trámite/ });
    expect(radicar).toBeEnabled();
    await user.click(radicar);

    await waitFor(() => {
      expect(mocks.generarConsolidado).toHaveBeenCalledWith('inst-1', undefined, true);
    });
    await waitFor(() =>
      expect(mocks.transitionInstance).toHaveBeenCalledWith('inst-1', 'entregado'),
    );
    expect(mocks.submitInstance).not.toHaveBeenCalled();
    // Toast de éxito + redirección inmediata (onExit), sin pantalla intermedia.
    expect(toastShow).toHaveBeenCalledWith(expect.stringMatching(/enviado a tránsito/i), 'success');
    expect(onExit).toHaveBeenCalledTimes(1);
  });

  it('N 03 — Radicar bloquea si el consolidado queda incompleto', async () => {
    const PREPARADO: WizardState = {
      ...TRASPASO_WIZARD,
      canSubmit: true,
      blockers: [],
      status: 'preparado',
      allowedTransitions: ['entregado'],
      steps: TRASPASO_WIZARD.steps.map((s) => ({ ...s, status: 'complete', reasons: [] as string[] })),
    };
    mocks.getWizardState.mockResolvedValue(PREPARADO);
    mocks.getInstance.mockResolvedValue({
      id: 'inst-1',
      status: 'preparado',
      draftFinalizedAt: null,
      fieldValues: [],
      actors: [],
    });
    mocks.getPrenda.mockResolvedValue(null);
  mocks.generarConsolidado.mockResolvedValue({
      document: { attachmentId: 'c-1', tipo: 'consolidado', filename: 'c.pdf', sha256: 'abc' },
      regenerado: true,
      incompleto: true,
      documentosFaltantes: ['fur', 'impronta'],
    });
    const onExit = vi.fn();
    const user = userEvent.setup();
    render(<TramiteWizard existingInstanceId="inst-1" onExit={onExit} />);

    await user.click(await screen.findByRole('button', { name: /Radicar trámite/ }));

    expect(
      await screen.findByText(/No se puede radicar: Expediente incompleto/i),
    ).toBeInTheDocument();
    expect(mocks.transitionInstance).not.toHaveBeenCalled();
    expect(onExit).not.toHaveBeenCalled();
  });

  it('N 03 dos pasos — el error del gate al preparar se muestra y el wizard sigue en borrador', async () => {
    const BORRADOR_COMPLETO: WizardState = {
      ...TRASPASO_WIZARD,
      canSubmit: true,
      blockers: [],
      steps: TRASPASO_WIZARD.steps.map((s) => ({ ...s, status: 'complete', reasons: [] as string[] })),
    };
    mocks.getWizardState.mockResolvedValue(BORRADOR_COMPLETO);
    mocks.transitionInstance.mockRejectedValue(
      new Error('La validación de identidad no está aprobada o no está vigente.'),
    );
    const user = userEvent.setup();
    renderWizard();
    await screen.findByRole('button', { name: /^Paso 1: Consulta/ });
    await user.click(screen.getByRole('button', { name: /^Paso 6: FUR/ }));
    await user.click(screen.getByRole('button', { name: /^Radicar trámite$/ }));

    expect(
      await screen.findByText(/validación de identidad no está aprobada/i),
    ).toBeInTheDocument();
    // Sigue en borrador: el botón "Preparar" continúa disponible para reintentar.
    expect(screen.getByRole('button', { name: /^Radicar trámite$/ })).toBeEnabled();
  });
});

describe('TramiteWizard — desacople validación identidad async (HU #10350)', () => {
  // Matrícula con datos completos pero identidad pendiente: el FUR (5) ahora es ALCANZABLE (incomplete,
  // no locked), así que es el paso de decisión donde se finaliza/radica. Identidad (4) → "Continuar".
  // canSubmit=true (datos listos; identidad diferida).
  const MATRICULA_DATA_DONE_IDENTITY_PENDING: WizardState = {
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
      { index: 3, key: 'identidad', label: 'Identidad', status: 'incomplete', reasons: ['identidad_pendiente', 'pendiente_biometria'] },
      { index: 4, key: 'fur', label: 'FUR', status: 'incomplete', reasons: ['fur_pendiente'] },
    ],
  };

  it('AC1 — el paso de decisión es FUR (5); Identidad ofrece "Continuar", no "Finalizar"', async () => {
    mocks.getWizardState.mockResolvedValue(MATRICULA_DATA_DONE_IDENTITY_PENDING);
    mocks.getInstance.mockResolvedValue({ id: 'inst-1', status: 'borrador', draftFinalizedAt: null, fieldValues: [], actors: [] });
    render(<TramiteWizard existingInstanceId="inst-1" onExit={() => {}} />);

    // Reanuda en Identidad (frontera). Ya NO es paso terminal → "Continuar" (no "Finalizar").
    await screen.findByRole('heading', { level: 2, name: 'Identidad' });
    expect(screen.getByRole('button', { name: /^Continuar$/ })).toBeEnabled();
    expect(screen.queryByRole('button', { name: /^Finalizar$/ })).not.toBeInTheDocument();
    // El paso 5 (FUR) es navegable aunque la identidad esté pendiente.
    expect(screen.getByRole('button', { name: /^Paso 5: FUR/ })).toBeEnabled();
  });

  it('AC1 — "Finalizar" en el paso FUR llama finalize-draft (no submit), avisa y vuelve al listado', async () => {
    mocks.getWizardState.mockResolvedValue(MATRICULA_DATA_DONE_IDENTITY_PENDING);
    mocks.getInstance.mockResolvedValue({ id: 'inst-1', status: 'borrador', draftFinalizedAt: null, fieldValues: [], actors: [] });
    const onExit = vi.fn();
    const user = userEvent.setup();
    render(<TramiteWizard existingInstanceId="inst-1" onExit={onExit} />);

    await screen.findByRole('heading', { level: 2, name: 'Identidad' });
    // Navega al paso 5 (FUR), el paso de decisión.
    await user.click(screen.getByRole('button', { name: /^Paso 5: FUR/ }));

    // Aviso de que el FUR/firma se generan automáticamente.
    expect(await screen.findByText(/se generarán automáticamente/i)).toBeInTheDocument();
    const finalizar = await screen.findByRole('button', { name: /^Finalizar$/ });
    expect(finalizar).toBeEnabled();
    expect(screen.queryByRole('button', { name: /Radicar trámite/ })).not.toBeInTheDocument();

    await user.click(finalizar);

    await waitFor(() => expect(mocks.finalizeDraft).toHaveBeenCalledWith('inst-1'));
    expect(mocks.submitInstance).not.toHaveBeenCalled();
    expect(toastShow).toHaveBeenCalledWith(expect.stringMatching(/pendiente validación del cliente/i), 'success');
    expect(onExit).toHaveBeenCalledTimes(1);
  });

  it('AC2 — borrador finalizado: datos en solo lectura, Identidad operable, Radicar trámite deshabilitado', async () => {
    mocks.getWizardState.mockResolvedValue(MATRICULA_DATA_DONE_IDENTITY_PENDING);
    // draftFinalizedAt presente ⇒ modo borrador finalizado (readOnly parcial).
    mocks.getInstance.mockResolvedValue({
      id: 'inst-1',
      status: 'borrador',
      draftFinalizedAt: '2026-06-20T10:00:00Z',
      fieldValues: [],
      actors: [],
    });
    const user = userEvent.setup();
    render(<TramiteWizard existingInstanceId="inst-1" onExit={() => {}} />);

    // Banner informativo de espera de validación (accesible: role=status).
    expect(await screen.findByText(/esperando validación del cliente/i)).toBeInTheDocument();

    // Identidad (frontera) sigue operable pese al readOnly parcial.
    await screen.findByRole('heading', { level: 2, name: 'Identidad' });
    expect(
      await screen.findByRole('button', { name: /Simular validación de identidad/ }),
    ).toBeEnabled();

    // Los datos quedan en solo lectura: el input del paso de consulta está deshabilitado.
    await user.click(screen.getByRole('button', { name: /^Paso 1: Consulta VIN/ }));
    expect(await screen.findByLabelText('Número VIN')).toBeDisabled();

    // En el paso FUR (decisión), "Radicar trámite" deshabilitado hasta validar; sin "Finalizar" (ya finalizado).
    await user.click(screen.getByRole('button', { name: /^Paso 5: FUR/ }));
    expect(await screen.findByRole('button', { name: /^Radicar trámite$/ })).toBeDisabled();
    expect(screen.queryByRole('button', { name: /^Finalizar$/ })).not.toBeInTheDocument();
  });
});

describe('TramiteWizard — consulta persiste antes de preflight', () => {
  it('persiste el VIN (PATCH field_values) ANTES de runPreflight, y refresca', async () => {
    const user = userEvent.setup();
    renderWizard();
    await screen.findByRole('button', { name: /^Paso 1: Consulta VIN/ });
    // 1 carga inicial del wizard.
    await waitFor(() => expect(mocks.getWizardState).toHaveBeenCalledTimes(1));

    // Captura el VIN en el input del paso consulta_vin (aria-label "Número VIN"
    // tras el rediseño del card de matrícula).
    await user.type(screen.getByLabelText('Número VIN'), '9BWZZZ377VT004251');

    // Orden de llamadas observado para verificar persist→preflight.
    const calls: string[] = [];
    mocks.patchFieldValues.mockImplementation(async () => {
      calls.push('patch');
      return { id: 'inst-1', fieldValues: [] };
    });
    mocks.runPreflight.mockImplementation(async () => {
      calls.push('preflight');
      return GREEN_PREFLIGHT;
    });

    await user.click(screen.getByRole('button', { name: /Consultar RUNT/ }));

    // PATCH con el VIN.
    await waitFor(() =>
      expect(mocks.patchFieldValues).toHaveBeenCalledWith('inst-1', [
        { formFieldId: null, fieldKey: 'vin', valueText: '9BWZZZ377VT004251', valueJson: null },
      ]),
    );
    await waitFor(() => expect(mocks.runPreflight).toHaveBeenCalledWith('inst-1'));
    // El PATCH ocurre ANTES del preflight.
    expect(calls).toEqual(['patch', 'preflight']);
    // Tras correr preflight, el wizard se re-consulta (refresh()).
    await waitFor(() => expect(mocks.getWizardState).toHaveBeenCalledTimes(2));
  });

  it('no persiste ni corre preflight si el VIN está vacío', async () => {
    const user = userEvent.setup();
    renderWizard();
    await screen.findByRole('button', { name: /^Paso 1: Consulta VIN/ });

    await user.click(screen.getByRole('button', { name: /Consultar RUNT/ }));

    // Validación: sin identificador no se persiste ni se consulta.
    expect(mocks.patchFieldValues).not.toHaveBeenCalled();
    expect(mocks.runPreflight).not.toHaveBeenCalled();
    expect(screen.getByText(/Ingresa el VIN antes de consultar/)).toBeInTheDocument();
  });

  it('traspaso persiste placa + documento del propietario antes de preflight', async () => {
    mocks.getWizardState.mockResolvedValue(TRASPASO_WIZARD);
    // Proveedor de placa = Verifik: SÍ pide y envía el tipo de documento del propietario (HU #10478).
    mocks.getConsultationConfig.mockResolvedValue({
      vehicleVin: 'kyverum_runt',
      vehiclePlate: 'verifik',
      conductor: 'kyverum_runt_conductor',
    });
    const user = userEvent.setup();
    renderWizard();
    await screen.findByRole('button', { name: /^Paso 1: Consulta/ });

    await user.type(screen.getByLabelText(/^Placa$/), 'ABC123');
    await user.type(
      screen.getByLabelText(/Número documento propietario/),
      '1020304050',
    );

    await user.click(screen.getByRole('button', { name: /Consultar RUNT/ }));

    await waitFor(() =>
      expect(mocks.patchFieldValues).toHaveBeenCalledWith('inst-1', [
        { formFieldId: null, fieldKey: 'plate', valueText: 'ABC123', valueJson: null },
        { formFieldId: null, fieldKey: 'owner_document_type', valueText: 'CC', valueJson: null },
        {
          formFieldId: null,
          fieldKey: 'owner_document_number',
          valueText: '1020304050',
          valueJson: null,
        },
      ]),
    );
    await waitFor(() => expect(mocks.runPreflight).toHaveBeenCalledWith('inst-1'));
  });
});

describe('TramiteWizard — bloqueo de duplicidad de trámite en curso (HU #10882)', () => {
  it('AC1: el preflight con 409 DUPLICATE_ACTIVE_PROCEDURE muestra el aviso y el botón Retomar', async () => {
    const user = userEvent.setup();
    mocks.runPreflight.mockRejectedValue(
      new FakeTramitesApiError(409, 'Ya existe un trámite en proceso para este VIN/placa.', {
        title: 'DUPLICATE_ACTIVE_PROCEDURE',
        status: 409,
        detail: 'Ya existe un trámite en proceso para este VIN/placa.',
        procedureInstanceId: 'inst-existente-1',
      }),
    );
    renderWizard();
    await screen.findByRole('button', { name: /^Paso 1: Consulta VIN/ });

    await user.type(screen.getByLabelText('Número VIN'), '9BWZZZ377VT004251');
    await user.click(screen.getByRole('button', { name: /Consultar RUNT/ }));

    const alert = await screen.findByRole('alert');
    expect(alert).toHaveTextContent(/Ya existe un trámite en curso para este vehículo/);
    expect(
      screen.getByRole('button', { name: /Retomar el trámite existente/ }),
    ).toBeInTheDocument();
    // El error genérico NO se muestra a la vez que el aviso específico de duplicidad.
    expect(screen.queryByText(/No se pudo consultar\./)).not.toBeInTheDocument();
  });

  it('AC2: al pulsar Retomar navega al trámite existente devuelto por el bloqueo', async () => {
    const user = userEvent.setup();
    mocks.runPreflight.mockRejectedValue(
      new FakeTramitesApiError(409, 'Ya existe un trámite en proceso para este VIN/placa.', {
        title: 'DUPLICATE_ACTIVE_PROCEDURE',
        status: 409,
        detail: 'Ya existe un trámite en proceso para este VIN/placa.',
        procedureInstanceId: 'inst-existente-1',
      }),
    );
    renderWizard();
    await screen.findByRole('button', { name: /^Paso 1: Consulta VIN/ });

    await user.type(screen.getByLabelText('Número VIN'), '9BWZZZ377VT004251');
    await user.click(screen.getByRole('button', { name: /Consultar RUNT/ }));

    const retomarButton = await screen.findByRole('button', {
      name: /Retomar el trámite existente/,
    });
    await user.click(retomarButton);

    expect(routerPush).toHaveBeenCalledWith('/tramites/inst-existente-1');
  });

  it('un 409 de otro origen (no duplicidad) muestra el error genérico, sin el botón Retomar', async () => {
    const user = userEvent.setup();
    mocks.runPreflight.mockRejectedValue(
      new FakeTramitesApiError(409, 'Solo se puede correr preflight en estado borrador.', {
        title: 'Conflict',
        status: 409,
        detail: 'Solo se puede correr preflight en estado borrador.',
      }),
    );
    renderWizard();
    await screen.findByRole('button', { name: /^Paso 1: Consulta VIN/ });

    await user.type(screen.getByLabelText('Número VIN'), '9BWZZZ377VT004251');
    await user.click(screen.getByRole('button', { name: /Consultar RUNT/ }));

    expect(
      await screen.findByText(/Solo se puede correr preflight en estado borrador\./),
    ).toBeInTheDocument();
    expect(
      screen.queryByRole('button', { name: /Retomar el trámite existente/ }),
    ).not.toBeInTheDocument();
  });
});

describe('TramiteWizard — bloqueo por estado del vehículo (HU #10884)', () => {
  it('AC1: 422 VEHICLE_STATE_INVALID_FOR_TYPE con vehicleStatus ACTIVO informa "ya matriculado" y bloquea el avance', async () => {
    const user = userEvent.setup();
    mocks.runPreflight.mockRejectedValue(
      new FakeTramitesApiError(
        422,
        'El vehículo ya se encuentra matriculado: no es válido para este tipo de trámite.',
        {
          title: 'VEHICLE_STATE_INVALID_FOR_TYPE',
          status: 422,
          detail: 'El vehículo ya se encuentra matriculado: no es válido para este tipo de trámite.',
          vehicleStatus: 'ACTIVO',
          procedureType: 'matricula_inicial',
        },
      ),
    );
    // El paso de consulta arranca 'incomplete' (sin preflight persistido aún): sin avance posible.
    mocks.getWizardState.mockResolvedValue({
      ...MATRICULA_WIZARD,
      steps: MATRICULA_WIZARD.steps.map((s) =>
        s.key === 'consulta_vin' ? { ...s, status: 'incomplete' as const, reasons: [] } : s,
      ),
    });
    renderWizard();
    await screen.findByRole('button', { name: /^Paso 1: Consulta VIN/ });

    await user.type(screen.getByLabelText('Número VIN'), '9BWZZZ377VT004251');
    await user.click(screen.getByRole('button', { name: /Consultar RUNT/ }));

    const alert = await screen.findByRole('alert');
    expect(alert).toHaveTextContent(/ya se encuentra matriculado según el RUNT/i);
    // Bloqueo no subsanable: sin botón de acción (a diferencia del aviso de duplicidad con "Retomar").
    expect(screen.queryByRole('button', { name: /Retomar/ })).not.toBeInTheDocument();
    // El error genérico NO se muestra a la vez que el aviso específico.
    expect(screen.queryByText(/No se pudo consultar\./)).not.toBeInTheDocument();
    // El preflight no se persistió (422): el paso sigue incompleto → "Continuar" deshabilitado.
    expect(screen.getByRole('button', { name: /Continuar/ })).toBeDisabled();
  });

  it('AC1 (variante FLIT): vehicleStatus APROBADO_FLIT también informa "ya matriculado"', async () => {
    const user = userEvent.setup();
    mocks.runPreflight.mockRejectedValue(
      new FakeTramitesApiError(422, 'El vehículo ya se encuentra matriculado.', {
        title: 'VEHICLE_STATE_INVALID_FOR_TYPE',
        status: 422,
        vehicleStatus: 'APROBADO_FLIT',
        procedureType: 'matricula_inicial',
      }),
    );
    renderWizard();
    await screen.findByRole('button', { name: /^Paso 1: Consulta VIN/ });

    await user.type(screen.getByLabelText('Número VIN'), '9BWZZZ377VT004251');
    await user.click(screen.getByRole('button', { name: /Consultar RUNT/ }));

    const alert = await screen.findByRole('alert');
    expect(alert).toHaveTextContent(/ya cuenta con una matrícula aprobada/i);
  });

  it('AC2: vehicleStatus DESCONOCIDO informa que no se pudo confirmar el estado (RUNT sin dato) y bloquea', async () => {
    const user = userEvent.setup();
    mocks.runPreflight.mockRejectedValue(
      new FakeTramitesApiError(
        422,
        'No fue posible confirmar el estado del vehículo en el RUNT. Vuelve a intentarlo.',
        {
          title: 'VEHICLE_STATE_INVALID_FOR_TYPE',
          status: 422,
          vehicleStatus: 'DESCONOCIDO',
          procedureType: 'matricula_inicial',
        },
      ),
    );
    mocks.getWizardState.mockResolvedValue({
      ...MATRICULA_WIZARD,
      steps: MATRICULA_WIZARD.steps.map((s) =>
        s.key === 'consulta_vin' ? { ...s, status: 'incomplete' as const, reasons: [] } : s,
      ),
    });
    renderWizard();
    await screen.findByRole('button', { name: /^Paso 1: Consulta VIN/ });

    await user.type(screen.getByLabelText('Número VIN'), '9BWZZZ377VT004251');
    await user.click(screen.getByRole('button', { name: /Consultar RUNT/ }));

    const alert = await screen.findByRole('alert');
    expect(alert).toHaveTextContent(/no fue posible confirmar el estado del vehículo en el runt/i);
    expect(screen.getByRole('button', { name: /Continuar/ })).toBeDisabled();
  });

  it('un 422 de otro código (no VEHICLE_STATE_INVALID_FOR_TYPE) muestra el error genérico', async () => {
    const user = userEvent.setup();
    mocks.runPreflight.mockRejectedValue(
      new FakeTramitesApiError(422, 'Dato inválido en el payload.', {
        title: 'OTRO_CODIGO',
        status: 422,
      }),
    );
    renderWizard();
    await screen.findByRole('button', { name: /^Paso 1: Consulta VIN/ });

    await user.type(screen.getByLabelText('Número VIN'), '9BWZZZ377VT004251');
    await user.click(screen.getByRole('button', { name: /Consultar RUNT/ }));

    expect(await screen.findByText(/Dato inválido en el payload\./)).toBeInTheDocument();
  });
});

describe('TramiteWizard — aceptar riesgo de preflight rojo', () => {
  const RED_PREFLIGHT: PreflightSnapshot = {
    overall: 'red',
    checks: [
      {
        key: 'estado_vehiculo',
        label: 'Estado del vehículo',
        status: 'fail',
        source: 'RUNT',
        message: 'Estado: registrado',
      },
    ],
    createdAt: '2026-06-18T00:00:00Z',
  };

  it('marca el checkbox y persiste riesgo_aceptado + refresca el wizard', async () => {
    // Preflight rojo en el paso 1 → aparece el checkbox "Asumo el riesgo…".
    mocks.getPreflight.mockResolvedValue(RED_PREFLIGHT);
    const user = userEvent.setup();
    renderWizard();
    await screen.findByRole('button', { name: /^Paso 1: Consulta VIN/ });

    const checkbox = await screen.findByRole('checkbox', {
      name: /Asumo el riesgo de rechazo en el organismo de tránsito/i,
    });
    expect(checkbox).not.toBeChecked();

    await user.click(checkbox);

    // Persiste el flag en field_values…
    await waitFor(() =>
      expect(mocks.patchFieldValues).toHaveBeenCalledWith('inst-1', [
        { formFieldId: null, fieldKey: 'riesgo_aceptado', valueText: 'true', valueJson: null },
      ]),
    );
    // …y refresca el estado autoritativo del wizard para desbloquear el paso 2.
    await waitFor(() => expect(mocks.getWizardState).toHaveBeenCalled());
  });

  it('refleja riesgo ya aceptado (checkbox marcado) desde field_values', async () => {
    mocks.getPreflight.mockResolvedValue(RED_PREFLIGHT);
    mocks.getInstance.mockResolvedValue({
      id: 'inst-1',
      fieldValues: [{ fieldKey: 'riesgo_aceptado', valueText: 'true', valueJson: null }],
    });
    renderWizard();
    await screen.findByRole('button', { name: /^Paso 1: Consulta VIN/ });

    const checkbox = await screen.findByRole('checkbox', {
      name: /Asumo el riesgo de rechazo en el organismo de tránsito/i,
    });
    await waitFor(() => expect(checkbox).toBeChecked());
  });
});

describe('TramiteWizard — creación única de instancia (StrictMode)', () => {
  it('crea la instancia UNA sola vez aunque el efecto se re-invoque (StrictMode)', async () => {
    renderWizardStrict();
    await screen.findByRole('button', { name: /^Paso 1: Consulta VIN/ });
    // StrictMode monta→desmonta→remonta y doble-invoca el efecto; la guardia
    // con useRef debe evitar el segundo POST /instances.
    await waitFor(() => expect(mocks.createInstance).toHaveBeenCalledTimes(1));
  });

  it('muestra error visible si la creación de la instancia falla', async () => {
    mocks.createInstance.mockRejectedValueOnce(new Error('reference_number duplicado'));
    renderWizard();
    expect(await screen.findByRole('alert')).toHaveTextContent(/reference_number duplicado/);
  });
});

describe('TramiteWizard — Guardar y continuar (pasos de actores)', () => {
  // Traspaso con consulta+documentos completos y vendedor como frontera.
  const VENDEDOR_FRONTIER: WizardState = {
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
  // Tras guardar el vendedor: vendedor complete, comprador pasa a ser la frontera.
  const VENDEDOR_DONE: WizardState = {
    ...VENDEDOR_FRONTIER,
    steps: VENDEDOR_FRONTIER.steps.map((s) =>
      s.index === 2
        ? { ...s, status: 'complete', reasons: [] as string[] }
        : s.index === 3
          ? { ...s, status: 'incomplete', reasons: ['comprador_incompleto'] }
          : s,
    ),
  };

  it('Continuar en paso vendedor dispara save (PUT actors) ANTES de refrescar y avanzar', async () => {
    const user = userEvent.setup();
    const order: string[] = [];
    let wizardCalls = 0;
    mocks.getWizardState.mockImplementation(async () => {
      wizardCalls += 1;
      order.push('wizard');
      return wizardCalls === 1 ? VENDEDOR_FRONTIER : VENDEDOR_DONE;
    });
    mocks.getActors.mockResolvedValue([
      { rol: 'vendedor', tipoDocumento: 'CC', numeroDocumento: '999', nombreCompleto: 'Pedro Vendedor', email: 'pedro@x.com' },
    ]);
    mocks.saveActors.mockImplementation(async () => {
      order.push('save');
    });

    renderWizard();
    await screen.findByRole('button', { name: /^Paso 1: Consulta/ });

    // Navega al paso Vendedor (frontera).
    await user.click(screen.getByRole('button', { name: /^Paso 3: Vendedor/ }));
    // El form hidrata al vendedor cargado y auto-consulta RUNT.
    await screen.findByDisplayValue('Pedro Vendedor');
    await screen.findByText(/Persona encontrada en RUNT/i);

    // Footer del wizard: "Guardar y continuar" (no el botón propio del form).
    await user.click(screen.getByRole('button', { name: /Guardar y continuar/ }));

    // 1) Se guardó el set de actores con el vendedor.
    await waitFor(() => expect(mocks.saveActors).toHaveBeenCalledTimes(1));
    const [instanceId, actors] = mocks.saveActors.mock.calls[0];
    expect(instanceId).toBe('inst-1');
    expect(actors[0]).toMatchObject({ rol: 'vendedor', numeroDocumento: '999' });

    // 2) El save ocurre ANTES del refresh que decide el avance.
    expect(order.indexOf('save')).toBeLessThan(order.lastIndexOf('wizard'));

    // 3) Con el vendedor ya complete, el wizard avanza al paso Comprador.
    expect(await screen.findByText(/Identificación · Comprador/)).toBeInTheDocument();
  });

  // HU #10350 — al guardar la parte, el wizard asegura su identidad (reuso vigente o auto-validación).
  async function guardarVendedor() {
    const user = userEvent.setup();
    mocks.getWizardState.mockResolvedValue(VENDEDOR_FRONTIER);
    mocks.getActors.mockResolvedValue([
      { rol: 'vendedor', tipoDocumento: 'CC', numeroDocumento: '999', nombreCompleto: 'Pedro Vendedor', email: 'pedro@x.com' },
    ]);
    renderWizard();
    await screen.findByRole('button', { name: /^Paso 1: Consulta/ });
    await user.click(screen.getByRole('button', { name: /^Paso 3: Vendedor/ }));
    await screen.findByDisplayValue('Pedro Vendedor');
    await screen.findByText(/Persona encontrada en RUNT/i);
    await user.click(screen.getByRole('button', { name: /Guardar y continuar/ }));
  }

  it('sin identidad vigente + provider mock → simula la validación automáticamente (sin clic)', async () => {
    mocks.ensureIdentity.mockResolvedValue({ outcome: 'requiere_validacion' });
    mocks.getBiometricState.mockResolvedValue({ validations: [], provider: 'mock' });

    await guardarVendedor();

    await waitFor(() => expect(mocks.ensureIdentity).toHaveBeenCalledWith('inst-1', 'vendedor'));
    await waitFor(() => expect(mocks.simulateBiometric).toHaveBeenCalledWith('inst-1', { parte: 'vendedor' }));
    expect(mocks.iniciarBiometric).not.toHaveBeenCalled();
  });

  it('sin identidad vigente + provider kyverum → inicia la validación (envía enlace) automáticamente', async () => {
    mocks.ensureIdentity.mockResolvedValue({ outcome: 'requiere_validacion' });
    mocks.getBiometricState.mockResolvedValue({ validations: [], provider: 'kyverum' });

    await guardarVendedor();

    await waitFor(() => expect(mocks.iniciarBiometric).toHaveBeenCalledWith('inst-1', { parte: 'vendedor' }));
    expect(mocks.simulateBiometric).not.toHaveBeenCalled();
  });

  it('identidad vigente reutilizada → NO dispara una nueva validación', async () => {
    mocks.ensureIdentity.mockResolvedValue({ outcome: 'reusada' });

    await guardarVendedor();

    await waitFor(() => expect(mocks.ensureIdentity).toHaveBeenCalledWith('inst-1', 'vendedor'));
    expect(mocks.simulateBiometric).not.toHaveBeenCalled();
    expect(mocks.iniciarBiometric).not.toHaveBeenCalled();
  });

  it('si el ensure de identidad falla → avisa al gestor (toast) en vez de fallar en silencio', async () => {
    // Fix #2 (auditoría QA): el error de ensureIdentity ya NO se traga silenciosamente. Se avisa al
    // gestor con un toast (y se deja traza en consola) para que no continúe creyendo que la identidad
    // quedó encaminada. No bloquea el avance.
    mocks.ensureIdentity.mockRejectedValue(new Error('network'));

    await guardarVendedor();

    await waitFor(() => expect(mocks.ensureIdentity).toHaveBeenCalledWith('inst-1', 'vendedor'));
    await waitFor(() =>
      expect(toastShow).toHaveBeenCalledWith(
        expect.stringContaining('No se pudo iniciar automáticamente la validación de identidad'),
        'error',
      ),
    );
    // El fallo corta la orquestación (no se fuerza simular/iniciar tras el error) pero no rompe el flujo.
    expect(mocks.simulateBiometric).not.toHaveBeenCalled();
    expect(mocks.iniciarBiometric).not.toHaveBeenCalled();
  });
});

describe('TramiteWizard — traspaso journey (paso 2 documentos + vendedor split)', () => {
  const traspasoSteps = (vendedorStatus: 'locked' | 'incomplete'): WizardState => ({
    modalidad: 'traspaso',
    tipologiaCodigo: 'traspaso',
    totalSteps: 6,
    canSubmit: false,
    blockers: [],
    status: 'borrador',
    allowedTransitions: ['anulado', 'preparado'],
    steps: [
      { index: 0, key: 'consulta', label: 'Consulta', status: 'complete', reasons: [] },
      {
        index: 1,
        key: 'documentos',
        label: 'Datos y Documentos del Trámite',
        status: vendedorStatus === 'locked' ? 'incomplete' : 'complete',
        reasons: vendedorStatus === 'locked' ? ['documentos_incompletos'] : [],
      },
      { index: 2, key: 'vendedor', label: 'Vendedor', status: vendedorStatus, reasons: [] },
      { index: 3, key: 'comprador', label: 'Comprador', status: 'locked', reasons: [] },
      { index: 4, key: 'comercial', label: 'Comercial', status: 'locked', reasons: [] },
      { index: 5, key: 'fur', label: 'FUR', status: 'locked', reasons: [] },
    ],
  });

  it('el paso 2 (documentos) renderiza el checklist de documentos, no el preflight', async () => {
    mocks.getWizardState.mockResolvedValue(traspasoSteps('locked'));
    const user = userEvent.setup();
    renderWizard();
    await user.click(await screen.findByRole('button', { name: /^Paso 2: Datos y Documentos/ }));

    expect(
      await screen.findByRole('region', { name: 'Documentos del trámite' }),
    ).toBeInTheDocument();
    expect(mocks.getChecklist).toHaveBeenCalled();
  });

  it('el paso vendedor usa layout split (Identificación · Vendedor + Datos de contacto)', async () => {
    mocks.getWizardState.mockResolvedValue(traspasoSteps('incomplete'));
    const user = userEvent.setup();
    renderWizard();
    await user.click(await screen.findByRole('button', { name: /^Paso 3: Vendedor/ }));

    expect(await screen.findByText(/Identificación · Vendedor/)).toBeInTheDocument();
    expect(screen.getByText('Datos de contacto')).toBeInTheDocument();
  });
});

describe('TramiteWizard — paso comercial', () => {
  it('renderiza el form comercial en traspaso', async () => {
    mocks.getWizardState.mockResolvedValue(TRASPASO_WIZARD);
    const user = userEvent.setup();
    renderWizard();
    await screen.findByRole('button', { name: /^Paso 1: Consulta/ });
    await user.click(screen.getByRole('button', { name: /^Paso 5: Comercial/ }));
    expect(
      await screen.findByRole('form', { name: 'Datos comerciales del trámite' }),
    ).toBeInTheDocument();
    expect(screen.getByLabelText(/Valor de venta/)).toBeInTheDocument();
    expect(screen.getByLabelText(/Causal/)).toBeInTheDocument();
  });

  it('embebido: no muestra el botón propio "Guardar datos comerciales"', async () => {
    mocks.getWizardState.mockResolvedValue(TRASPASO_WIZARD);
    const user = userEvent.setup();
    renderWizard();
    await screen.findByRole('button', { name: /^Paso 1: Consulta/ });
    await user.click(screen.getByRole('button', { name: /^Paso 5: Comercial/ }));
    await screen.findByRole('form', { name: 'Datos comerciales del trámite' });
    // El guardado vive en el footer del wizard, no en un botón propio del form.
    expect(
      screen.queryByRole('button', { name: /Guardar datos comerciales/ }),
    ).toBeNull();
    expect(
      screen.getByRole('button', { name: /Guardar y continuar/ }),
    ).toBeInTheDocument();
  });

  it('"Guardar y continuar" persiste los datos comerciales (PUT) vía el footer', async () => {
    mocks.getWizardState.mockResolvedValue(TRASPASO_WIZARD);
    mocks.getCommercial.mockResolvedValue({
      valorVenta: 50_000_000,
      causal: 'COMPRAVENTA',
      tasaImpuesto: null,
      derechos: null,
      metodoPago: null,
    });
    const user = userEvent.setup();
    renderWizard();
    await screen.findByRole('button', { name: /^Paso 1: Consulta/ });
    await user.click(screen.getByRole('button', { name: /^Paso 5: Comercial/ }));
    // El form hidrata el valor formateado (COP agrupado).
    await screen.findByDisplayValue('50.000.000');

    await user.click(screen.getByRole('button', { name: /Guardar y continuar/ }));

    await waitFor(() => expect(mocks.putCommercial).toHaveBeenCalledTimes(1));
    const [instanceId, data] = mocks.putCommercial.mock.calls[0];
    expect(instanceId).toBe('inst-1');
    expect(data).toMatchObject({ valorVenta: 50_000_000, causal: 'COMPRAVENTA' });
  });
});

// HU #10478 — el paso de consulta de traspaso adapta el formulario al proveedor del tenant: con
// Kyverum RUNT no pide el tipo de documento del propietario (el RUNT lo resuelve); con Verifik sí.
describe('TramiteWizard — tipo de documento del propietario según proveedor (HU #10478)', () => {
  beforeEach(() => {
    mocks.getWizardState.mockResolvedValue(TRASPASO_WIZARD);
    mocks.getInstance.mockResolvedValue({ id: 'inst-tr', status: 'borrador', fieldValues: [] });
  });

  async function abrirPasoConsulta() {
    const user = userEvent.setup();
    render(<TramiteWizard existingInstanceId="inst-tr" onExit={() => {}} />);
    const consultaTab = await screen.findByRole('button', { name: /^Paso 1: Consulta/ });
    await user.click(consultaTab);
    // Espera a que el form de placa (traspaso) se pinte.
    await screen.findByLabelText('Placa');
  }

  it('con Kyverum RUNT (default) NO pide el tipo, pero sí placa y número', async () => {
    await abrirPasoConsulta();

    expect(screen.getByLabelText('Placa')).toBeInTheDocument();
    expect(screen.getByLabelText('Número documento propietario')).toBeInTheDocument();
    await waitFor(() =>
      expect(screen.queryByLabelText('Tipo documento propietario')).not.toBeInTheDocument(),
    );
  });

  it('con Verifik SÍ pide el tipo de documento del propietario', async () => {
    mocks.getConsultationConfig.mockResolvedValue({
      vehicleVin: 'kyverum_runt',
      vehiclePlate: 'verifik',
      conductor: 'kyverum_runt_conductor',
    });

    await abrirPasoConsulta();

    expect(
      await screen.findByLabelText('Tipo documento propietario'),
    ).toBeInTheDocument();
  });

  // Regresión: aunque el tipo esté OCULTO (Kyverum), el payload SIGUE enviando owner_document_type
  // (default 'CC') — el fallback a Verifik lo exige; omitirlo devolvía "requiere documento" (unknown)
  // y enmascaraba el fallo como pre-vuelo verde.
  it('con Kyverum el payload igual incluye owner_document_type (para el fallback a Verifik)', async () => {
    const user = userEvent.setup();
    render(<TramiteWizard existingInstanceId="inst-tr" onExit={() => {}} />);
    // Instancia existente: reanuda en la frontera, así que hay que abrir el paso de consulta.
    await user.click(await screen.findByRole('button', { name: /^Paso 1: Consulta/ }));
    await screen.findByLabelText('Placa');

    await user.type(screen.getByLabelText(/^Placa$/), 'PWL160');
    await user.type(screen.getByLabelText(/Número documento propietario/), '890903938');
    await user.click(screen.getByRole('button', { name: /Consultar RUNT/ }));

    await waitFor(() =>
      expect(mocks.patchFieldValues).toHaveBeenCalledWith('inst-tr', [
        { formFieldId: null, fieldKey: 'plate', valueText: 'PWL160', valueJson: null },
        { formFieldId: null, fieldKey: 'owner_document_type', valueText: 'CC', valueJson: null },
        {
          formFieldId: null,
          fieldKey: 'owner_document_number',
          valueText: '890903938',
          valueJson: null,
        },
      ]),
    );
  });
});
