import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

import type { WizardState } from '@/lib/api/types/procedure-runtime';

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

/** Epic #12550 — lo que devuelve el preview para un vehículo con placa preasignada (Ruta Corta). */
const PREVIEW_RUTA_CORTA = {
  ...PREVIEW_RESULT,
  vehicleFields: [
    ...PREVIEW_RESULT.vehicleFields,
    { formFieldId: '', fieldKey: 'plate', valueText: 'WVT948', valueJson: null, source: 'consultation' },
    { formFieldId: '', fieldKey: 'transit_office_name', valueText: 'STRIA TTEyTTO ENVIGADO', valueJson: null, source: 'consultation' },
  ],
  route: 'corta' as const,
  transitOffice: { id: 'ot-envigado', code: '05266000', name: 'Tránsito de Envigado', cityName: 'ENVIGADO' },
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

const digito = () => screen.getByLabelText('Dígito de preasignación de placa') as HTMLSelectElement;

/**
 * HU #11628 — con preasignación activa el dígito exige una elección consciente antes de "Continuar"
 * (dígito o "sin preferencia" explícita); el placeholder ('') ya no vale como respuesta. Los tests que
 * no versan sobre esta regla concreta declaran "sin preferencia" aquí, igual que haría el gestor.
 */
async function declararSinPreferenciaDigito(user: ReturnType<typeof userEvent.setup>) {
  await waitFor(() => expect(digito()).toBeEnabled());
  await user.selectOptions(digito(), 'none');
}

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

/**
 * El interruptor de trámite prioritario se oculta en el paso 1 del wizard.
 * La marca sigue existiendo en `procedure_instances.prioritario` (listado OT y resumen FUR).
 */
describe('Trámite prioritario — paso 1', () => {
  it('no ofrece el interruptor en la consulta del vehículo', async () => {
    const user = userEvent.setup();
    renderNuevo();

    await consultarVehiculo(user);
    await elegirSecretaria(user);
    await declararSinPreferenciaDigito(user);

    expect(screen.queryByRole('button', { name: 'Trámite prioritario' })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /Trámite prioritario/ })).not.toBeInTheDocument();
  });

  it('sin marcarlo no se toca el endpoint (la columna ya nace en false)', async () => {
    const user = userEvent.setup();
    renderNuevo();

    await consultarVehiculo(user);
    await elegirSecretaria(user);
    await declararSinPreferenciaDigito(user);
    await user.click(screen.getByRole('button', { name: /Continuar/ }));

    await waitFor(() => expect(mocks.createInstanceFromConsulta).toHaveBeenCalled());
    expect(mocks.setPriority).not.toHaveBeenCalled();
  });
});

