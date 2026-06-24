import { StrictMode } from 'react';
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

import type {
  CommercialData,
  PreflightSnapshot,
  ProcedureConfiguration,
  WizardState,
} from '@/lib/api/types/procedure-runtime';

// ── Mock del cliente HTTP (sin red real) ───────────────────────────
const mocks = vi.hoisted(() => ({
  createInstance: vi.fn(),
  getInstance: vi.fn(),
  getWizardState: vi.fn(),
  patchFieldValues: vi.fn(),
  runPreflight: vi.fn(),
  getPreflight: vi.fn(),
  getCommercial: vi.fn(),
  putCommercial: vi.fn(),
  submitInstance: vi.fn(),
  // dependencias de los componentes embebidos
  getActors: vi.fn(),
  saveActors: vi.fn(),
  getChecklist: vi.fn(),
  getAttachments: vi.fn(),
  uploadAttachment: vi.fn(),
  deleteAttachment: vi.fn(),
}));

vi.mock('@/lib/api/tramites-client', () => ({
  tramitesClient: mocks,
  DEV_TENANT_ID: 'tenant-dev',
  DEV_USER_ID: 'user-dev',
}));

// El wizard usa useToast() para el aviso de "enviado a tránsito"; se stubea para
// no exigir <ToastProvider> en cada render y poder asertar el mensaje.
const toastShow = vi.hoisted(() => vi.fn());
vi.mock('@/components/admin/Toast', () => ({
  useToast: () => ({ show: toastShow }),
}));

import { TramiteWizard } from '@/components/operacion/TramiteWizard';

const CONFIG: ProcedureConfiguration = {
  id: 'type-1',
  code: 'TRASPASO_STD',
  name: 'Traspaso estándar',
  family: 'TRASPASO',
  publishedAt: '2026-06-18T00:00:00Z',
  conformationRules: [],
  steps: [],
};

const MATRICULA_WIZARD: WizardState = {
  modalidad: 'matricula_inicial',
  tipologiaCodigo: 'matricula_inicial',
  totalSteps: 5,
  canSubmit: false,
  blockers: ['documentos_incompletos'],
  steps: [
    { index: 0, key: 'consulta_vin', label: 'Consulta VIN', status: 'complete', reasons: [] },
    { index: 1, key: 'documentos', label: 'Documentos', status: 'incomplete', reasons: ['documentos_incompletos'] },
    { index: 2, key: 'comprador', label: 'Comprador', status: 'incomplete', reasons: ['runt_comprador'] },
    { index: 3, key: 'identidad', label: 'Identidad', status: 'locked', reasons: [] },
    { index: 4, key: 'fur', label: 'FUR', status: 'locked', reasons: [] },
  ],
};

const TRASPASO_WIZARD: WizardState = {
  modalidad: 'traspaso',
  tipologiaCodigo: 'traspaso',
  totalSteps: 6,
  canSubmit: false,
  blockers: ['preflight_red'],
  steps: [
    { index: 0, key: 'consulta', label: 'Consulta', status: 'complete', reasons: [] },
    { index: 1, key: 'validacion', label: 'Validación', status: 'complete', reasons: [] },
    { index: 2, key: 'vendedor', label: 'Vendedor', status: 'complete', reasons: [] },
    { index: 3, key: 'comprador', label: 'Comprador', status: 'complete', reasons: [] },
    { index: 4, key: 'comercial', label: 'Comercial', status: 'complete', reasons: [] },
    { index: 5, key: 'fur', label: 'FUR', status: 'incomplete', reasons: ['fur_pendiente'] },
  ],
};

// Wizard con TODOS los pasos completos: el caso típico de un trámite ya enviado
// a tránsito (submitted), navegable de extremo a extremo en solo lectura.
const SUBMITTED_WIZARD: WizardState = {
  modalidad: 'matricula_inicial',
  tipologiaCodigo: 'matricula_inicial',
  totalSteps: 5,
  canSubmit: true,
  blockers: [],
  steps: [
    { index: 0, key: 'consulta_vin', label: 'Consulta VIN', status: 'complete', reasons: [] },
    { index: 1, key: 'documentos', label: 'Documentos', status: 'complete', reasons: [] },
    { index: 2, key: 'comprador', label: 'Comprador', status: 'complete', reasons: [] },
    { index: 3, key: 'identidad', label: 'Identidad', status: 'complete', reasons: [] },
    { index: 4, key: 'fur', label: 'FUR', status: 'complete', reasons: [] },
  ],
};

