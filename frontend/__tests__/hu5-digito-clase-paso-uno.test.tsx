import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

import type { WizardState } from '@/lib/api/types/procedure-runtime';

// HU5 (registro diferido 2026-11-09) — el dígito del paso 1 depende de la clase consultada:
// MOTO (lista exacta) cambia el texto al primer número; REMOLQUE (lista exacta; maquinaria no se
// puede saber aquí sin trámite creado) oculta el selector y el gate HU #11628 no aplica.
// Uso de ejemplo: consultar un VIN cuyo `vehicle_class` es «SEMIRREMOLQUE» → «Continuar» habilitado
// sin dígito y sin escribir `plate_preferred_last_digit`.
// Setup copiado de radicacion-paso-uno.test.tsx.
/**
 * Dígito de preferencia de placa (HU #10805) declarado en el PASO 1, en la tarjeta de radicación,
 * donde lo ubica el repo de diseño. Es el mismo dato que el paso del FUR sigue ofreciendo al radicar
 * sin placa (`plate_preferred_last_digit`): una guía para que el organismo, al asignar una placa de
 * su rango, prefiera una terminada en ese número.
 *
 * Solo se ofrece si la ruta de preasignación está activa para la compañía en ESE organismo
 * (HU #10806, `GET /plate-preassign/status`); si no, el trámite se entrega de forma estándar y el
 * dígito no tendría a quién guiar. Sin trámite creado todavía, la preferencia viaja con la creación.
 */
const mocks = vi.hoisted(() => ({
  getCamaraComercioRequirements: vi.fn(() => Promise.resolve([])),
  createInstance: vi.fn(),
  createInstanceFromConsulta: vi.fn(),
  runPreflightPreview: vi.fn(),
  getWizardPreview: vi.fn(),
  getWizardState: vi.fn(),
  getInstance: vi.fn(),
  patchFieldValues: vi.fn(),
  setCurrentStep: vi.fn(),
  setPriority: vi.fn(),
  runPreflight: vi.fn(),
  getPreflight: vi.fn(),
  getConsultationConfig: vi.fn(),
  listTransitOffices: vi.fn(),
  listVehicleServiceTypes: vi.fn(),
}));

/** Epic #12550 — organismo que devolvería `getOrganismoRuntNoHabilitado` para el error del preview. */
const rutaMocks = vi.hoisted(() => ({ organismoNoHabilitado: null as string | null }));

