import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

import type { WizardState } from '@/lib/api/types/procedure-runtime';

/**
 * "Guardar y continuar" que no avanza: el guardado va bien pero un gate del backend sigue sin
 * cumplirse. Antes el wizard no decía NADA (el botón parecía no responder y la razón solo aparecía
 * abreviada en el sidebar, con el código crudo humanizado: "Simit Pendiente"). Ahora se explicita
 * qué falta, con copy accionable.
 */
const mocks = vi.hoisted(() => ({
  getInstance: vi.fn(),
  getWizardState: vi.fn(),
  getActors: vi.fn(),
  saveActors: vi.fn(),
  ensureIdentity: vi.fn(),
  getBiometricState: vi.fn(),
  // HU #10875 — panel consolidado de identidad (IdentityStatusPanel), montado por el wizard siempre
  // que hay instanceId.
  getInstanceIdentityValidationAlerts: vi.fn(),
  simulateBiometric: vi.fn(),
  iniciarBiometric: vi.fn(),
  getConsultationConfig: vi.fn(),
  getPreflight: vi.fn(),
  setCurrentStep: vi.fn(),
  listTransitOffices: vi.fn(),
  runtPersonLookup: vi.fn(),
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

// Traspaso con el comprador como frontera: incompleto por `simit_pendiente` (el trámite no tiene
// snapshot de pre-vuelo, así que el gate de comparendos del comprador no se puede evaluar).
const TRASPASO_SIMIT_PENDIENTE: WizardState = {
  modalidad: 'traspaso',
  tipologiaCodigo: 'traspaso',
  totalSteps: 6,
  canSubmit: false,
  blockers: [],
  status: 'borrador',
  allowedTransitions: ['anulado'],
  steps: [
    { index: 0, key: 'consulta', label: 'Consulta del vehículo', status: 'complete', reasons: [] },
    { index: 1, key: 'documentos', label: 'Documentos', status: 'complete', reasons: [] },
    { index: 2, key: 'vendedor', label: 'Vendedor', status: 'complete', reasons: [] },
    { index: 3, key: 'comprador', label: 'Comprador', status: 'incomplete', reasons: ['simit_pendiente'] },
    { index: 4, key: 'comercial', label: 'Datos comerciales', status: 'locked', reasons: [] },
    { index: 5, key: 'fur', label: 'Generar FUR', status: 'locked', reasons: [] },
  ],
};

beforeEach(() => {
  vi.clearAllMocks();
  mocks.getInstance.mockResolvedValue({ id: 'inst-1', status: 'borrador', fieldValues: [] });
  mocks.getWizardState.mockResolvedValue(TRASPASO_SIMIT_PENDIENTE);
  mocks.getActors.mockResolvedValue([
    {
      rol: 'vendedor',
      tipoDocumento: 'CC',
      numeroDocumento: '1037669356',
      nombreCompleto: 'WILLYN LONDOÑO CALLE',
      email: 'vendedor@example.com',
      personType: 'natural',
    },
    {
      rol: 'comprador',
      tipoDocumento: 'CC',
      numeroDocumento: '1193552679',
      nombreCompleto: 'DANIEL AMADO GARCIA',
      email: 'comprador@example.com',
      personType: 'natural',
    },
  ]);
  mocks.saveActors.mockResolvedValue(undefined);
  mocks.ensureIdentity.mockResolvedValue({ outcome: 'ya_vigente' });
  mocks.getBiometricState.mockResolvedValue({ validations: [], provider: 'mock' });
  mocks.getInstanceIdentityValidationAlerts.mockResolvedValue({ alerts: [], total: 0 });
  mocks.getConsultationConfig.mockResolvedValue({ vehiclePlate: 'kyverum_runt', onlyOwnVehicles: false });
  mocks.getPreflight.mockResolvedValue(null);
  mocks.listTransitOffices.mockResolvedValue([]);
  mocks.setCurrentStep.mockResolvedValue({ id: 'inst-1', currentStep: 'comprador' });
});

describe('Wizard — avance rechazado por un gate del backend', () => {
  it('guarda pero NO avanza mientras el gate siga sin cumplirse', async () => {
    const user = userEvent.setup();
    render(<TramiteWizard existingInstanceId="inst-1" onExit={() => {}} />);

    // El wizard abre en la frontera: el paso del comprador.
    await screen.findByRole('button', { name: /^Paso 4: Comprador/ });

    await user.click(await screen.findByRole('button', { name: /Guardar y continuar/ }));

    await waitFor(() => expect(mocks.saveActors).toHaveBeenCalled());
    // Comportamiento original: se queda en el mismo paso (el motivo se lee en el sidebar).
    expect(screen.getByRole('button', { name: /^Paso 4: Comprador/ })).toHaveAttribute(
      'aria-label',
      expect.stringContaining('incomplete'),
    );
    expect(screen.queryByText(/aún no se puede continuar/i)).not.toBeInTheDocument();
  });

  it('el código crudo del gate ya no se muestra humanizado ("Simit Pendiente")', async () => {
    render(<TramiteWizard existingInstanceId="inst-1" onExit={() => {}} />);

    await screen.findByRole('button', { name: /^Paso 4: Comprador/ });

    expect(screen.queryByText('Simit Pendiente')).not.toBeInTheDocument();
    expect(
      await screen.findByText(/Falta la consulta de comparendos del comprador/i),
    ).toBeInTheDocument();
  });
});
