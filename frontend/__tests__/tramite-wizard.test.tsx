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

  it('Finalizar habilitado cuando canSubmit', async () => {
    mocks.getWizardState.mockResolvedValue({
      ...TRASPASO_WIZARD,
      canSubmit: true,
      blockers: [],
      steps: TRASPASO_WIZARD.steps.map((s) => ({ ...s, status: 'complete', reasons: [] as string[] })),
    });
    const user = userEvent.setup();
    renderWizard();
    await screen.findByRole('button', { name: /^Paso 1: Consulta/ });
    await user.click(screen.getByRole('button', { name: /^Paso 6: FUR/ }));
    const finish = screen.getByRole('button', { name: /Finalizar/ });
    expect(finish).toBeEnabled();
    await user.click(finish);
    await waitFor(() => expect(mocks.submitInstance).toHaveBeenCalledWith('inst-1'));
  });
});

describe('TramiteWizard — consulta persiste antes de preflight', () => {
  it('persiste el VIN (PATCH field_values) ANTES de runPreflight, y refresca', async () => {
    const user = userEvent.setup();
    renderWizard();
    await screen.findByRole('button', { name: /^Paso 1: Consulta VIN/ });
    // 1 carga inicial del wizard.
    await waitFor(() => expect(mocks.getWizardState).toHaveBeenCalledTimes(1));

    // Captura el VIN en el input del paso consulta_vin.
    await user.type(screen.getByLabelText(/^VIN$/), '9BWZZZ377VT004251');

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
