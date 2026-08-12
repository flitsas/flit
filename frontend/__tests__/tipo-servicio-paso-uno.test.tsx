import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

import type { WizardState } from '@/lib/api/types/procedure-runtime';

/**
 * Captura del TIPO DE SERVICIO en el paso 1 del wizard de trámites — solo matrícula inicial, justo
 * debajo de la tarjeta de Transformaciones. Seis valores del catálogo `vehicle-service-types`; con
 * PUBLICO se exige además NIT + consulta RUES (razón social de solo lectura). "Continuar" queda
 * deshabilitado hasta que el tipo esté elegido (y, en PUBLICO, hasta que la razón social llegue).
 * En traspaso no aparece nada (cero cambios en esa modalidad).
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
  ruesPreview: vi.fn(),
  // Directorio de la compañía: se consulta ANTES que el RUES (misma escalera que el actor jurídico).
  lookupLegalRepresentativeByNit: vi.fn(),
}));

vi.mock('@/lib/api/tramites-client', () => ({
  tramitesClient: mocks,
  DEV_TENANT_ID: 'tenant-dev',
  DEV_USER_ID: 'user-dev',
  getDuplicateActiveProcedureId: () => null,
  getVehicleStateBlock: () => null,
  isTransitOfficeUnavailable: () => false,
  // Mismo duck-typing que la implementación real: `err.status === 503`.
  isRuesPreviewUnavailable: (err: unknown) =>
    !!err && typeof err === 'object' && (err as { status?: unknown }).status === 503,
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
];

const TIPOS_SERVICIO = [
  { id: 'ts-1', code: 'PARTICULAR', name: 'Particular', sortOrder: 1 },
  { id: 'ts-2', code: 'PUBLICO', name: 'Público', sortOrder: 2 },
  { id: 'ts-3', code: 'DIPLOMATICO', name: 'Diplomático', sortOrder: 3 },
  { id: 'ts-4', code: 'OFICIAL', name: 'Oficial', sortOrder: 4 },
  { id: 'ts-5', code: 'ESPECIAL', name: 'Especial', sortOrder: 5 },
  { id: 'ts-6', code: 'OTROS', name: 'Otros', sortOrder: 6 },
];

const VIN_VALIDO = '9BWZZZ377VT004251';
const PLACA_VALIDA = 'ABC123';
const NIT_EMPRESA = '900123456';

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
    createdAt: '2026-08-11T18:00:00Z',
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
  await user.click(
    await screen.findByRole('button', { name: /Seleccionar secretaría de tránsito/i }),
  );
  await user.click(await screen.findByRole('button', { name: /Medellín/ }));
}

/** Deja el paso 1 de matrícula con la consulta RUNT ya resuelta en verde (secretaría + VIN). */
async function consultarVehiculo(user: ReturnType<typeof userEvent.setup>) {
  await elegirSecretaria(user);
  await user.type(await screen.findByLabelText('Número VIN'), VIN_VALIDO);
  await user.click(screen.getByRole('button', { name: 'Consultar RUNT' }));
  await waitFor(() => expect(mocks.runPreflightPreview).toHaveBeenCalled());
}

beforeEach(() => {
  vi.clearAllMocks();
  mocks.runPreflightPreview.mockResolvedValue(PREVIEW_RESULT);
  mocks.getConsultationConfig.mockResolvedValue({ vehiclePlate: 'kyverum_runt', onlyOwnVehicles: false });
  mocks.listTransitOffices.mockResolvedValue(SECRETARIAS);
  mocks.listVehicleServiceTypes.mockResolvedValue(TIPOS_SERVICIO);
  // Sin coincidencia en el directorio: el flujo cae al RUES, que es lo que cubre la mayoría de estos casos.
  mocks.lookupLegalRepresentativeByNit.mockResolvedValue(null);
  mocks.setCurrentStep.mockResolvedValue({ id: 'inst-1', currentStep: 'documentos' });
  mocks.createInstanceFromConsulta.mockResolvedValue({
    instance: {
      id: 'inst-1',
      referenceNumber: 'MAT-2026-000001',
      status: 'borrador',
      procedureTypeId: 'type-1',
      tenantId: 'tenant-1',
      createdAt: '2026-08-11T18:00:00Z',
    },
    preflight: PREVIEW_RESULT.preflight,
  });
});

