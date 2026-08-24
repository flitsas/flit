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

const plateMocks = vi.hoisted(() => ({ getPlatePreassignStatus: vi.fn() }));

vi.mock('@/lib/api/tramites-client', () => ({
  tramitesClient: mocks,
  DEV_TENANT_ID: 'tenant-dev',
  DEV_USER_ID: 'user-dev',
  getDuplicateActiveProcedureId: () => null,
  getVehicleStateBlock: () => null,
  isTransitOfficeUnavailable: () => false,
}));

vi.mock('@/lib/api/admin-plate-ranges', () => plateMocks);

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
};

function renderNuevo() {
  mocks.getWizardPreview.mockResolvedValue(wizard());
  return render(
    <TramiteWizard
      procedureTypeCode="MATRICULA_NUEVA"
        modalidad="matricula_inicial"
      title="Nuevo trámite"
      onCreated={() => {}}
      onExit={() => {}}
    />,
  );
}

/** Deja el paso 1 con la consulta RUNT ya resuelta (la tarjeta de radicación aparece con ella). */
async function consultarVehiculo(user: ReturnType<typeof userEvent.setup>) {
  await user.type(await screen.findByLabelText('Número VIN'), VIN_VALIDO);
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
  plateMocks.getPlatePreassignStatus.mockResolvedValue({ enabled: true });
  mocks.setPriority.mockResolvedValue({ id: 'inst-1', prioritario: true });
});

/**
 * Trámite prioritario (HU #10536). No es un field_value: vive en `procedure_instances.prioritario`
 * y tiene endpoint propio, que necesita el id. Por eso la marca del paso 1 se recuerda y se aplica
 * justo después de crear el trámite, en vez de viajar con el patch del resto de lo capturado.
 */
