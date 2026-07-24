import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

import type { WizardState } from '@/lib/api/types/procedure-runtime';

/**
 * CF-02 (HU #10879 AC3/AC5 · HU #10883 AC3/AC4) — el trámite NO se crea al entrar al wizard.
 *
 * AC3 — En el paso 1 (consulta del vehículo) no existe registro: la consulta va contra el preview
 * desacoplado y NO se llama a ningún endpoint de creación.
 * AC4 — Al pulsar "Continuar" (paso 1 → paso 2) se crea el trámite en borrador reusando esa consulta
 * (`previewToken`) y se navega a la ruta del trámite recién creado.
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

const routerReplace = vi.hoisted(() => vi.fn());
vi.mock('next/navigation', () => ({
  useRouter: () => ({ push: vi.fn(), replace: routerReplace, prefetch: vi.fn() }),
}));

import { TramiteWizard } from '@/components/operacion/TramiteWizard';

// Esqueleto que devuelve GET /wizard-preview: paso 1 abierto, el resto bloqueado.
const PREVIEW_MATRICULA: WizardState = {
  modalidad: 'matricula_inicial',
  tipologiaCodigo: 'matricula_inicial',
  totalSteps: 5,
  canSubmit: false,
  blockers: [],
  status: 'borrador',
  allowedTransitions: [],
  steps: [
    { index: 1, key: 'consulta_vin', label: 'Consulta VIN', status: 'incomplete', reasons: [] },
    { index: 2, key: 'documentos', label: 'Documentos', status: 'locked', reasons: [] },
    { index: 3, key: 'comprador', label: 'Comprador', status: 'locked', reasons: [] },
    { index: 4, key: 'identidad', label: 'Identidad', status: 'locked', reasons: [] },
    { index: 5, key: 'fur', label: 'FUR', status: 'locked', reasons: [] },
  ],
};

const PREVIEW_RESULT = {
  previewToken: 'token-abc',
  preflight: {
    overall: 'green' as const,
    checks: [{ key: 'soat', label: 'SOAT', status: 'ok' as const, source: 'RUNT' }],
    createdAt: '2026-07-23T18:00:00Z',
  },
  vehicleFields: [
    { formFieldId: '', fieldKey: 'vehicle_brand', valueText: 'RENAULT', valueJson: null, source: 'consultation' },
  ],
};

const VIN_VALIDO = '9BWZZZ377VT004251';

function renderNuevaMatricula() {
  return render(
    <TramiteWizard
      modalidad="matricula_inicial"
      title="Matrícula inicial"
      onCreated={(summary) => routerReplace(`/tramites/${summary.id}?t=${summary.tenantId}`)}
      onExit={() => {}}
    />,
  );
}

beforeEach(() => {
  vi.clearAllMocks();
  mocks.getWizardPreview.mockResolvedValue(PREVIEW_MATRICULA);
  mocks.runPreflightPreview.mockResolvedValue(PREVIEW_RESULT);
  mocks.getConsultationConfig.mockResolvedValue({ vehiclePlate: 'kyverum_runt', onlyOwnVehicles: false });
  mocks.setCurrentStep.mockResolvedValue({ id: 'inst-1', currentStep: 'documentos' });
  mocks.patchFieldValues.mockResolvedValue({ id: 'inst-1', fieldValues: [] });
  mocks.createInstanceFromConsulta.mockResolvedValue({
    instance: {
      id: 'inst-1',
      referenceNumber: 'MAT-2026-000001',
      status: 'borrador',
      procedureTypeId: 'type-1',
      tenantId: 'tenant-1',
      createdAt: '2026-07-23T18:00:00Z',
    },
    preflight: PREVIEW_RESULT.preflight,
  });
});

describe('CF-02 — el trámite se crea al pasar al segundo paso', () => {
  it('AC3: entrar al paso 1 no crea ningún trámite', async () => {
    renderNuevaMatricula();

    expect(await screen.findByRole('button', { name: 'Consultar RUNT' })).toBeInTheDocument();
    expect(mocks.createInstance).not.toHaveBeenCalled();
    expect(mocks.createInstanceFromConsulta).not.toHaveBeenCalled();
    // Sin instancia no se toca ningún endpoint per-instance.
    expect(mocks.patchFieldValues).not.toHaveBeenCalled();
    expect(mocks.runPreflight).not.toHaveBeenCalled();
    expect(mocks.getWizardState).not.toHaveBeenCalled();
  });

  it('AC3: la consulta del vehículo corre desacoplada, sin crear el trámite', async () => {
    const user = userEvent.setup();
    renderNuevaMatricula();

    await user.type(await screen.findByLabelText('Número VIN'), VIN_VALIDO);
    await user.click(screen.getByRole('button', { name: 'Consultar RUNT' }));

    await waitFor(() => expect(mocks.runPreflightPreview).toHaveBeenCalledTimes(1));
    expect(mocks.runPreflightPreview).toHaveBeenCalledWith(
      expect.objectContaining({ modalidad: 'matricula_inicial', vin: VIN_VALIDO }),
    );
    expect(mocks.createInstanceFromConsulta).not.toHaveBeenCalled();
    // El resultado de la consulta se pinta igual que con trámite creado.
    expect(await screen.findByText('RENAULT')).toBeInTheDocument();
  });

  it('AC4: "Continuar" crea el trámite reusando la consulta y navega a su ruta', async () => {
    const user = userEvent.setup();
    renderNuevaMatricula();

    await user.type(await screen.findByLabelText('Número VIN'), VIN_VALIDO);
    await user.click(screen.getByRole('button', { name: 'Consultar RUNT' }));
    await waitFor(() => expect(mocks.runPreflightPreview).toHaveBeenCalled());

    await user.click(screen.getByRole('button', { name: /Continuar/ }));

    await waitFor(() => expect(mocks.createInstanceFromConsulta).toHaveBeenCalledTimes(1));
    expect(mocks.createInstanceFromConsulta).toHaveBeenCalledWith(
      expect.objectContaining({
        modalidad: 'matricula_inicial',
        vin: VIN_VALIDO,
        previewToken: 'token-abc',
      }),
    );
    // AC1 (#10883) — el punto de retoma queda en el segundo paso.
    await waitFor(() =>
      expect(mocks.setCurrentStep).toHaveBeenCalledWith('inst-1', 'documentos', 'tenant-1'),
    );
    expect(routerReplace).toHaveBeenCalledWith('/tramites/inst-1?t=tenant-1');
  });

  // El paso 1 conserva TODOS los controles del flujo anterior (condiciones del trámite,
  // transformaciones, paz y salvo, riesgo). Lo único que cambia es cuándo se guardan: en vez de un
  // PATCH inmediato, viajan con la creación al continuar al paso 2.
  it('las condiciones marcadas en el paso 1 se persisten al crear el trámite', async () => {
    const user = userEvent.setup();
    renderNuevaMatricula();

    await user.type(await screen.findByLabelText('Número VIN'), VIN_VALIDO);
    await user.click(screen.getByRole('button', { name: 'Consultar RUNT' }));
    await waitFor(() => expect(mocks.runPreflightPreview).toHaveBeenCalled());

    await user.click(screen.getByRole('checkbox', { name: /Cambio de carrocería/ }));
    // Sin trámite todavía: no se persiste nada en este momento.
    expect(mocks.patchFieldValues).not.toHaveBeenCalled();

    await user.click(screen.getByRole('button', { name: /Continuar/ }));

    await waitFor(() => expect(mocks.createInstanceFromConsulta).toHaveBeenCalled());
    await waitFor(() =>
      expect(mocks.patchFieldValues).toHaveBeenCalledWith(
        'inst-1',
        [expect.objectContaining({ fieldKey: 'cambio_carroceria', valueText: 'true' })],
        'tenant-1',
      ),
    );
  });

  it('AC4: sin consulta previa no se puede continuar (no hay nada que crear)', async () => {
    renderNuevaMatricula();

    const continuar = await screen.findByRole('button', { name: /Continuar/ });
    expect(continuar).toBeDisabled();
    expect(mocks.createInstanceFromConsulta).not.toHaveBeenCalled();
  });

  it('AC4: editar el VIN tras consultar invalida la consulta y vuelve a bloquear Continuar', async () => {
    const user = userEvent.setup();
    renderNuevaMatricula();

    await user.type(await screen.findByLabelText('Número VIN'), VIN_VALIDO);
    await user.click(screen.getByRole('button', { name: 'Consultar RUNT' }));
    await waitFor(() => expect(mocks.runPreflightPreview).toHaveBeenCalled());
    await waitFor(() =>
      expect(screen.getByRole('button', { name: /Continuar/ })).not.toBeDisabled(),
    );

    await user.type(screen.getByLabelText('Número VIN'), '9');

    await waitFor(() => expect(screen.getByRole('button', { name: /Continuar/ })).toBeDisabled());
    expect(mocks.createInstanceFromConsulta).not.toHaveBeenCalled();
  });

  it('AC4: si la creación falla, se avisa y no se navega (no queda trámite a medias)', async () => {
    const user = userEvent.setup();
    mocks.createInstanceFromConsulta.mockRejectedValue(new Error('409 Ya existe un trámite en proceso'));
    renderNuevaMatricula();

    await user.type(await screen.findByLabelText('Número VIN'), VIN_VALIDO);
    await user.click(screen.getByRole('button', { name: 'Consultar RUNT' }));
    await waitFor(() => expect(mocks.runPreflightPreview).toHaveBeenCalled());
    await user.click(screen.getByRole('button', { name: /Continuar/ }));

    expect(await screen.findByRole('alert')).toHaveTextContent('Ya existe un trámite en proceso');
    expect(routerReplace).not.toHaveBeenCalled();
  });
});
