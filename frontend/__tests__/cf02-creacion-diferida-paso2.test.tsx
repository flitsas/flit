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
  listTransitOffices: vi.fn(),
  listVehicleServiceTypes: vi.fn(),
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

vi.mock('@/components/admin/Toast', () => ({
  useToast: () => ({ show: vi.fn() }),
}));

// HU #11628 — sin mock, la consulta real de preasignación de placa falla en jsdom y cae al
// fallback `enabled: true`, lo que exigiría declarar el dígito de preferencia en cada test de este
// archivo (que no versa sobre esa HU). Se mockea en `false`: mismo comportamiento de siempre, el
// selector queda deshabilitado y "Continuar" no depende de él.
vi.mock('@/lib/api/admin-plate-ranges', () => ({
  getPlatePreassignStatus: vi.fn().mockResolvedValue({ enabled: false }),
}));

const routerReplace = vi.hoisted(() => vi.fn());
vi.mock('next/navigation', () => ({
  useRouter: () => ({ push: vi.fn(), replace: routerReplace, prefetch: vi.fn() }),
}));

import { TramiteWizard } from '@/components/operacion/TramiteWizard';

// Esqueleto que devuelve GET /wizard-preview: paso 1 abierto, el resto bloqueado.
const PREVIEW_MATRICULA: WizardState = {
  // ADR-0050 — el estado trae la FAMILIA en `modalidad` y el `code` canónico en `tipologiaCodigo`.
  modalidad: 'MATRICULAS',
  tipologiaCodigo: 'MATRICULA_NUEVA',
  totalSteps: 5,
  canSubmit: false,
  blockers: [],
  status: 'borrador',
  allowedTransitions: [],
  steps: [
    { index: 1, key: 'consulta_vin', label: 'Consulta VIN', status: 'incomplete', reasons: [] },
    { index: 2, key: 'documentos', label: 'Datos y Documentos del Trámite', status: 'locked', reasons: [] },
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
    { formFieldId: '', fieldKey: 'vehicle_class', valueText: 'CAMION', valueJson: null, source: 'consultation' },
    { formFieldId: '', fieldKey: 'vehicle_body_type', valueText: 'ESTACAS', valueJson: null, source: 'consultation' },
    { formFieldId: '', fieldKey: 'vehicle_body_type_runt', valueText: 'ESTACAS', valueJson: null, source: 'consultation' },
    { formFieldId: '', fieldKey: 'vehicle_color', valueText: 'BLANCO', valueJson: null, source: 'consultation' },
    { formFieldId: '', fieldKey: 'vehicle_fuel', valueText: 'DIESEL', valueJson: null, source: 'consultation' },
  ],
};

const VIN_VALIDO = '9BWZZZ377VT004251';

/** HU #11199 — la matrícula inicial ahora exige elegir la secretaría antes de consultar el VIN. */
const SECRETARIA_ID = 'ot-medellin';
const SECRETARIAS = [
  { id: SECRETARIA_ID, code: '05001000', name: 'Secretaría de Movilidad de Medellín', cityCode: '05001' },
];

/** Deja el paso 1 listo para consultar: VIN escrito. */
async function prepararConsulta(user: ReturnType<typeof userEvent.setup>) {
  await user.type(await screen.findByLabelText(/Ingrese el VIN o la placa/i), VIN_VALIDO);
}

/**
 * Elige el organismo de radicación. Va DESPUÉS de consultar: la tarjeta "Organismo de Tránsito y
 * Radicación" solo aparece con el vehículo ya identificado, y el organismo se exige al crear el
 * trámite, no al consultarlo.
 */
async function elegirSecretaria(user: ReturnType<typeof userEvent.setup>) {
  await user.click(await screen.findByRole('combobox', { name: /secretaría de tránsito/i }));
  await user.click(await screen.findByRole('option', { name: /Medellín/ }));
}

