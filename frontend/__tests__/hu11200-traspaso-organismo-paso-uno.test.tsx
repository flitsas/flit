import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

import type { WizardState } from '@/lib/api/types/procedure-runtime';

/**
 * HU #11200 — en traspaso el organismo lo impone el RUNT (donde está matriculado el vehículo). Lo que
 * se adelanta al primer paso es saber si en ese organismo se puede radicar: antes el gestor lo
 * descubría al final, con el trámite entero preparado.
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

const otNoDisponible = vi.hoisted(() => ({ value: false }));

vi.mock('@/lib/api/tramites-client', () => ({
  tramitesClient: mocks,
  DEV_TENANT_ID: 'tenant-dev',
  DEV_USER_ID: 'user-dev',
  getDuplicateActiveProcedureId: () => null,
  getVehicleStateBlock: () => null,
  isTransitOfficeUnavailable: () => otNoDisponible.value,
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

const PREVIEW_OK = {
  previewToken: 'token-traspaso',
  preflight: {
    overall: 'green' as const,
    checks: [{ key: 'estado_vehiculo', label: 'Estado del vehículo', status: 'ok' as const, source: 'RUNT' }],
    createdAt: '2026-08-01T18:00:00Z',
  },
  vehicleFields: [
    { formFieldId: '', fieldKey: 'vehicle_brand', valueText: 'MAZDA', valueJson: null, source: 'consultation' },
    {
      formFieldId: '',
      fieldKey: 'transit_office_name',
      valueText: 'Secretaría de Movilidad de Medellín',
      valueJson: null,
      source: 'consultation',
    },
  ],
};

async function consultarVehiculo(user: ReturnType<typeof userEvent.setup>) {
  await user.type(await screen.findByLabelText('Placa del vehículo'), 'ABC123');
  await user.type(screen.getByLabelText('Número documento del propietario'), '1020304050');
  await user.click(screen.getByRole('button', { name: 'Consultar RUNT' }));
}

beforeEach(() => {
  vi.clearAllMocks();
  otNoDisponible.value = false;
  mocks.getWizardPreview.mockResolvedValue(WIZARD_TRASPASO);
  mocks.runPreflightPreview.mockResolvedValue(PREVIEW_OK);
  mocks.getConsultationConfig.mockResolvedValue({ vehiclePlate: 'verifik', onlyOwnVehicles: false });
  mocks.setCurrentStep.mockResolvedValue({ id: 'inst-9', currentStep: 'documentos' });
  mocks.createInstanceFromConsulta.mockResolvedValue({
    instance: {
      id: 'inst-9',
      referenceNumber: 'TRA-2026-000009',
      status: 'borrador',
      procedureTypeId: 'type-2',
      tenantId: 'tenant-1',
      createdAt: '2026-08-01T18:00:00Z',
    },
    preflight: PREVIEW_OK.preflight,
  });
});

function renderTraspaso() {
  return render(
    <TramiteWizard modalidad="traspaso" title="Traspaso" onCreated={() => {}} onExit={() => {}} />,
  );
}

describe('HU #11200 — el organismo del traspaso se valida en el primer paso', () => {
  it('AC1: con el organismo activo y habilitado, el trámite continúa con normalidad', async () => {
    const user = userEvent.setup();
    renderTraspaso();

    await consultarVehiculo(user);

    await waitFor(() => expect(mocks.runPreflightPreview).toHaveBeenCalledTimes(1));
    expect(await screen.findByText('MAZDA')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /Continuar/ })).toBeEnabled();
  });

  it('AC2/AC3: si no se puede radicar allí, se explica qué pedirle al administrador', async () => {
    const user = userEvent.setup();
    otNoDisponible.value = true;
    mocks.runPreflightPreview.mockRejectedValue(new Error('422'));
    renderTraspaso();

    await consultarVehiculo(user);

    const aviso = await screen.findByText(/No puedes radicar en este organismo de tránsito/);
    expect(aviso).toBeInTheDocument();
    expect(aviso).toHaveTextContent(/no está activo en FLIT o no está habilitado para tu compañía/);
    expect(aviso).toHaveTextContent(/Solicita al administrador que lo active y lo habilite/);
  });

  it('AC2/AC3: con el organismo bloqueado no se puede avanzar ni crear el trámite', async () => {
    const user = userEvent.setup();
    otNoDisponible.value = true;
    mocks.runPreflightPreview.mockRejectedValue(new Error('422'));
    renderTraspaso();

    await consultarVehiculo(user);
    await screen.findByText(/No puedes radicar en este organismo de tránsito/);

    expect(screen.getByRole('button', { name: /Continuar/ })).toBeDisabled();
    expect(mocks.createInstanceFromConsulta).not.toHaveBeenCalled();
  });

  it('AC5: en traspaso no se pide elegir organismo (lo impone el RUNT)', async () => {
    renderTraspaso();

    await screen.findByLabelText('Placa del vehículo');
    expect(screen.queryByLabelText('Secretaría de tránsito')).not.toBeInTheDocument();
    expect(mocks.listTransitOffices).not.toHaveBeenCalled();
  });
});