const GREEN_PREFLIGHT: PreflightSnapshot = {
  overall: 'green',
  checks: [
    { key: 'soat', label: 'SOAT', status: 'ok', source: 'RUNT', message: 'Vigente' },
  ],
  createdAt: '2026-06-18T00:00:00Z',
};

const EMPTY_COMMERCIAL: CommercialData = {
  valorVenta: null,
  causal: null,
  tasaImpuesto: null,
  derechos: null,
  metodoPago: null,
};

beforeEach(() => {
  vi.clearAllMocks();
  mocks.createInstance.mockResolvedValue({ id: 'inst-1' });
  mocks.getInstance.mockResolvedValue({ id: 'inst-1', fieldValues: [] });
  mocks.getWizardState.mockResolvedValue(MATRICULA_WIZARD);
  mocks.patchFieldValues.mockResolvedValue({ id: 'inst-1', fieldValues: [] });
  mocks.runPreflight.mockResolvedValue(GREEN_PREFLIGHT);
  mocks.getPreflight.mockResolvedValue(null);
  mocks.getCommercial.mockResolvedValue(EMPTY_COMMERCIAL);
  mocks.putCommercial.mockResolvedValue(EMPTY_COMMERCIAL);
  mocks.submitInstance.mockResolvedValue({ id: 'inst-1' });
  mocks.getActors.mockResolvedValue([]);
  mocks.saveActors.mockResolvedValue(undefined);
  mocks.getChecklist.mockResolvedValue({ items: [], faltanObligatorios: 0, completo: true });
  mocks.getAttachments.mockResolvedValue([]);
});

function renderWizard() {
  return render(
    <TramiteWizard configuration={CONFIG} procedureTypeId="type-1" onExit={() => {}} />,
  );
}

function renderWizardStrict() {
  return render(
    <StrictMode>
      <TramiteWizard configuration={CONFIG} procedureTypeId="type-1" onExit={() => {}} />
    </StrictMode>,
  );
}

describe('TramiteWizard — sidebar server-driven por modalidad', () => {
  it('matrícula pinta 5 pasos (VIN-first)', async () => {
    renderWizard();
    // 5 pasos en el sidebar, etiquetados por aria-label único.
    const stepButtons = await screen.findAllByRole('button', { name: /^Paso \d+:/ });
    expect(stepButtons).toHaveLength(5);
    expect(screen.getByRole('button', { name: /^Paso 1: Consulta VIN/ })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /^Paso 2: Documentos/ })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /^Paso 3: Comprador/ })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /^Paso 4: Identidad/ })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /^Paso 5: FUR/ })).toBeInTheDocument();
  });

  it('traspaso pinta 6 pasos (placa-first)', async () => {
    mocks.getWizardState.mockResolvedValue(TRASPASO_WIZARD);
    renderWizard();
    const stepButtons = await screen.findAllByRole('button', { name: /^Paso \d+:/ });
    expect(stepButtons).toHaveLength(6);
    expect(screen.getByText('Validación')).toBeInTheDocument();
    expect(screen.getByText('Comercial')).toBeInTheDocument();
  });
});

