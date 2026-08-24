import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

import type { WizardState } from '@/lib/api/types/procedure-runtime';

/**
 * FEATURE 02 — "solo vehículos propios": con la política activa el propietario NO es un dato que el
 * gestor elija, es la compañía. El campo se precarga con su NIT y queda de SOLO LECTURA durante todo
 * el trámite. Antes se dejaba editar y el rechazo llegaba al consultar, lo que invitaba a escribir un
 * NIT ajeno para descubrir después que no se podía. La regla la sigue imponiendo el backend
 * (VehicleOwnershipGuard); esto evita el intento.
 */
const mocks = vi.hoisted(() => ({
  createInstance: vi.fn(),
  createInstanceFromConsulta: vi.fn(),
  runPreflightPreview: vi.fn(),
  getWizardPreview: vi.fn(),
  getWizardState: vi.fn(),
  getInstance: vi.fn(),
  patchFieldValues: vi.fn(),
  setCurrentStep: vi.fn(),
  runPreflight: vi.fn(),
  getPreflight: vi.fn(),
  getConsultationConfig: vi.fn(),
  listTransitOffices: vi.fn(),
}));

vi.mock('@/lib/api/tramites-client', () => ({
  tramitesClient: mocks,
  DEV_TENANT_ID: 'tenant-dev',
  DEV_USER_ID: 'user-dev',
  getDuplicateActiveProcedureId: () => null,
  getVehicleStateBlock: () => null,
  isTransitOfficeUnavailable: () => false,
}));

// El NIT del tenant sale del JWT; sin él no hay con qué precargar ni qué bloquear.
vi.mock('@/lib/api/client', async (importOriginal) => ({
  ...(await importOriginal<typeof import('@/lib/api/client')>()),
  getToken: () => 'token-de-prueba',
}));
vi.mock('@/lib/auth/jwt', () => ({
  decodeJwtPayload: () => ({ company_nit: '900123456-7' }),
}));

vi.mock('@/components/admin/Toast', () => ({
  useToast: () => ({ show: vi.fn() }),
}));

vi.mock('next/navigation', () => ({
  useRouter: () => ({ push: vi.fn(), replace: vi.fn(), prefetch: vi.fn() }),
}));

import { TramiteWizard } from '@/components/operacion/TramiteWizard';

const WIZARD_TRASPASO: WizardState = {
  modalidad: 'traspaso',
  tipologiaCodigo: 'traspaso_standard',
  totalSteps: 3,
  canSubmit: false,
  blockers: [],
  status: 'borrador',
  allowedTransitions: [],
  steps: [
    { index: 1, key: 'consulta', label: 'Consulta placa', status: 'incomplete', reasons: [] },
    { index: 2, key: 'documentos', label: 'Documentos', status: 'locked', reasons: [] },
    { index: 3, key: 'fur', label: 'FUR', status: 'locked', reasons: [] },
  ],
};

/** `verifik` como primario deja visible el selector de tipo de documento (con kyverum se oculta). */
function configConPolitica(onlyOwnVehicles: boolean) {
  return {
    vehiclePlate: 'verifik',
    onlyOwnVehicles,
    onlyOwnVehiclesByFamily: { matriculas: false, traspaso: onlyOwnVehicles, otros: false },
    blockProcedureFamily: { matriculas: false, traspaso: false, otros: false },
  };
}

function renderTraspaso() {
  return render(
    <TramiteWizard procedureTypeCode="TRASPASO_STANDARD"
        modalidad="traspaso" title="Traspaso" onCreated={() => {}} onExit={() => {}} />,
  );
}

beforeEach(() => {
  vi.clearAllMocks();
  mocks.getWizardPreview.mockResolvedValue(WIZARD_TRASPASO);
  mocks.getConsultationConfig.mockResolvedValue(configConPolitica(true));
});

describe('FEATURE 02 — el propietario queda fijo cuando la compañía solo tramita lo propio', () => {
  it('precarga el NIT de la compañía y lo deja de solo lectura', async () => {
    renderTraspaso();

    const numero = (await screen.findByLabelText(
      'Número documento propietario',
    )) as HTMLInputElement;

    // Se normaliza quitando separadores; el dígito de verificación se conserva
    // (isTenantOwnDocument lo tolera con y sin él).
    await waitFor(() => expect(numero).toHaveValue('9001234567'));
    expect(numero).toHaveAttribute('readonly');
  });

  it('no deja escribir otro documento en el campo bloqueado', async () => {
    const user = userEvent.setup();
    renderTraspaso();

    const numero = (await screen.findByLabelText(
      'Número documento propietario',
    )) as HTMLInputElement;
    await waitFor(() => expect(numero).toHaveValue('9001234567'));

    await user.type(numero, '800999888');

    // Corazón de la regla: teclear no cambia el propietario.
    expect(numero).toHaveValue('9001234567');
  });

  it('bloquea también el tipo de documento (queda en NIT)', async () => {
    renderTraspaso();

    const tipo = (await screen.findByLabelText(
      'Tipo documento propietario',
    )) as HTMLSelectElement;

    await waitFor(() => expect(tipo).toHaveValue('NIT'));
    expect(tipo).toBeDisabled();
  });

  it('explica por qué no se puede editar (el estado no se comunica solo con el fondo gris)', async () => {
    renderTraspaso();

    expect(
      await screen.findByText(/el propietario queda fijo en su\s+NIT y no se puede editar/i),
    ).toBeInTheDocument();
  });

  it('sin la política, el propietario se edita con normalidad', async () => {
    mocks.getConsultationConfig.mockResolvedValue(configConPolitica(false));
    const user = userEvent.setup();
    renderTraspaso();

    const numero = (await screen.findByLabelText(
      'Número documento propietario',
    )) as HTMLInputElement;

    expect(numero).not.toHaveAttribute('readonly');
    await user.type(numero, '1020304050');
    expect(numero).toHaveValue('1020304050');
    expect(screen.queryByText(/no se puede editar durante el trámite/i)).not.toBeInTheDocument();
  });
});