describe('Trámite prioritario — paso 1', () => {
  it('marcarlo aplica la prioridad en cuanto el trámite existe', async () => {
    const user = userEvent.setup();
    renderNuevo();

    await consultarVehiculo(user);
    await elegirSecretaria(user);
    await declararSinPreferenciaDigito(user);

    const toggle = await screen.findByRole('button', { name: 'Trámite prioritario' });
    expect(toggle).toHaveAttribute('aria-pressed', 'false');
    await user.click(toggle);
    expect(toggle).toHaveAttribute('aria-pressed', 'true');
    // Sin trámite todavía: no se puede llamar al endpoint, que exige el id.
    expect(mocks.setPriority).not.toHaveBeenCalled();

    await user.click(screen.getByRole('button', { name: /Continuar/ }));

    await waitFor(() => expect(mocks.createInstanceFromConsulta).toHaveBeenCalled());
    await waitFor(() =>
      expect(mocks.setPriority).toHaveBeenCalledWith('inst-1', true, 'tenant-1'),
    );
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

  /** Es una preferencia de orden en la bandeja del OT, no un requisito: si falla, el trámite sigue. */
  it('si la marca falla, el trámite ya creado no se pierde: se navega igual', async () => {
    mocks.setPriority.mockRejectedValue(new Error('503'));
    const onCreated = vi.fn();
    const user = userEvent.setup();
    mocks.getWizardPreview.mockResolvedValue(wizard());
    render(
      <TramiteWizard
        procedureTypeCode="MATRICULA_NUEVA"
        modalidad="matricula_inicial"
        title="Nuevo trámite"
        onCreated={onCreated}
        onExit={() => {}}
      />,
    );

    await consultarVehiculo(user);
    await elegirSecretaria(user);
    await declararSinPreferenciaDigito(user);
    await user.click(await screen.findByRole('button', { name: 'Trámite prioritario' }));
    await user.click(screen.getByRole('button', { name: /Continuar/ }));

    await waitFor(() => expect(onCreated).toHaveBeenCalledWith(expect.objectContaining({ id: 'inst-1' })));
  });
});

describe('Dígito de preasignación de placa — paso 1', () => {
  it('sin organismo elegido no se puede indicar: la preasignación depende de él', async () => {
    const user = userEvent.setup();
    renderNuevo();

    await consultarVehiculo(user);

    expect(await screen.findByLabelText('Dígito de preasignación de placa')).toBeDisabled();
    expect(screen.getByText(/Elige primero la secretaría/)).toBeInTheDocument();
    // No se consulta el estado de la ruta hasta que haya organismo que consultar.
    expect(plateMocks.getPlatePreassignStatus).not.toHaveBeenCalled();
  });

  it('con preasignación activa se habilita y la preferencia viaja al crear el trámite', async () => {
    const user = userEvent.setup();
    renderNuevo();

    await consultarVehiculo(user);
    await elegirSecretaria(user);

    await waitFor(() =>
      expect(plateMocks.getPlatePreassignStatus).toHaveBeenCalledWith(SECRETARIA_ID),
    );
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
   * AC2 (HU #10799) — misma regla que el paso del FUR: si la consulta trae placa, el vehículo ya
   * está matriculado y no hay nada que el organismo tenga que asignarle.
   */
  it('con placa del RUNT no aplica la preasignación: se dice y no se ofrece el dígito', async () => {
    mocks.runPreflightPreview.mockResolvedValue({
      ...PREVIEW_RESULT,
      vehicleFields: [
        ...PREVIEW_RESULT.vehicleFields,
        { formFieldId: '', fieldKey: 'plate', valueText: 'JNH38H', valueJson: null, source: 'consultation' },
      ],
    });
    const user = userEvent.setup();
    renderNuevo();

    await consultarVehiculo(user);
    await elegirSecretaria(user);

    // La placa se nombra dentro del propio aviso (también se pinta arriba, en los datos del RUNT).
    const aviso = await screen.findByText(/No aplica la preasignación de placa/);
    expect(aviso).toHaveTextContent('JNH38H');
    expect(screen.queryByLabelText('Dígito de preasignación de placa')).not.toBeInTheDocument();
    // Ni se consulta el estado de la ruta: no hay ruta que decidir.
    expect(plateMocks.getPlatePreassignStatus).not.toHaveBeenCalled();

    await user.click(screen.getByRole('button', { name: /Continuar/ }));
    await waitFor(() => expect(mocks.createInstanceFromConsulta).toHaveBeenCalled());
    // Y el trámite no nace con la ruta de preasignación activa.
    const patches = mocks.patchFieldValues.mock.calls.flatMap(
      (c) => c[1] as { fieldKey: string; valueText: string }[],
    );
    expect(patches.find((i) => i.fieldKey === 'plate_route_active')?.valueText ?? 'false').toBe('false');
  });

  /**
   * HU #10806 (Alternativa C) — `plate_route_active` es lo que lee el trigger de BD para marcar
   * `plate_flow_status = 'preasignado'` al radicar sin placa. El paso del FUR lo escribe al abrir su
   * sección; aquí viaja con la creación.
   */
  it('la decisión de ruta queda persistida con el trámite', async () => {
    const user = userEvent.setup();
    renderNuevo();

    await consultarVehiculo(user);
    await elegirSecretaria(user);
    await declararSinPreferenciaDigito(user);

    await user.click(screen.getByRole('button', { name: /Continuar/ }));

    await waitFor(() =>
      expect(mocks.patchFieldValues).toHaveBeenCalledWith(
        'inst-1',
        expect.arrayContaining([
          expect.objectContaining({ fieldKey: 'plate_route_active', valueText: 'true' }),
        ]),
        'tenant-1',
      ),
    );
  });

  it('organismo sin preasignación: se explica que el trámite se entrega estándar y no se ofrece', async () => {
    plateMocks.getPlatePreassignStatus.mockResolvedValue({ enabled: false });
    const user = userEvent.setup();
    renderNuevo();

    await consultarVehiculo(user);
    await elegirSecretaria(user);

    expect(
      await screen.findByText(/no tiene preasignación de placa activa/),
    ).toBeInTheDocument();
    expect(digito()).toBeDisabled();
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
    await waitFor(() =>
      expect(plateMocks.getPlatePreassignStatus).toHaveBeenLastCalledWith('ot-envigado'),
    );
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

  it('AC4 — organismo sin preasignación activa: "Continuar" avanza sin exigir nada (no se puede bloquear)', async () => {
    plateMocks.getPlatePreassignStatus.mockResolvedValue({ enabled: false });
    const user = userEvent.setup();
    renderNuevo();

    await consultarVehiculo(user);
    await elegirSecretaria(user);
    await waitFor(() => expect(digito()).toBeDisabled());

    // El selector deshabilitado no exige elección: el botón sigue habilitado.
    expect(screen.getByRole('button', { name: /Continuar/ })).toBeEnabled();

    await user.click(screen.getByRole('button', { name: /Continuar/ }));
    await waitFor(() => expect(mocks.createInstanceFromConsulta).toHaveBeenCalled());
  });

  it('AC5 — sin placa y sin dígito declarado, la ruta de preasignación sigue activándose igual que antes', async () => {
    const user = userEvent.setup();
    renderNuevo();

    await consultarVehiculo(user);
    await elegirSecretaria(user);
    await declararSinPreferenciaDigito(user);
    await user.click(screen.getByRole('button', { name: /Continuar/ }));

    // `plate_route_active` (lo que gobierna la ruta de preasignación al radicar sin placa) sigue
    // dependiendo únicamente de `getPlatePreassignStatus`, no del dígito — mismo comportamiento que
    // antes de la HU #11628.
    await waitFor(() =>
      expect(mocks.patchFieldValues).toHaveBeenCalledWith(
        'inst-1',
        expect.arrayContaining([
          expect.objectContaining({ fieldKey: 'plate_route_active', valueText: 'true' }),
        ]),
        'tenant-1',
      ),
    );
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

  it('muestra los tres datos ya guardados en vez de esconder la tarjeta', async () => {
    renderExistente();

    // Secretaría: el combobox llega con el organismo elegido, no con el aviso de "aún no has…".
    // La hidratación es asíncrona (sale del detalle del trámite), así que se espera al valor.
    const combo = await screen.findByRole('combobox', { name: /secretaría de tránsito/i });
    await waitFor(() => expect(combo).toHaveValue('Secretaría de Movilidad de Medellín'));
    expect(screen.queryByText(/Aún no has seleccionado la secretaría/)).toBeNull();

    // Dígito y prioridad, releídos del expediente.
    await waitFor(() => expect(digito().value).toBe('7'));
    expect(screen.getByRole('button', { name: /Trámite prioritario/ })).toHaveAttribute(
      'aria-pressed',
      'true',
    );
  });

  it('cambiar la prioridad se guarda en el acto, sin esperar a "Continuar"', async () => {
    const user = userEvent.setup();
    renderExistente();

    const toggle = await screen.findByRole('button', { name: /Trámite prioritario/ });
    await waitFor(() => expect(toggle).toHaveAttribute('aria-pressed', 'true'));
    await user.click(toggle);

    await waitFor(() => expect(mocks.setPriority).toHaveBeenCalledWith('inst-1', false));
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
    await screen.findByLabelText('Número VIN');
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
    await user.type(await screen.findByLabelText('Número VIN'), VIN_VALIDO);
    await user.click(screen.getByRole('button', { name: 'Consultar RUNT' }));

    expect(await screen.findByText(/Consultando información en el RUNT/)).toBeInTheDocument();

    resolver(PREVIEW_RESULT);
    await waitFor(() =>
      expect(screen.queryByText(/Consultando información en el RUNT/)).toBeNull(),
    );
  });
});