describe('TramiteWizard — instancia existente (Track B)', () => {
  it('con existingInstanceId NO crea instancia y carga el wizard de ese id', async () => {
    render(<TramiteWizard existingInstanceId="inst-99" onExit={() => {}} />);

    // El wizard server-driven se hidrata con el id de la URL...
    const stepButtons = await screen.findAllByRole('button', { name: /^Paso \d+:/ });
    expect(stepButtons).toHaveLength(5);
    expect(mocks.getWizardState).toHaveBeenCalledWith('inst-99', expect.anything());
    // ...y NO dispara un POST /instances (F5 reabre, no re-crea).
    expect(mocks.createInstance).not.toHaveBeenCalled();
  });

  it('reanuda en la frontera (primer paso incompleto), no en el paso 1', async () => {
    // MATRICULA_WIZARD: paso 1 (Consulta VIN) completo, paso 2 (Documentos) incompleto.
    // Al abrir la instancia existente, el cuerpo debe arrancar en Documentos.
    render(<TramiteWizard existingInstanceId="inst-99" onExit={() => {}} />);

    // El título del paso activo (h2 del cuerpo) es "Documentos", no "Consulta VIN".
    expect(
      await screen.findByRole('heading', { level: 2, name: 'Documentos' }),
    ).toBeInTheDocument();
    expect(
      screen.queryByRole('heading', { level: 2, name: 'Consulta VIN' }),
    ).not.toBeInTheDocument();
  });

  it('un trámite sin avance (paso 1 = frontera) abre en el paso 1', async () => {
    // Todos los pasos incompletos: la frontera es el paso 1 (Consulta VIN).
    mocks.getWizardState.mockResolvedValue({
      ...MATRICULA_WIZARD,
      steps: [
        { index: 0, key: 'consulta_vin', label: 'Consulta VIN', status: 'incomplete', reasons: [] },
        { index: 1, key: 'documentos', label: 'Documentos', status: 'locked', reasons: [] },
        { index: 2, key: 'comprador', label: 'Comprador', status: 'locked', reasons: [] },
        { index: 3, key: 'identidad', label: 'Identidad', status: 'locked', reasons: [] },
        { index: 4, key: 'fur', label: 'FUR', status: 'locked', reasons: [] },
      ],
    });
    render(<TramiteWizard existingInstanceId="inst-77" onExit={() => {}} />);

    expect(
      await screen.findByRole('heading', { level: 2, name: 'Consulta VIN' }),
    ).toBeInTheDocument();
  });
});

describe('TramiteWizard — solo lectura (Track C)', () => {
  beforeEach(() => {
    mocks.getWizardState.mockResolvedValue(SUBMITTED_WIZARD);
    // El estado submitted activa el modo solo lectura.
    mocks.getInstance.mockResolvedValue({
      id: 'inst-sub',
      status: 'submitted',
      fieldValues: [],
    });
  });

  it('muestra el banner de solo lectura y oculta Continuar/Finalizar', async () => {
    render(<TramiteWizard existingInstanceId="inst-sub" onExit={() => {}} />);

    expect(await screen.findByText(/solo visualización/i)).toBeInTheDocument();
    // El botón de salida pasa a "Volver al listado".
    expect(
      screen.getByRole('button', { name: /Volver al listado/ }),
    ).toBeInTheDocument();
    // Sin acciones de edición en el footer.
    expect(screen.queryByRole('button', { name: 'Finalizar' })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /Continuar/ })).not.toBeInTheDocument();
    expect(
      screen.queryByRole('button', { name: /Guardar y continuar/ }),
    ).not.toBeInTheDocument();
  });

  it('los inputs del paso de consulta están deshabilitados y sin Consultar RUNT', async () => {
    const user = userEvent.setup();
    render(<TramiteWizard existingInstanceId="inst-sub" onExit={() => {}} />);

    // Consulta VIN es un paso completo → navegable en solo lectura.
    const consultaTab = await screen.findByRole('button', { name: /^Paso 1: Consulta VIN/ });
    await user.click(consultaTab);

    const vin = await screen.findByLabelText('Número VIN');
    expect(vin).toBeDisabled();
    expect(
      screen.queryByRole('button', { name: 'Consultar RUNT' }),
    ).not.toBeInTheDocument();
  });
});

describe('TramiteWizard — status y reasons traducidos', () => {
  it('traduce los códigos de reason a copy amigable', async () => {
    renderWizard();
    await screen.findByRole('button', { name: /^Paso 1: Consulta VIN/ });
    expect(screen.getByText(/Faltan documentos obligatorios/)).toBeInTheDocument();
    expect(screen.getByText(/Consulta RUNT del comprador/)).toBeInTheDocument();
  });

  it('los pasos locked no son clickables', async () => {
    renderWizard();
    await screen.findByRole('button', { name: /^Paso 1: Consulta VIN/ });
    const locked = screen.getByRole('button', { name: /^Paso 4: Identidad \(locked\)/ });
    expect(locked).toBeDisabled();
  });
});