describe('Dígito de preasignación de placa — paso 1', () => {
  it('sin organismo elegido no se puede indicar: la preasignación depende de él', async () => {
    const user = userEvent.setup();
    renderNuevo();

    await consultarVehiculo(user);

    expect(await screen.findByLabelText('Dígito de preasignación de placa')).toBeDisabled();
    expect(screen.getByText(/Elige primero la secretaría/)).toBeInTheDocument();
  });

  it('con organismo elegido se habilita (sin depender del inventario del OT) y la preferencia viaja al crear el trámite', async () => {
    const user = userEvent.setup();
    renderNuevo();

    await consultarVehiculo(user);
    await elegirSecretaria(user);

    // Epic #12550 (decisión B) — ya no se consulta si el organismo tiene inventario de placas.
    await waitFor(() => expect(digito()).toBeEnabled());

    await user.selectOptions(digito(), '7');
    // Sin trámite todavía: no se persiste nada en este momento.
    expect(mocks.patchFieldValues).not.toHaveBeenCalled();

    await user.click(screen.getByRole('button', { name: /Continuar/ }));

    await waitFor(() => expect(mocks.createInstanceFromConsulta).toHaveBeenCalled());
    await waitFor(() =>
      expect(mocks.patchFieldValues).toHaveBeenCalledWith(
        'inst-1',
        expect.arrayContaining([
          expect.objectContaining({ fieldKey: 'plate_preferred_last_digit', valueText: '7' }),
        ]),
        'tenant-1',
      ),
    );
  });

  /**
   * Epic #12550 (HU #12649, AC1/AC4) — Ruta Corta: el RUNT trae placa y organismo; el gestor los ve
   * en solo lectura, no elige secretaría ni dígito, y la creación no envía organismo (lo fija el
   * backend con el del RUNT).
   */
  it('Ruta Corta: placa y organismo del RUNT en solo lectura, sin secretaría ni dígito, y la creación no envía organismo', async () => {
    mocks.runPreflightPreview.mockResolvedValue(PREVIEW_RUTA_CORTA);
    const user = userEvent.setup();
    renderNuevo();

    await consultarVehiculo(user);

    const chip = await screen.findByTestId('ruta-matricula');
    expect(chip).toHaveAttribute('data-ruta', 'corta');
    // Los nombres «Ruta Corta» / «Ruta Larga» son internos: no se muestran al gestor.
    expect(chip).not.toHaveTextContent(/Ruta (Corta|Larga)/);
    expect(chip).toHaveTextContent(/Llegará al organismo listo para su decisión/);
    expect(screen.getByTestId('ruta-corta-placa')).toHaveTextContent('WVT948');
    expect(screen.getByTestId('ruta-corta-organismo')).toHaveTextContent('Tránsito de Envigado');
    expect(screen.queryByRole('combobox', { name: /secretaría de tránsito/i })).not.toBeInTheDocument();
    expect(screen.queryByLabelText('Dígito de preasignación de placa')).not.toBeInTheDocument();
    // El organismo de «Datos del vehículo» tampoco se edita: viene del RUNT igual que la placa.
    expect(screen.queryByRole('button', { name: 'Editar' })).not.toBeInTheDocument();

    // Continuar no exige secretaría ni dígito.
    expect(screen.getByRole('button', { name: /Continuar/ })).toBeEnabled();
    await user.click(screen.getByRole('button', { name: /Continuar/ }));
    await waitFor(() => expect(mocks.createInstanceFromConsulta).toHaveBeenCalled());
    expect(mocks.createInstanceFromConsulta.mock.calls[0][0]).toMatchObject({
      previewToken: 'token-abc',
      transitOfficeId: undefined,
    });
    // Y no viaja ninguna preferencia de dígito ni el campo informativo de ruta.
    const patches = mocks.patchFieldValues.mock.calls.flatMap(
      (c) => c[1] as { fieldKey: string; valueText: string }[],
    );
    expect(patches.find((i) => i.fieldKey === 'plate_route_active')).toBeUndefined();
    expect(patches.find((i) => i.fieldKey === 'plate_preferred_last_digit')?.valueText ?? '').toBe('');
  });

  /**
   * Epic #12550 (HU #12649, AC3) — el RUNT reporta un organismo que la compañía no tiene habilitado:
   * se nombra el organismo y no se puede continuar (no hay consulta válida).
   */
  it('organismo del RUNT no habilitado: se dice cuál y no se puede continuar', async () => {
    rutaMocks.organismoNoHabilitado = 'STRIA TTOyTTE PALMIRA';
    mocks.runPreflightPreview.mockRejectedValue(
      Object.assign(new Error('422'), {
        status: 422,
        problem: { title: 'organismo_runt_no_habilitado', transitOfficeName: 'STRIA TTOyTTE PALMIRA' },
      }),
    );
    const user = userEvent.setup();
    renderNuevo();

    await consultarVehiculo(user);

    const aviso = await screen.findByText(/no tiene habilitado ese organismo de tránsito/);
    expect(aviso).toHaveTextContent('STRIA TTOyTTE PALMIRA');
    expect(screen.queryByTestId('ruta-matricula')).not.toBeInTheDocument();
    expect(screen.getByRole('button', { name: /Continuar/ })).toBeDisabled();
  });

  it('Ruta Corta sin organismo en el RUNT: se dice y se radica en la secretaría que se elija después', async () => {
    mocks.runPreflightPreview.mockResolvedValue({
      ...PREVIEW_RUTA_CORTA,
      vehicleFields: PREVIEW_RUTA_CORTA.vehicleFields.filter((f) => f.fieldKey !== 'transit_office_name'),
      transitOffice: null,
    });
    const user = userEvent.setup();
    renderNuevo();

    await consultarVehiculo(user);

    expect(await screen.findByTestId('ruta-corta-organismo')).toHaveTextContent(/El RUNT no reporta el organismo/);
    expect(screen.getByRole('button', { name: /Continuar/ })).toBeEnabled();
  });

  /**
   * Epic #12550 (HU #12649, AC2) — Ruta Larga: chip explicativo y los controles de siempre; el campo
   * informativo `plate_route_active` (HU #10806) dejó de escribirse.
   */
  it('Ruta Larga: chip, secretaría y dígito, sin escribir plate_route_active', async () => {
    const user = userEvent.setup();
    renderNuevo();

    await consultarVehiculo(user);

    const chip = await screen.findByTestId('ruta-matricula');
    expect(chip).toHaveAttribute('data-ruta', 'larga');
    expect(chip).toHaveTextContent(/la asignará en Preasignación/);
    expect(chip).not.toHaveTextContent(/Ruta (Corta|Larga)/);
    // Sin placa del RUNT el organismo de «Datos del vehículo» sí se puede corregir.
    expect(screen.getByRole('button', { name: 'Editar' })).toBeInTheDocument();

    await elegirSecretaria(user);
    await declararSinPreferenciaDigito(user);
    await user.click(screen.getByRole('button', { name: /Continuar/ }));

    await waitFor(() => expect(mocks.createInstanceFromConsulta).toHaveBeenCalled());
    expect(mocks.createInstanceFromConsulta.mock.calls[0][0]).toMatchObject({ transitOfficeId: SECRETARIA_ID });
    const patches = mocks.patchFieldValues.mock.calls.flatMap(
      (c) => c[1] as { fieldKey: string; valueText: string }[],
    );
    expect(patches.find((i) => i.fieldKey === 'plate_route_active')).toBeUndefined();
  });

  /**
   * La preasignación es del organismo: si el gestor corrige dónde radica, la preferencia que ya
   * declaró no puede viajar intacta al organismo nuevo, que quizá ni opera con rangos.
   */
  it('cambiar de organismo reinicia la preferencia ya elegida', async () => {
    const user = userEvent.setup();
    renderNuevo();

    await consultarVehiculo(user);
    await elegirSecretaria(user);
    await waitFor(() => expect(digito()).toBeEnabled());
    await user.selectOptions(digito(), '7');
    expect(digito()).toHaveValue('7');

    await elegirSecretaria(user, /Envigado/);

    // HU #11628 — el reinicio deja el selector en "no decidido" (placeholder), no en "sin
    // preferencia": son estados distintos y el segundo exige una elección nueva del gestor.
    await waitFor(() => expect(digito()).toHaveValue(''));
    await declararSinPreferenciaDigito(user);

    await user.click(screen.getByRole('button', { name: /Continuar/ }));
    await waitFor(() => expect(mocks.createInstanceFromConsulta).toHaveBeenCalled());
    // Lo anotado antes se limpió: la creación no lleva una preferencia del organismo anterior.
    const patches = mocks.patchFieldValues.mock.calls.flatMap((c) => c[1] as { fieldKey: string; valueText: string }[]);
    const digitoPersistido = patches.find((i) => i.fieldKey === 'plate_preferred_last_digit');
    expect(digitoPersistido?.valueText ?? '').toBe('');
  });
});