function renderNuevaMatricula() {
  return render(
    <TramiteWizard
      procedureTypeCode="MATRICULA_NUEVA"
        family="MATRICULAS"
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
  mocks.listTransitOffices.mockResolvedValue(SECRETARIAS);
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

    await prepararConsulta(user);
    await user.click(screen.getByRole('button', { name: 'Consultar RUNT' }));

    await waitFor(() => expect(mocks.runPreflightPreview).toHaveBeenCalledTimes(1));
    expect(mocks.runPreflightPreview).toHaveBeenCalledWith(
      expect.objectContaining({
        modalidad: 'MATRICULAS',
        procedureTypeCode: 'MATRICULA_NUEVA',
        vin: VIN_VALIDO,
      }),
    );
    expect(mocks.createInstanceFromConsulta).not.toHaveBeenCalled();
    // El resultado de la consulta se pinta igual que con trámite creado.
    expect(await screen.findByText('RENAULT')).toBeInTheDocument();
  });

  // Sin instancia no hay gates de backend que evaluar, así que el bloqueo tiene que vivir también
  // aquí. Antes bastaba con que la consulta respondiera —aunque fuera con el vehículo inexistente—
  // para habilitar "Continuar": se creaba el trámite y quedaba atascado en el paso 1.
  it('bloquea "Continuar" si el vehículo no se encontró en el RUNT (no subsanable)', async () => {
    mocks.runPreflightPreview.mockResolvedValue({
      ...PREVIEW_RESULT,
      preflight: {
        overall: 'red' as const,
        checks: [
          {
            key: 'vehiculo',
            label: 'Vehículo RUNT',
            status: 'fail' as const,
            source: 'verifik',
            message: 'Vehículo no encontrado en RUNT',
          },
        ],
        createdAt: '2026-08-06T00:00:00Z',
      },
    });
    const user = userEvent.setup();
    renderNuevaMatricula();

    await prepararConsulta(user);
    await user.click(screen.getByRole('button', { name: 'Consultar RUNT' }));
    await waitFor(() => expect(mocks.runPreflightPreview).toHaveBeenCalled());
    await elegirSecretaria(user);

    expect(screen.getByRole('button', { name: /Continuar/ })).toBeDisabled();
    // Es bloqueo DURO: no se ofrece el escape de "asumo el riesgo".
    expect(screen.queryByText(/Asumo el riesgo/)).not.toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: /Continuar/ }));
    expect(mocks.createInstanceFromConsulta).not.toHaveBeenCalled();
  });

  // Rojo subsanable (SOAT/RTM): sí se puede avanzar, pero solo aceptando el riesgo.
  it('con rojo subsanable exige aceptar el riesgo antes de habilitar "Continuar"', async () => {
    mocks.runPreflightPreview.mockResolvedValue({
      ...PREVIEW_RESULT,
      preflight: {
        overall: 'red' as const,
        checks: [
          {
            key: 'soat',
            label: 'SOAT',
            status: 'fail' as const,
            source: 'verifik',
            message: 'SOAT vencido',
          },
        ],
        createdAt: '2026-08-06T00:00:00Z',
      },
    });
    const user = userEvent.setup();
    renderNuevaMatricula();

    await prepararConsulta(user);
    await user.click(screen.getByRole('button', { name: 'Consultar RUNT' }));
    await waitFor(() => expect(mocks.runPreflightPreview).toHaveBeenCalled());
    await elegirSecretaria(user);

    expect(screen.getByRole('button', { name: /Continuar/ })).toBeDisabled();

    await user.click(
      await screen.findByRole('checkbox', {
        name: /Asumo el riesgo de rechazo en el organismo de tránsito/i,
      }),
    );

    await waitFor(() =>
      expect(screen.getByRole('button', { name: /Continuar/ })).toBeEnabled(),
    );
  });

  it('AC4: "Continuar" crea el trámite reusando la consulta y navega a su ruta', async () => {
    const user = userEvent.setup();
    renderNuevaMatricula();

    await prepararConsulta(user);
    await user.click(screen.getByRole('button', { name: 'Consultar RUNT' }));
    await waitFor(() => expect(mocks.runPreflightPreview).toHaveBeenCalled());
    await elegirSecretaria(user);

    await user.click(screen.getByRole('button', { name: /Continuar/ }));

    await waitFor(() => expect(mocks.createInstanceFromConsulta).toHaveBeenCalledTimes(1));
    expect(mocks.createInstanceFromConsulta).toHaveBeenCalledWith(
      expect.objectContaining({
        modalidad: 'MATRICULAS',
        // ADR-0050 — el `code` es lo que decide QUÉ trámite se crea; la familia solo el bloqueo.
        procedureTypeCode: 'MATRICULA_NUEVA',
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

  // Lo que se marca en el paso 1 (paz y salvo, aceptación de riesgo) no se pierde por no existir
  // todavía el trámite: se anota en memoria y viaja con la creación al continuar. Las
  // transformaciones y el tipo de servicio ya NO se declaran aquí — viven en el paso de requisitos,
  // contra el trámite ya creado (ver DeclaracionesTramite).
  it('lo marcado en el paso 1 se persiste al crear el trámite', async () => {
    mocks.runPreflightPreview.mockResolvedValue({
      ...PREVIEW_RESULT,
      preflight: {
        overall: 'red' as const,
        checks: [
          { key: 'soat', label: 'SOAT', status: 'fail' as const, source: 'verifik', message: 'SOAT vencido' },
        ],
        createdAt: '2026-08-06T00:00:00Z',
      },
    });
    const user = userEvent.setup();
    renderNuevaMatricula();

    await prepararConsulta(user);
    await user.click(screen.getByRole('button', { name: 'Consultar RUNT' }));
    await waitFor(() => expect(mocks.runPreflightPreview).toHaveBeenCalled());
    await elegirSecretaria(user);

    await user.click(
      await screen.findByRole('checkbox', {
        name: /Asumo el riesgo de rechazo en el organismo de tránsito/i,
      }),
    );
    // Sin trámite todavía: no se persiste nada en este momento.
    expect(mocks.patchFieldValues).not.toHaveBeenCalled();

    await user.click(screen.getByRole('button', { name: /Continuar/ }));

    await waitFor(() => expect(mocks.createInstanceFromConsulta).toHaveBeenCalled());
    await waitFor(() =>
      expect(mocks.patchFieldValues).toHaveBeenCalledWith(
        'inst-1',
        expect.arrayContaining([
          expect.objectContaining({ fieldKey: 'riesgo_aceptado', valueText: 'true' }),
        ]),
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

    await prepararConsulta(user);
    await user.click(screen.getByRole('button', { name: 'Consultar RUNT' }));
    await waitFor(() => expect(mocks.runPreflightPreview).toHaveBeenCalled());
    await elegirSecretaria(user);
    await waitFor(() =>
      expect(screen.getByRole('button', { name: /Continuar/ })).not.toBeDisabled(),
    );

    await user.type(screen.getByLabelText(/Ingrese el VIN o la placa/i), '9');

    await waitFor(() => expect(screen.getByRole('button', { name: /Continuar/ })).toBeDisabled());
    expect(mocks.createInstanceFromConsulta).not.toHaveBeenCalled();
  });

  it('AC4: si la creación falla, se avisa y no se navega (no queda trámite a medias)', async () => {
    const user = userEvent.setup();
    mocks.createInstanceFromConsulta.mockRejectedValue(new Error('409 Ya existe un trámite en proceso'));
    renderNuevaMatricula();

    await prepararConsulta(user);
    await user.click(screen.getByRole('button', { name: 'Consultar RUNT' }));
    await waitFor(() => expect(mocks.runPreflightPreview).toHaveBeenCalled());
    await elegirSecretaria(user);
    await user.click(screen.getByRole('button', { name: /Continuar/ }));

    expect(await screen.findByRole('alert')).toHaveTextContent('Ya existe un trámite en proceso');
    expect(routerReplace).not.toHaveBeenCalled();
  });
});