describe('TramiteWizard — navegación en cascada (frontera)', () => {
  it('Identidad no es clickeable cuando Comprador (paso previo) está incompleto', async () => {
    // Defensa de frontend: aunque el backend devolviera Identidad como 'incomplete'
    // (no 'locked'), solo se navega a pasos completos o a la frontera (primer
    // incompleto = Comprador). Identidad queda fuera de alcance → no clickeable.
    mocks.getWizardState.mockResolvedValue({
      ...MATRICULA_WIZARD,
      steps: [
        { index: 0, key: 'consulta_vin', label: 'Consulta VIN', status: 'complete', reasons: [] },
        { index: 1, key: 'documentos', label: 'Documentos', status: 'complete', reasons: [] },
        { index: 2, key: 'comprador', label: 'Comprador', status: 'incomplete', reasons: ['runt_comprador'] },
        { index: 3, key: 'identidad', label: 'Identidad', status: 'incomplete', reasons: ['identidad_pendiente'] },
        { index: 4, key: 'fur', label: 'FUR', status: 'incomplete', reasons: ['fur_pendiente'] },
      ],
    });
    renderWizard();
    await screen.findByRole('button', { name: /^Paso 1: Consulta VIN/ });
    // Comprador es la frontera → navegable.
    expect(screen.getByRole('button', { name: /^Paso 3: Comprador/ })).toBeEnabled();
    // Identidad está más allá de la frontera → NO navegable, pese a no estar 'locked'.
    expect(screen.getByRole('button', { name: /^Paso 4: Identidad/ })).toBeDisabled();
  });
});

describe('TramiteWizard — Continuar', () => {
  it('Continuar habilitado en step complete', async () => {
    renderWizard();
    await screen.findByRole('button', { name: /^Paso 1: Consulta VIN/ });
    // Paso activo inicial = consulta_vin (complete) → Continuar habilitado.
    expect(screen.getByRole('button', { name: /Continuar/ })).toBeEnabled();
  });

  it('Continuar deshabilitado al navegar a un step incompleto', async () => {
    const user = userEvent.setup();
    renderWizard();
    await screen.findByRole('button', { name: /^Paso 1: Consulta VIN/ });
    // Navega al paso "Documentos" (incomplete).
    await user.click(screen.getByRole('button', { name: /^Paso 2: Documentos/ }));
    expect(screen.getByRole('button', { name: /Continuar/ })).toBeDisabled();
  });
});

describe('TramiteWizard — Finalizar y blockers', () => {
  it('Finalizar deshabilitado con blockers y los muestra traducidos', async () => {
    // Wizard donde el último paso es alcanzable y canSubmit=false.
    mocks.getWizardState.mockResolvedValue(TRASPASO_WIZARD);
    const user = userEvent.setup();
    renderWizard();
    await screen.findByRole('button', { name: /^Paso 1: Consulta/ });
    // Navega al último paso (FUR, índice 5).
    await user.click(screen.getByRole('button', { name: /^Paso 6: FUR/ }));
    expect(screen.getByRole('button', { name: /Finalizar/ })).toBeDisabled();
    expect(screen.getByText(/Hay bloqueos críticos en el pre-vuelo/)).toBeInTheDocument();
  });

  it('Finalizar envía, dispara toast de éxito y vuelve al listado (sin pantalla intermedia)', async () => {
    mocks.getWizardState.mockResolvedValue({
      ...TRASPASO_WIZARD,
      canSubmit: true,
      blockers: [],
      steps: TRASPASO_WIZARD.steps.map((s) => ({ ...s, status: 'complete', reasons: [] as string[] })),
    });
    const onExit = vi.fn();
    const user = userEvent.setup();
    render(
      <TramiteWizard configuration={CONFIG} procedureTypeId="type-1" onExit={onExit} />,
    );
    await screen.findByRole('button', { name: /^Paso 1: Consulta/ });
    await user.click(screen.getByRole('button', { name: /^Paso 6: FUR/ }));
    const finish = screen.getByRole('button', { name: /Finalizar/ });
    expect(finish).toBeEnabled();
    await user.click(finish);

    await waitFor(() => expect(mocks.submitInstance).toHaveBeenCalledWith('inst-1'));
    // Toast de éxito + redirección inmediata (onExit), sin pantalla intermedia.
    expect(toastShow).toHaveBeenCalledWith(expect.stringMatching(/enviado a tránsito/i), 'success');
    expect(onExit).toHaveBeenCalledTimes(1);
    expect(screen.queryByText('¡Trámite enviado!')).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Volver a Operación' })).not.toBeInTheDocument();
  });
});