/**
 * HU #11628 — el dígito de preferencia de placa exige una elección consciente. El valor vacío dejaba
 * de significar dos cosas indistinguibles: "no lo he tocado" y "no tengo preferencia".
 */
describe('HU #11628 — elección consciente del dígito de preferencia', () => {
  it('AC1 — sin decidir, con preasignación activa, "Continuar" no avanza y explica qué falta', async () => {
    const user = userEvent.setup();
    renderNuevo();

    await consultarVehiculo(user);
    await elegirSecretaria(user);
    await waitFor(() => expect(digito()).toBeEnabled());

    // Ni dígito ni "sin preferencia": el selector queda en el placeholder ("no decidido").
    expect(digito()).toHaveValue('');

    const continuarBtn = screen.getByRole('button', { name: /Continuar/ });
    expect(continuarBtn).toBeDisabled();
    expect(
      screen.getByText(/Elige un dígito o indica que no tienes preferencia/),
    ).toBeInTheDocument();

    // Clic sobre el botón deshabilitado: no avanza (no crea el trámite).
    await user.click(continuarBtn);
    expect(mocks.createInstanceFromConsulta).not.toHaveBeenCalled();
  });

  it('AC2 — declarar "sin preferencia" avanza y queda registrado como declarado (no como no-decidido)', async () => {
    const user = userEvent.setup();
    renderNuevo();

    await consultarVehiculo(user);
    await elegirSecretaria(user);
    await declararSinPreferenciaDigito(user);

    expect(screen.getByRole('button', { name: /Continuar/ })).toBeEnabled();

    await user.click(screen.getByRole('button', { name: /Continuar/ }));

    await waitFor(() => expect(mocks.createInstanceFromConsulta).toHaveBeenCalled());
    await waitFor(() => {
      const patches = mocks.patchFieldValues.mock.calls.flatMap(
        (c) => c[1] as { fieldKey: string; valueText: string }[],
      );
      // Contrato SIN CAMBIOS: sigue siendo cadena vacía (ausencia), igual que antes de la HU.
      expect(
        patches.find((i) => i.fieldKey === 'plate_preferred_last_digit')?.valueText,
      ).toBe('');
      // La señal aparte SÍ distingue "declaró sin preferencia" de "no decidido".
      expect(
        patches.find((i) => i.fieldKey === 'plate_preferred_last_digit_declared')?.valueText,
      ).toBe('true');
    });
  });

  it('AC3 — elegir un dígito avanza y persiste el mismo contrato de siempre', async () => {
    const user = userEvent.setup();
    renderNuevo();

    await consultarVehiculo(user);
    await elegirSecretaria(user);
    await waitFor(() => expect(digito()).toBeEnabled());
    await user.selectOptions(digito(), '3');

    expect(screen.getByRole('button', { name: /Continuar/ })).toBeEnabled();
    await user.click(screen.getByRole('button', { name: /Continuar/ }));

    await waitFor(() => expect(mocks.createInstanceFromConsulta).toHaveBeenCalled());
    await waitFor(() => {
      const patches = mocks.patchFieldValues.mock.calls.flatMap(
        (c) => c[1] as { fieldKey: string; valueText: string }[],
      );
      expect(
        patches.find((i) => i.fieldKey === 'plate_preferred_last_digit')?.valueText,
      ).toBe('3');
      expect(
        patches.find((i) => i.fieldKey === 'plate_preferred_last_digit_declared')?.valueText,
      ).toBe('true');
    });
  });

  it('AC4 (revisado por Epic #12550) — en Ruta Corta no hay dígito que exigir: "Continuar" avanza', async () => {
    mocks.runPreflightPreview.mockResolvedValue(PREVIEW_RUTA_CORTA);
    const user = userEvent.setup();
    renderNuevo();

    await consultarVehiculo(user);
    await screen.findByTestId('ruta-corta-placa');

    expect(screen.getByRole('button', { name: /Continuar/ })).toBeEnabled();
    await user.click(screen.getByRole('button', { name: /Continuar/ }));
    await waitFor(() => expect(mocks.createInstanceFromConsulta).toHaveBeenCalled());
  });

  it('AC5 (revisado por Epic #12550) — la Ruta Larga no escribe plate_route_active: la ruta la decide el estado del trámite', async () => {
    const user = userEvent.setup();
    renderNuevo();

    await consultarVehiculo(user);
    await elegirSecretaria(user);
    await declararSinPreferenciaDigito(user);
    await user.click(screen.getByRole('button', { name: /Continuar/ }));

    await waitFor(() => expect(mocks.createInstanceFromConsulta).toHaveBeenCalled());
    const patches = mocks.patchFieldValues.mock.calls.flatMap(
      (c) => c[1] as { fieldKey: string; valueText: string }[],
    );
    expect(patches.find((i) => i.fieldKey === 'plate_route_active')).toBeUndefined();
  });
});

