import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

import type { WizardState } from '@/lib/api/types/procedure-runtime';

/**
 * HU #11199 — la secretaría de tránsito se elige en el PRIMER paso de la matrícula inicial.
 *
 * Antes se elegía en el último paso (el del FUR), con un modal que se auto-abría: el gestor recorría
 * el trámite entero para descubrir al final que no podía radicar donde pensaba. Ahora es requisito
 * para consultar el VIN, y el paso del FUR ya no la pide.
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
  listVehicleServiceTypes: vi.fn(),
}));

const transitOfficeUnavailable = vi.hoisted(() => ({ value: false }));

vi.mock('@/lib/api/tramites-client', () => ({
  tramitesClient: mocks,
  DEV_TENANT_ID: 'tenant-dev',
  DEV_USER_ID: 'user-dev',
  getDuplicateActiveProcedureId: () => null,
  getVehicleStateBlock: () => null,
  isTransitOfficeUnavailable: () => transitOfficeUnavailable.value,
}));

vi.mock('@/components/admin/Toast', () => ({
  useToast: () => ({ show: vi.fn() }),
}));

vi.mock('next/navigation', () => ({
  useRouter: () => ({ push: vi.fn(), replace: vi.fn(), prefetch: vi.fn() }),
}));

import { TramiteWizard } from '@/components/operacion/TramiteWizard';

const SECRETARIA_ID = 'ot-medellin';
const SECRETARIAS = [
  { id: SECRETARIA_ID, code: '05001000', name: 'Secretaría de Movilidad de Medellín', cityCode: '05001' },
  { id: 'ot-envigado', code: '05266000', name: 'Tránsito de Envigado', cityCode: '05266' },
];

const VIN_VALIDO = '9BWZZZ377VT004251';
const PLACA_VALIDA = 'ABC123';

function wizard(modalidad: 'matricula_inicial' | 'traspaso'): WizardState {
  const primerPaso = modalidad === 'matricula_inicial'
    ? { index: 1, key: 'consulta_vin', label: 'Consulta VIN', status: 'incomplete' as const, reasons: [] }
    : { index: 1, key: 'consulta', label: 'Consulta placa', status: 'incomplete' as const, reasons: [] };
  return {
    modalidad,
    tipologiaCodigo: modalidad,
    totalSteps: 3,
    canSubmit: false,
    blockers: [],
    status: 'borrador',
    allowedTransitions: [],
    steps: [
      primerPaso,
      { index: 2, key: 'documentos', label: 'Documentos', status: 'locked', reasons: [] },
      { index: 3, key: 'fur', label: 'FUR', status: 'locked', reasons: [] },
    ],
  };
}

const PREVIEW_RESULT = {
  previewToken: 'token-abc',
  preflight: {
    overall: 'green' as const,
    checks: [{ key: 'soat', label: 'SOAT', status: 'ok' as const, source: 'RUNT' }],
    createdAt: '2026-08-01T18:00:00Z',
  },
  vehicleFields: [
    { formFieldId: '', fieldKey: 'vehicle_brand', valueText: 'RENAULT', valueJson: null, source: 'consultation' },
  ],
};

function renderNuevo(modalidad: 'matricula_inicial' | 'traspaso' = 'matricula_inicial') {
  mocks.getWizardPreview.mockResolvedValue(wizard(modalidad));
  return render(
    <TramiteWizard modalidad={modalidad} title="Nuevo trámite" onCreated={() => {}} onExit={() => {}} />,
  );
}

async function elegirSecretaria(user: ReturnType<typeof userEvent.setup>) {
  // Combobox en línea: enfocar el campo despliega la lista, y la opción se elige de ella.
  await user.click(await screen.findByRole('combobox', { name: /secretaría de tránsito/i }));
  await user.click(await screen.findByRole('option', { name: /Medellín/ }));
}

beforeEach(() => {
  vi.clearAllMocks();
  transitOfficeUnavailable.value = false;
  mocks.runPreflightPreview.mockResolvedValue(PREVIEW_RESULT);
  mocks.getConsultationConfig.mockResolvedValue({ vehiclePlate: 'kyverum_runt', onlyOwnVehicles: false });
  mocks.listTransitOffices.mockResolvedValue(SECRETARIAS);
  mocks.setCurrentStep.mockResolvedValue({ id: 'inst-1', currentStep: 'documentos' });
  mocks.createInstanceFromConsulta.mockResolvedValue({
    instance: {
      id: 'inst-1',
      referenceNumber: 'MAT-2026-000001',
      status: 'borrador',
      procedureTypeId: 'type-1',
      tenantId: 'tenant-1',
      createdAt: '2026-08-01T18:00:00Z',
    },
    preflight: PREVIEW_RESULT.preflight,
  });
});

describe('HU #11199 — la secretaría se elige en el primer paso', () => {
  it('AC1: el primer paso de la matrícula ofrece elegir la secretaría', async () => {
    const user = userEvent.setup();
    renderNuevo();

    // La tarjeta de radicación aparece CON el resultado de la consulta: primero se identifica el
    // vehículo, después se elige dónde radicarlo.
    await user.type(await screen.findByLabelText('Número VIN'), VIN_VALIDO);
    await user.click(screen.getByRole('button', { name: 'Consultar RUNT' }));
    await waitFor(() => expect(mocks.runPreflightPreview).toHaveBeenCalled());

    const campo = await screen.findByRole('combobox', { name: /secretaría de tránsito/i });
    await user.click(campo);
    expect(await screen.findByRole('option', { name: /Medellín/ })).toBeInTheDocument();
    expect(screen.getByRole('option', { name: /Envigado/ })).toBeInTheDocument();
  });

  it('la consulta corre sin secretaría: el organismo se elige después', async () => {
    const user = userEvent.setup();
    renderNuevo();

    // El organismo ya no gatea la consulta: se pregunta dónde radicar sobre un vehículo YA
    // identificado, no antes de saber de cuál se habla.
    await user.type(await screen.findByLabelText('Número VIN'), VIN_VALIDO);

    const boton = screen.getByRole('button', { name: 'Consultar RUNT' });
    expect(boton).toBeEnabled();
    await user.click(boton);

    await waitFor(() => expect(mocks.runPreflightPreview).toHaveBeenCalledTimes(1));
    // La tarjeta de radicación aparece con el resultado, no antes.
    await elegirSecretaria(user);
  });

  it('AC3: se advierte que solo hay organismos activos y qué hacer si falta el buscado', async () => {
    const user = userEvent.setup();
    renderNuevo();

    await user.type(await screen.findByLabelText('Número VIN'), VIN_VALIDO);
    await user.click(screen.getByRole('button', { name: 'Consultar RUNT' }));
    await waitFor(() => expect(mocks.runPreflightPreview).toHaveBeenCalled());

    expect(
      await screen.findByText(/Solo se muestran los organismos de tránsito activos en FLIT/),
    ).toBeInTheDocument();
    expect(
      screen.getByText(/solicita al administrador que lo agregue y lo active/),
    ).toBeInTheDocument();
  });

  it('AC3: si la secretaría dejó de estar habilitada, se explica qué pedirle al administrador', async () => {
    const user = userEvent.setup();
    transitOfficeUnavailable.value = true;
    mocks.runPreflightPreview.mockRejectedValue(new Error('422'));
    renderNuevo();

    await user.type(await screen.findByLabelText('Número VIN'), VIN_VALIDO);
    await user.click(screen.getByRole('button', { name: 'Consultar RUNT' }));

    expect(await screen.findByText(/No puedes radicar en este organismo de tránsito/)).toBeInTheDocument();
    // No se ofrece continuar: el trámite no puede seguir hasta que el administrador lo habilite.
    expect(screen.getByRole('button', { name: /Continuar/ })).toBeDisabled();
  });

  it('la secretaría elegida viaja a la creación del trámite', async () => {
    const user = userEvent.setup();
    renderNuevo();

    await user.type(await screen.findByLabelText('Número VIN'), VIN_VALIDO);
    await user.click(screen.getByRole('button', { name: 'Consultar RUNT' }));
    await waitFor(() => expect(mocks.runPreflightPreview).toHaveBeenCalled());
    await elegirSecretaria(user);

    await user.click(screen.getByRole('button', { name: /Continuar/ }));

    await waitFor(() =>
      expect(mocks.createInstanceFromConsulta).toHaveBeenCalledWith(
        expect.objectContaining({ vin: VIN_VALIDO, transitOfficeId: SECRETARIA_ID }),
      ),
    );
  });

  it('cambiar de secretaría NO invalida la consulta ya hecha', async () => {
    const user = userEvent.setup();
    renderNuevo();

    await user.type(await screen.findByLabelText('Número VIN'), VIN_VALIDO);
    await user.click(screen.getByRole('button', { name: 'Consultar RUNT' }));
    await waitFor(() => expect(mocks.runPreflightPreview).toHaveBeenCalledTimes(1));
    await elegirSecretaria(user);
    expect(screen.getByRole('button', { name: /Continuar/ })).toBeEnabled();

    // La consulta ya no se corre contra el organismo, así que corregirlo no la invalida: borrarla
    // obligaría a repetir el RUNT por cambiar un dato que no intervino en él.
    await user.click(screen.getByRole('combobox', { name: /secretaría de tránsito/i }));
    await user.click(await screen.findByRole('option', { name: /Envigado/ }));

    expect(screen.getByRole('button', { name: /Continuar/ })).toBeEnabled();
    expect(mocks.runPreflightPreview).toHaveBeenCalledTimes(1);
  });

  it('AC5: en traspaso no se pide la secretaría y el flujo es el actual', async () => {
    const user = userEvent.setup();
    renderNuevo('traspaso');

    await screen.findByLabelText('Placa');
    expect(screen.queryByLabelText('Secretaría de tránsito')).not.toBeInTheDocument();
    expect(mocks.listTransitOffices).not.toHaveBeenCalled();

    await user.type(screen.getByLabelText('Placa'), PLACA_VALIDA);
    await user.type(screen.getByLabelText('Número documento propietario'), '1020304050');
    const boton = screen.getByRole('button', { name: 'Consultar RUNT' });
    expect(boton).toBeEnabled();
  });
});