describe('Tipo de servicio — paso 1 (solo matrícula inicial)', () => {
  it('NO aparece en traspaso (cero cambios en esa modalidad)', async () => {
    renderNuevo('traspaso');

    await screen.findByLabelText('Placa');
    expect(screen.queryByLabelText('Tipo de servicio')).not.toBeInTheDocument();
    expect(mocks.listVehicleServiceTypes).not.toHaveBeenCalled();

    // El resto del flujo de traspaso sigue intacto: placa + propietario habilitan la consulta.
    await userEvent.setup().type(screen.getByLabelText('Placa'), PLACA_VALIDA);
    await userEvent.setup().type(screen.getByLabelText('Número documento propietario'), '1020304050');
    expect(screen.getByRole('button', { name: 'Consultar RUNT' })).toBeEnabled();
  });

  it('en matrícula inicial NO aparece antes de consultar el vehículo', async () => {
    renderNuevo('matricula_inicial');

    // Antes de la consulta no hay vehículo que clasificar: la tarjeta no debe robarle sitio a lo
    // que sí es la tarea del paso. Aparece junto a los datos del vehículo y las transformaciones.
    await screen.findByLabelText('Número VIN');
    expect(screen.queryByLabelText('Tipo de servicio')).not.toBeInTheDocument();
  });

  it('aparece tras consultar el vehículo, con las seis opciones del catálogo', async () => {
    const user = userEvent.setup();
    renderNuevo('matricula_inicial');

    await consultarVehiculo(user);

    const select = (await screen.findByLabelText('Tipo de servicio')) as HTMLSelectElement;
    await waitFor(() => expect(mocks.listVehicleServiceTypes).toHaveBeenCalledTimes(1));
    for (const tipo of TIPOS_SERVICIO) {
      expect(
        Array.from(select.options).some((o) => o.value === tipo.code && o.text === tipo.name),
      ).toBe(true);
    }
  });

  it('gatea "Continuar" sin tipo elegido, aunque la consulta RUNT ya haya salido verde', async () => {
    const user = userEvent.setup();
    renderNuevo();

    await consultarVehiculo(user);

    expect(screen.getByRole('button', { name: /Continuar/ })).toBeDisabled();
    expect(mocks.createInstanceFromConsulta).not.toHaveBeenCalled();
  });

  it('con un tipo distinto de PUBLICO, elegirlo basta para habilitar "Continuar"', async () => {
    const user = userEvent.setup();
    renderNuevo();

    await consultarVehiculo(user);
    await user.selectOptions(screen.getByLabelText('Tipo de servicio'), 'PARTICULAR');

    await waitFor(() => expect(screen.getByRole('button', { name: /Continuar/ })).toBeEnabled());
    expect(screen.queryByLabelText('NIT empresa vinculadora')).not.toBeInTheDocument();
  });

  it('gatea "Continuar" con PUBLICO hasta que la consulta RUES devuelva la razón social', async () => {
    const user = userEvent.setup();
    renderNuevo();

    await consultarVehiculo(user);
    await user.selectOptions(screen.getByLabelText('Tipo de servicio'), 'PUBLICO');

    expect(await screen.findByLabelText('NIT empresa vinculadora')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /Continuar/ })).toBeDisabled();

    await user.type(screen.getByLabelText('NIT empresa vinculadora'), NIT_EMPRESA);
    // Sin consultar el RUES todavía: sigue deshabilitado.
    expect(screen.getByRole('button', { name: /Continuar/ })).toBeDisabled();
  });

  /**
   * Misma escalera que el actor jurídico (HU #10906): si la empresa ya está en el directorio del
   * tenant NO se gasta una consulta al proveedor externo. Es instantáneo y sigue funcionando aunque
   * el RUES esté caído — el caso más común, porque las vinculadoras se repiten entre trámites.
   */
  it('si la empresa ya está en el directorio, la precarga sin consultar el RUES', async () => {
    mocks.lookupLegalRepresentativeByNit.mockResolvedValue({
      company: { nit: NIT_EMPRESA, razonSocial: 'BANCOLOMBIA S.A.S' },
      representatives: [],
    });
    const user = userEvent.setup();
    renderNuevo();

    await consultarVehiculo(user);
    await user.selectOptions(screen.getByLabelText('Tipo de servicio'), 'PUBLICO');
    await user.type(screen.getByLabelText('NIT empresa vinculadora'), NIT_EMPRESA);
    await user.click(screen.getByRole('button', { name: 'Buscar empresa en RUES' }));

    const razonSocial = await screen.findByLabelText('Razón social');
    expect(razonSocial).toHaveTextContent(/^BANCOLOMBIA S\.A\.S$/);
    // El proveedor externo NO se toca cuando el directorio ya tenía el dato.
    expect(mocks.ruesPreview).not.toHaveBeenCalled();
    // Y se dice de dónde salió: no se consultó el RUES, y el gestor tiene derecho a saberlo.
    expect(await screen.findByText(/Precargado desde el directorio/i)).toBeInTheDocument();
    await waitFor(() => expect(screen.getByRole('button', { name: /Continuar/ })).toBeEnabled());
  });

  it('si el directorio falla, no bloquea: cae al RUES', async () => {
    mocks.lookupLegalRepresentativeByNit.mockRejectedValue(new Error('500'));
    mocks.ruesPreview.mockResolvedValue({ found: true, nit: NIT_EMPRESA, razonSocial: 'TRANSPORTES SAS' });
    const user = userEvent.setup();
    renderNuevo();

    await consultarVehiculo(user);
    await user.selectOptions(screen.getByLabelText('Tipo de servicio'), 'PUBLICO');
    await user.type(screen.getByLabelText('NIT empresa vinculadora'), NIT_EMPRESA);
    await user.click(screen.getByRole('button', { name: 'Buscar empresa en RUES' }));

    await waitFor(() => expect(mocks.ruesPreview).toHaveBeenCalled());
    expect(await screen.findByLabelText('Razón social')).toHaveTextContent(/^TRANSPORTES SAS$/);
  });

  it('flujo feliz del RUES: la razón social llega de solo lectura y habilita "Continuar"', async () => {
    mocks.ruesPreview.mockResolvedValue({ found: true, nit: NIT_EMPRESA, razonSocial: 'TRANSPORTES SAS' });
    const user = userEvent.setup();
    renderNuevo();

    await consultarVehiculo(user);
    await user.selectOptions(screen.getByLabelText('Tipo de servicio'), 'PUBLICO');
    await user.type(screen.getByLabelText('NIT empresa vinculadora'), NIT_EMPRESA);
    await user.click(screen.getByRole('button', { name: 'Buscar empresa en RUES' }));

    await waitFor(() => expect(mocks.ruesPreview).toHaveBeenCalledWith({ documentNumber: NIT_EMPRESA }));
    const razonSocial = await screen.findByLabelText('Razón social');
    expect(razonSocial).toHaveTextContent(/^TRANSPORTES SAS$/);
    // No es un campo de formulario: es un `output` de solo lectura. La diferencia importa — un
    // `input` de una línea recorta las razones sociales largas del RUES y, al ser readOnly, el
    // resto del nombre queda fuera del alcance del gestor.
    expect(razonSocial.tagName).toBe('OUTPUT');

    await waitFor(() => expect(screen.getByRole('button', { name: /Continuar/ })).toBeEnabled());

    await user.click(screen.getByRole('button', { name: /Continuar/ }));
    await waitFor(() =>
      expect(mocks.createInstanceFromConsulta).toHaveBeenCalledWith(
        expect.objectContaining({
          tipoServicioCode: 'PUBLICO',
          empresaVinculadoraNit: NIT_EMPRESA,
          empresaVinculadoraRazonSocial: 'TRANSPORTES SAS',
        }),
      ),
    );
  });

  it('RUES found:false — el proveedor respondió y el NIT no existe (no es un fallo transitorio)', async () => {
    mocks.ruesPreview.mockResolvedValue({ found: false, nit: NIT_EMPRESA, razonSocial: null });
    const user = userEvent.setup();
    renderNuevo();

    await consultarVehiculo(user);
    await user.selectOptions(screen.getByLabelText('Tipo de servicio'), 'PUBLICO');
    await user.type(screen.getByLabelText('NIT empresa vinculadora'), NIT_EMPRESA);
    await user.click(screen.getByRole('button', { name: 'Buscar empresa en RUES' }));

    expect(
      await screen.findByText(/No se encontró una empresa con ese NIT en el RUES/),
    ).toBeInTheDocument();
    // Distinto del mensaje de fallo transitorio (503): no se ofrece "Reintentar".
    expect(screen.queryByRole('button', { name: /Reintentar/i })).not.toBeInTheDocument();
    expect(screen.getByRole('button', { name: /Continuar/ })).toBeDisabled();
  });

  it('RUES 503 — fallo transitorio del proveedor, no del operador: se ofrece reintentar', async () => {
    mocks.ruesPreview.mockRejectedValueOnce({ status: 503, message: 'Service Unavailable' });
    mocks.ruesPreview.mockResolvedValueOnce({ found: true, nit: NIT_EMPRESA, razonSocial: 'TRANSPORTES SAS' });
    const user = userEvent.setup();
    renderNuevo();

    await consultarVehiculo(user);
    await user.selectOptions(screen.getByLabelText('Tipo de servicio'), 'PUBLICO');
    await user.type(screen.getByLabelText('NIT empresa vinculadora'), NIT_EMPRESA);
    await user.click(screen.getByRole('button', { name: 'Buscar empresa en RUES' }));

    const aviso = await screen.findByText(/El RUES no respondió/);
    expect(aviso).toBeInTheDocument();
    // Mensaje distinto del "no encontrado": aquí sí se ofrece reintentar.
    expect(screen.queryByText(/No se encontró una empresa con ese NIT/)).not.toBeInTheDocument();
    expect(screen.getByRole('button', { name: /Continuar/ })).toBeDisabled();

    await user.click(screen.getByRole('button', { name: /Reintentar/i }));

    await waitFor(() => expect(mocks.ruesPreview).toHaveBeenCalledTimes(2));
    expect(await screen.findByLabelText('Razón social')).toHaveTextContent(/^TRANSPORTES SAS$/);
  });
});