/**
 * REGRESIÓN — la tarjeta de radicación sobrevive a la creación del trámite.
 *
 * La tarjeta se pintaba con la misma condición que decide si el organismo viaja en el cuerpo de la
 * creación, es decir «mientras el trámite no exista». Al continuar, el trámite se creaba y la
 * tarjeta desaparecía: el gestor volvía al paso 1 y no encontraba ni la secretaría que acababa de
 * elegir, ni el dígito, ni la prioridad. Los tres datos estaban guardados; no había dónde verlos.
 *
 * Ahora la tarjeta se pinta en matrícula siempre que haya vehículo, y sobre un trámite creado los
 * tres controles releen su valor y lo guardan en el acto.
 */
describe('Tarjeta de radicación sobre un trámite ya creado', () => {
  const INSTANCIA = {
    id: 'inst-1',
    referenceNumber: 'MAT-2026-000001',
    status: 'borrador' as const,
    procedureTypeId: 'type-1',
    tenantId: 'tenant-1',
    createdAt: '2026-08-13T18:00:00Z',
    submittedAt: null,
    completedAt: null,
    prioritario: true,
    fieldValues: [
      { formFieldId: '', fieldKey: 'vin', valueText: VIN_VALIDO, valueJson: null, source: 'consultation' },
      { formFieldId: '', fieldKey: 'vehicle_brand', valueText: 'RENAULT', valueJson: null, source: 'consultation' },
      { formFieldId: '', fieldKey: 'transit_office_id', valueText: SECRETARIA_ID, valueJson: null, source: 'manual' },
      { formFieldId: '', fieldKey: 'plate_preferred_last_digit', valueText: '7', valueJson: null, source: 'manual' },
    ],
    statusHistory: [],
    actors: [],
  };

  function renderExistente() {
    mocks.getWizardState.mockResolvedValue(wizard());
    mocks.getInstance.mockResolvedValue(INSTANCIA);
    mocks.getPreflight.mockResolvedValue(PREVIEW_RESULT.preflight);
    return render(<TramiteWizard existingInstanceId="inst-1" onExit={() => {}} />);
  }

  it('muestra secretaría y dígito ya guardados en vez de esconder la tarjeta', async () => {
    renderExistente();

    // Secretaría: el combobox llega con el organismo elegido, no con el aviso de "aún no has…".
    // La hidratación es asíncrona (sale del detalle del trámite), así que se espera al valor.
    const combo = await screen.findByRole('combobox', { name: /secretaría de tránsito/i });
    await waitFor(() => expect(combo).toHaveValue('Secretaría de Movilidad de Medellín'));
    expect(screen.queryByText(/Aún no has seleccionado la secretaría/)).toBeNull();

    await waitFor(() => expect(digito().value).toBe('7'));
    expect(screen.queryByRole('button', { name: /Trámite prioritario/ })).not.toBeInTheDocument();
  });

  /**
   * Epic #12550 (HU #12649, AC5) — un borrador con placa del RUNT se rehidrata en Ruta Corta: placa y
   * organismo del expediente en solo lectura, sin secretaría ni dígito editables.
   */
  it('un borrador con placa del RUNT se rehidrata en Ruta Corta, en solo lectura', async () => {
    mocks.getWizardState.mockResolvedValue(wizard());
    mocks.getInstance.mockResolvedValue({
      ...INSTANCIA,
      fieldValues: [
        { formFieldId: '', fieldKey: 'vin', valueText: VIN_VALIDO, valueJson: null, source: 'consultation' },
        { formFieldId: '', fieldKey: 'vehicle_brand', valueText: 'RENAULT', valueJson: null, source: 'consultation' },
        { formFieldId: '', fieldKey: 'plate', valueText: 'WVT948', valueJson: null, source: 'consultation' },
        { formFieldId: '', fieldKey: 'transit_office_id', valueText: 'ot-envigado', valueJson: null, source: 'manual' },
        { formFieldId: '', fieldKey: 'transit_office_name', valueText: 'Tránsito de Envigado', valueJson: null, source: 'manual' },
      ],
    });
    mocks.getPreflight.mockResolvedValue(PREVIEW_RESULT.preflight);
    render(<TramiteWizard existingInstanceId="inst-1" onExit={() => {}} />);

    expect(await screen.findByTestId('ruta-matricula')).toHaveAttribute('data-ruta', 'corta');
    expect(screen.getByTestId('ruta-corta-placa')).toHaveTextContent('WVT948');
    expect(screen.getByTestId('ruta-corta-organismo')).toHaveTextContent('Tránsito de Envigado');
    expect(screen.queryByRole('combobox', { name: /secretaría de tránsito/i })).not.toBeInTheDocument();
    expect(screen.queryByLabelText('Dígito de preasignación de placa')).not.toBeInTheDocument();
  });

  it('no ofrece cambiar la prioridad desde el paso 1', async () => {
    renderExistente();

    expect(await screen.findByRole('combobox', { name: /secretaría de tránsito/i })).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /Trámite prioritario/ })).not.toBeInTheDocument();
    expect(mocks.setPriority).not.toHaveBeenCalled();
  });

  it('cambiar de organismo lo persiste con su nombre, que es lo que leen el FUR y el listado', async () => {
    const user = userEvent.setup();
    renderExistente();
    await screen.findByRole('combobox', { name: /secretaría de tránsito/i });

    await elegirSecretaria(user, /Envigado/);

    await waitFor(() =>
      expect(mocks.patchFieldValues).toHaveBeenCalledWith('inst-1', [
        { formFieldId: null, fieldKey: 'transit_office_id', valueText: 'ot-envigado', valueJson: null },
        {
          formFieldId: null,
          fieldKey: 'transit_office_name',
          valueText: 'Tránsito de Envigado',
          valueJson: null,
        },
      ]),
    );
  });
});