describe('TramiteWizard — consulta persiste antes de preflight', () => {
  it('persiste el VIN (PATCH field_values) ANTES de runPreflight, y refresca', async () => {
    const user = userEvent.setup();
    renderWizard();
    await screen.findByRole('button', { name: /^Paso 1: Consulta VIN/ });
    // 1 carga inicial del wizard.
    await waitFor(() => expect(mocks.getWizardState).toHaveBeenCalledTimes(1));

    // Captura el VIN en el input del paso consulta_vin (aria-label "Número VIN"
    // tras el rediseño del card de matrícula).
    await user.type(screen.getByLabelText('Número VIN'), '9BWZZZ377VT004251');

    // Orden de llamadas observado para verificar persist→preflight.
    const calls: string[] = [];
    mocks.patchFieldValues.mockImplementation(async () => {
      calls.push('patch');
      return { id: 'inst-1', fieldValues: [] };
    });
    mocks.runPreflight.mockImplementation(async () => {
      calls.push('preflight');
      return GREEN_PREFLIGHT;
    });

    await user.click(screen.getByRole('button', { name: /Consultar RUNT/ }));

    // PATCH con el VIN.
    await waitFor(() =>
      expect(mocks.patchFieldValues).toHaveBeenCalledWith('inst-1', [
        { formFieldId: null, fieldKey: 'vin', valueText: '9BWZZZ377VT004251', valueJson: null },
      ]),
    );
    await waitFor(() => expect(mocks.runPreflight).toHaveBeenCalledWith('inst-1'));
    // El PATCH ocurre ANTES del preflight.
    expect(calls).toEqual(['patch', 'preflight']);
    // Tras correr preflight, el wizard se re-consulta (refresh()).
    await waitFor(() => expect(mocks.getWizardState).toHaveBeenCalledTimes(2));
  });

  it('no persiste ni corre preflight si el VIN está vacío', async () => {
    const user = userEvent.setup();
    renderWizard();
    await screen.findByRole('button', { name: /^Paso 1: Consulta VIN/ });

    await user.click(screen.getByRole('button', { name: /Consultar RUNT/ }));

    // Validación: sin identificador no se persiste ni se consulta.
    expect(mocks.patchFieldValues).not.toHaveBeenCalled();
    expect(mocks.runPreflight).not.toHaveBeenCalled();
    expect(screen.getByText(/Ingresa el VIN antes de consultar/)).toBeInTheDocument();
  });

  it('traspaso persiste placa + documento del propietario antes de preflight', async () => {
    mocks.getWizardState.mockResolvedValue(TRASPASO_WIZARD);
    const user = userEvent.setup();
    renderWizard();
    await screen.findByRole('button', { name: /^Paso 1: Consulta/ });

    await user.type(screen.getByLabelText(/^Placa$/), 'ABC123');
    await user.type(
      screen.getByLabelText(/Número documento propietario/),
      '1020304050',
    );

    await user.click(screen.getByRole('button', { name: /Consultar RUNT/ }));

    await waitFor(() =>
      expect(mocks.patchFieldValues).toHaveBeenCalledWith('inst-1', [
        { formFieldId: null, fieldKey: 'plate', valueText: 'ABC123', valueJson: null },
        { formFieldId: null, fieldKey: 'owner_document_type', valueText: 'CC', valueJson: null },
        {
          formFieldId: null,
          fieldKey: 'owner_document_number',
          valueText: '1020304050',
          valueJson: null,
        },
      ]),
    );
    await waitFor(() => expect(mocks.runPreflight).toHaveBeenCalledWith('inst-1'));
  });
});

describe('TramiteWizard — creación única de instancia (StrictMode)', () => {
  it('crea la instancia UNA sola vez aunque el efecto se re-invoque (StrictMode)', async () => {
    renderWizardStrict();
    await screen.findByRole('button', { name: /^Paso 1: Consulta VIN/ });
    // StrictMode monta→desmonta→remonta y doble-invoca el efecto; la guardia
    // con useRef debe evitar el segundo POST /instances.
    await waitFor(() => expect(mocks.createInstance).toHaveBeenCalledTimes(1));
  });

  it('muestra error visible si la creación de la instancia falla', async () => {
    mocks.createInstance.mockRejectedValueOnce(new Error('reference_number duplicado'));
    renderWizard();
    expect(await screen.findByRole('alert')).toHaveTextContent(/reference_number duplicado/);
  });
});