vi.mock('@/lib/api/tramites-client', () => ({
  tramitesClient: mocks,
  DEV_TENANT_ID: 'tenant-dev',
  DEV_USER_ID: 'user-dev',
  getDuplicateActiveProcedureId: () => null,
  getVehicleStateBlock: () => null,
  isTransitOfficeUnavailable: () => false,
  // Epic #12550 — organismo del RUNT no habilitado (Ruta Corta); null = no es ese error.
  getOrganismoRuntNoHabilitado: () => rutaMocks.organismoNoHabilitado,
  isVehicleBodyTypeMissing: () => false,
  isVehiclePrendaMissing: () => false,
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

function wizard(): WizardState {
  return {
    modalidad: 'matricula_inicial',
    tipologiaCodigo: 'matricula_inicial',
    totalSteps: 3,
    canSubmit: false,
    blockers: [],
    status: 'borrador',
    allowedTransitions: [],
    steps: [
      { index: 1, key: 'consulta_vin', label: 'Consulta VIN', status: 'incomplete', reasons: [] },
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
    createdAt: '2026-08-13T18:00:00Z',
  },
  vehicleFields: [
    { formFieldId: '', fieldKey: 'vehicle_brand', valueText: 'RENAULT', valueJson: null, source: 'consultation' },
  ],
  // Epic #12550 — sin placa el RUNT decide Ruta Larga: el gestor elige secretaría y dígito.
  route: 'larga' as const,
  transitOffice: null,
};

function renderNuevo() {
  mocks.getWizardPreview.mockResolvedValue(wizard());
  return render(
    <TramiteWizard
      procedureTypeCode="MATRICULA_NUEVA"
        family="MATRICULAS"
      title="Nuevo trámite"
      onCreated={() => {}}
      onExit={() => {}}
    />,
  );
}

/** Deja el paso 1 con la consulta RUNT ya resuelta (la tarjeta de radicación aparece con ella). */
async function consultarVehiculo(user: ReturnType<typeof userEvent.setup>) {
  await user.type(await screen.findByLabelText(/Número VIN/i), VIN_VALIDO);
  await user.click(screen.getByRole('button', { name: 'Consultar RUNT' }));
  await waitFor(() => expect(mocks.runPreflightPreview).toHaveBeenCalled());
}

async function elegirSecretaria(
  user: ReturnType<typeof userEvent.setup>,
  nombre: RegExp = /Medellín/,
) {
  await user.click(await screen.findByRole('combobox', { name: /secretaría de tránsito/i }));
  await user.click(await screen.findByRole('option', { name: nombre }));
}

const digito = () => screen.getByLabelText('Dígito de preferencia') as HTMLSelectElement;

beforeEach(() => {
  vi.clearAllMocks();
  rutaMocks.organismoNoHabilitado = null;
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
      createdAt: '2026-08-13T18:00:00Z',
    },
    preflight: PREVIEW_RESULT.preflight,
  });
  mocks.setPriority.mockResolvedValue({ id: 'inst-1', prioritario: true });
});


function previewConClase(clase: string) {
  return {
    ...PREVIEW_RESULT,
    vehicleFields: [
      ...PREVIEW_RESULT.vehicleFields,
      { formFieldId: '', fieldKey: 'vehicle_class', valueText: clase, valueJson: null, source: 'consultation' },
    ],
  };
}

const NOTA_MOTO =
  'Indica el número con el que prefieres que empiece la serie numérica de la moto (ej. ABCX2D o ABCX2). El OT lo tomará como guía al preasignar.';

describe('HU5 — dígito de preferencia según la clase (paso 1)', () => {
  it('MOTO — etiqueta «Dígito numérico de preferencia» y nota del primer número', async () => {
    mocks.runPreflightPreview.mockResolvedValue(previewConClase('Motocicleta'));
    const user = userEvent.setup();
    renderNuevo();
    await consultarVehiculo(user);
    await elegirSecretaria(user);
    const sel = (await screen.findByLabelText('Dígito numérico de preferencia')) as HTMLSelectElement;
    await waitFor(() => expect(sel).toBeEnabled());
    await user.selectOptions(sel, '4');
    expect(screen.getByText(NOTA_MOTO)).toBeInTheDocument();
    expect(screen.queryByText(/\(opcional\)/)).not.toBeInTheDocument();
  });

  it('MOTO — el gate obligatorio sigue aplicando', async () => {
    mocks.runPreflightPreview.mockResolvedValue(previewConClase('CICLOMOTOR'));
    const user = userEvent.setup();
    renderNuevo();
    await consultarVehiculo(user);
    await elegirSecretaria(user);
    await screen.findByLabelText('Dígito numérico de preferencia');
    expect(screen.getByRole('button', { name: /Continuar/ })).toBeDisabled();
    expect(screen.getByText(/Elige un dígito o indica que no tienes preferencia/)).toBeInTheDocument();
  });

  it.each(['REMOLQUE', 'SEMIRREMOLQUE', 'SEMIREMOLQUE', 'Remolque y semirremolque'])(
    'REMOLQUE (%s) — sin selector, sin gate y sin enviar dígito',
    async (clase) => {
      mocks.runPreflightPreview.mockResolvedValue(previewConClase(clase));
      const user = userEvent.setup();
      renderNuevo();
      await consultarVehiculo(user);
      await elegirSecretaria(user);

      expect(screen.queryByLabelText(/Dígito (numérico )?de preferencia/)).not.toBeInTheDocument();
      expect(screen.queryByText(/Elige un dígito o indica que no tienes preferencia/)).not.toBeInTheDocument();
      const continuar = screen.getByRole('button', { name: /Continuar/ });
      await waitFor(() => expect(continuar).toBeEnabled());
      await user.click(continuar);
      await waitFor(() => expect(mocks.createInstanceFromConsulta).toHaveBeenCalled());
      const patches = mocks.patchFieldValues.mock.calls.flatMap(
        (c) => c[1] as { fieldKey: string; valueText: string }[],
      );
      expect(patches.some((i) => i.fieldKey === 'plate_preferred_last_digit' && i.valueText !== '')).toBe(false);
    },
  );

  it.each([
    ['carro', 'AUTOMOVIL'],
    ['clase desconocida', 'CAMION GRUA'],
    ['clase vacía', ''],
  ])('%s — etiqueta, nota y gate actuales', async (_n, clase) => {
    mocks.runPreflightPreview.mockResolvedValue(previewConClase(clase));
    const user = userEvent.setup();
    renderNuevo();
    await consultarVehiculo(user);
    await elegirSecretaria(user);
    await waitFor(() => expect(digito()).toBeEnabled());
    expect(screen.getByRole('button', { name: /Continuar/ })).toBeDisabled();
    await user.selectOptions(digito(), '3');
    expect(
      screen.getByText(
        'Indica el número en el que prefieres que termine la placa. El OT lo toma como guía: puede asignar una placa que termine en ese dígito u otra.',
      ),
    ).toBeInTheDocument();
  });

  it('re-consultar un remolque tras elegir dígito en un carro borra la preferencia', async () => {
    const user = userEvent.setup();
    mocks.runPreflightPreview.mockResolvedValueOnce(previewConClase('AUTOMOVIL'));
    renderNuevo();
    await consultarVehiculo(user);
    await elegirSecretaria(user);
    await waitFor(() => expect(digito()).toBeEnabled());
    await user.selectOptions(digito(), '7');
    mocks.runPreflightPreview.mockResolvedValueOnce(previewConClase('REMOLQUE'));
    await user.click(screen.getByRole('button', { name: /Consultar RUNT/ }));
    await waitFor(() => expect(mocks.runPreflightPreview).toHaveBeenCalledTimes(2));
    await waitFor(() =>
      expect(screen.queryByLabelText(/Dígito (numérico )?de preferencia/)).not.toBeInTheDocument(),
    );
    const continuar = screen.getByRole('button', { name: /Continuar/ });
    await waitFor(() => expect(continuar).toBeEnabled());
    await user.click(continuar);
    await waitFor(() => expect(mocks.createInstanceFromConsulta).toHaveBeenCalled());
    const patches = mocks.patchFieldValues.mock.calls.flatMap(
      (c) => c[1] as { fieldKey: string; valueText: string }[],
    );
    expect(patches.some((i) => i.fieldKey === 'plate_preferred_last_digit' && i.valueText === '7')).toBe(false);
  });
});

// D4 (decisión del usuario 2026-11-09) — en motos las opciones dicen «Primer número N».
// Uso de ejemplo: clase «MOTOCICLETA» → opciones «Sin preferencia», «Primer número 0» … «Primer número 9».
describe('D4 — opciones del selector de dígito (paso 1)', () => {
  it('MOTO — «Primer número N» y el valor enviado sigue siendo el dígito', async () => {
    mocks.runPreflightPreview.mockResolvedValue(previewConClase('MOTOCICLETA'));
    const user = userEvent.setup();
    renderNuevo();
    await consultarVehiculo(user);
    await elegirSecretaria(user);
    const sel = (await screen.findByLabelText('Dígito numérico de preferencia')) as HTMLSelectElement;
    const textos = Array.from(sel.options).filter((o) => !o.hidden).map((o) => o.textContent);
    expect(textos).toEqual(['Sin preferencia', ...Array.from({ length: 10 }, (_, i) => `Primer número ${i}`)]);
    await waitFor(() => expect(sel).toBeEnabled());
    await user.selectOptions(sel, '4');
    expect(sel.value).toBe('4');
  });

  it('CARRO — siguen «Termina en N»', async () => {
    mocks.runPreflightPreview.mockResolvedValue(previewConClase('AUTOMOVIL'));
    const user = userEvent.setup();
    renderNuevo();
    await consultarVehiculo(user);
    await elegirSecretaria(user);
    const textos = Array.from(digito().options).filter((o) => !o.hidden).map((o) => o.textContent);
    expect(textos).toEqual(['Sin preferencia', ...Array.from({ length: 10 }, (_, i) => `Termina en ${i}`)]);
  });
});