/**
 * REGRESIÓN — el velo de espera solo lo levanta el gestor.
 *
 * La escena del vehículo se colgó de `loading`, que incluye la recarga automática del pre-vuelo al
 * abrir el paso. Resultado: el velo aparecía en cada entrada al asistente sin que nadie hubiera
 * pulsado nada, y el gestor veía el carrito «en cada recarga». Una espera que el usuario no provocó
 * no se anuncia tapándole la pantalla: se anuncia en el sitio que la provocó, y aquí no hay ninguno.
 */
describe('Velo de espera del paso 1', () => {
  it('no aparece al abrir el paso, aunque el pre-vuelo se esté recargando solo', async () => {
    renderNuevo();
    await screen.findByLabelText(/Número VIN/i);
    expect(screen.queryByText(/Consultando información en el RUNT/)).toBeNull();
  });

  it('aparece mientras dura la consulta que dispara el gestor', async () => {
    // La consulta queda colgada a propósito: así el velo sigue en pantalla al asertar.
    let resolver: (v: unknown) => void = () => {};
    mocks.runPreflightPreview.mockImplementation(
      () => new Promise((r) => { resolver = r; }),
    );

    const user = userEvent.setup();
    renderNuevo();
    await user.type(await screen.findByLabelText(/Número VIN/i), VIN_VALIDO);
    await user.click(screen.getByRole('button', { name: 'Consultar RUNT' }));

    expect(await screen.findByText(/Consultando información en el RUNT/)).toBeInTheDocument();

    resolver(PREVIEW_RESULT);
    await waitFor(() =>
      expect(screen.queryByText(/Consultando información en el RUNT/)).toBeNull(),
    );
  });
});