describe('TramiteWizard — Guardar y continuar (pasos de actores)', () => {
  // Traspaso con consulta+validación completas y vendedor como frontera.
  const VENDEDOR_FRONTIER: WizardState = {
    modalidad: 'traspaso',
    tipologiaCodigo: 'traspaso',
    totalSteps: 6,
    canSubmit: false,
    blockers: [],
    steps: [
      { index: 0, key: 'consulta', label: 'Consulta', status: 'complete', reasons: [] },
      { index: 1, key: 'validacion', label: 'Validación', status: 'complete', reasons: [] },
      { index: 2, key: 'vendedor', label: 'Vendedor', status: 'incomplete', reasons: ['vendedor_incompleto'] },
      { index: 3, key: 'comprador', label: 'Comprador', status: 'locked', reasons: [] },
      { index: 4, key: 'comercial', label: 'Comercial', status: 'locked', reasons: [] },
      { index: 5, key: 'fur', label: 'FUR', status: 'locked', reasons: [] },
    ],
  };
  // Tras guardar el vendedor: vendedor complete, comprador pasa a ser la frontera.
  const VENDEDOR_DONE: WizardState = {
    ...VENDEDOR_FRONTIER,
    steps: VENDEDOR_FRONTIER.steps.map((s) =>
      s.index === 2
        ? { ...s, status: 'complete', reasons: [] as string[] }
        : s.index === 3
          ? { ...s, status: 'incomplete', reasons: ['comprador_incompleto'] }
          : s,
    ),
  };

  it('Continuar en paso vendedor dispara save (PUT actors) ANTES de refrescar y avanzar', async () => {
    const user = userEvent.setup();
    const order: string[] = [];
    let wizardCalls = 0;
    mocks.getWizardState.mockImplementation(async () => {
      wizardCalls += 1;
      order.push('wizard');
      return wizardCalls === 1 ? VENDEDOR_FRONTIER : VENDEDOR_DONE;
    });
    mocks.getActors.mockResolvedValue([
      { rol: 'vendedor', tipoDocumento: 'CC', numeroDocumento: '999', nombreCompleto: 'Pedro Vendedor', email: 'pedro@x.com' },
    ]);
    mocks.saveActors.mockImplementation(async () => {
      order.push('save');
    });

    renderWizard();
    await screen.findByRole('button', { name: /^Paso 1: Consulta/ });

    // Navega al paso Vendedor (frontera).
    await user.click(screen.getByRole('button', { name: /^Paso 3: Vendedor/ }));
    // El form hidrata al vendedor cargado.
    await screen.findByDisplayValue('Pedro Vendedor');

    // Footer del wizard: "Guardar y continuar" (no el botón propio del form).
    await user.click(screen.getByRole('button', { name: /Guardar y continuar/ }));

    // 1) Se guardó el set de actores con el vendedor.
    await waitFor(() => expect(mocks.saveActors).toHaveBeenCalledTimes(1));
    const [instanceId, actors] = mocks.saveActors.mock.calls[0];
    expect(instanceId).toBe('inst-1');
    expect(actors[0]).toMatchObject({ rol: 'vendedor', numeroDocumento: '999' });

    // 2) El save ocurre ANTES del refresh que decide el avance.
    expect(order.indexOf('save')).toBeLessThan(order.lastIndexOf('wizard'));

    // 3) Con el vendedor ya complete, el wizard avanza al paso Comprador.
    expect(await screen.findByText(/Identificación · Comprador/)).toBeInTheDocument();
  });
});

describe('TramiteWizard — paso comercial', () => {
  it('renderiza el form comercial en traspaso', async () => {
    mocks.getWizardState.mockResolvedValue(TRASPASO_WIZARD);
    const user = userEvent.setup();
    renderWizard();
    await screen.findByRole('button', { name: /^Paso 1: Consulta/ });
    await user.click(screen.getByRole('button', { name: /^Paso 5: Comercial/ }));
    expect(
      await screen.findByRole('form', { name: 'Datos comerciales del trámite' }),
    ).toBeInTheDocument();
    expect(screen.getByLabelText(/Valor de venta/)).toBeInTheDocument();
    expect(screen.getByLabelText(/Causal/)).toBeInTheDocument();
  });
});
