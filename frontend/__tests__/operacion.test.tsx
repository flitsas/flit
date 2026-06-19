import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

import type { ProcedureTypeSummary } from '@/lib/api/types/procedure-parametrization';
import type {
  ProcedureConfiguration,
  WizardState,
} from '@/lib/api/types/procedure-runtime';

// ── Mock del cliente HTTP (sin red real) ───────────────────────────
const mocks = vi.hoisted(() => ({
  listPublishedProcedureTypes: vi.fn(),
  getConfiguration: vi.fn(),
  createInstance: vi.fn(),
  getInstance: vi.fn(),
  patchFieldValues: vi.fn(),
  submitInstance: vi.fn(),
  runConsultation: vi.fn(),
  // wizard server-driven (Slice 4b)
  getWizardState: vi.fn(),
  runPreflight: vi.fn(),
  getPreflight: vi.fn(),
  getCommercial: vi.fn(),
  putCommercial: vi.fn(),
  getActors: vi.fn(),
  saveActors: vi.fn(),
  getChecklist: vi.fn(),
  getAttachments: vi.fn(),
}));

vi.mock('@/lib/api/tramites-client', () => ({
  tramitesClient: mocks,
  DEV_TENANT_ID: 'tenant-dev',
  DEV_USER_ID: 'user-dev',
}));

import { OperacionView } from '@/components/operacion/OperacionView';

const PUBLISHED: ProcedureTypeSummary[] = [
  {
    id: 'pt-traspaso',
    code: 'TRASPASO_STD',
    name: 'Traspaso estándar',
    family: 'TRASPASO',
    publicationStatus: 'published',
    isActive: true,
    publishedAt: '2026-01-01T00:00:00Z',
  },
];

const CONFIG: ProcedureConfiguration = {
  id: 'pt-traspaso',
  code: 'TRASPASO_STD',
  name: 'Traspaso estándar',
  family: 'TRASPASO',
  publishedAt: '2026-01-01T00:00:00Z',
  conformationRules: [],
  steps: [
    {
      id: 'step-1',
      code: 'datos_vehiculo',
      title: 'Datos del vehículo',
      sortOrder: 1,
      isActive: true,
      sections: [
        {
          id: 'sec-1',
          code: 'vehiculo',
          title: 'Vehículo',
          sortOrder: 1,
          formFields: [
            {
              id: 'f-placa',
              fieldKey: 'placa',
              label: 'Placa del vehículo',
              fieldType: 'text',
              isRequired: true,
              sortOrder: 1,
              isLocked: false,
              lockReason: null,
              consultationTemplateId: null,
            },
          ],
        },
      ],
    },
    {
      id: 'step-2',
      code: 'confirmacion',
      title: 'Confirmación',
      sortOrder: 2,
      isActive: true,
      sections: [
        {
          id: 'sec-2',
          code: 'confirm',
          title: 'Confirmar',
          sortOrder: 1,
          formFields: [],
        },
      ],
    },
  ],
};

const WIZARD: WizardState = {
  modalidad: 'traspaso',
  tipologiaCodigo: 'traspaso',
  totalSteps: 6,
  canSubmit: false,
  blockers: ['documentos_incompletos'],
  steps: [
    { index: 0, key: 'consulta', label: 'Consulta', status: 'complete', reasons: [] },
    { index: 1, key: 'validacion', label: 'Validación', status: 'incomplete', reasons: ['validacion_pendiente'] },
    { index: 2, key: 'vendedor', label: 'Vendedor', status: 'incomplete', reasons: ['vendedor_incompleto'] },
    { index: 3, key: 'comprador', label: 'Comprador', status: 'locked', reasons: [] },
    { index: 4, key: 'comercial', label: 'Comercial', status: 'locked', reasons: [] },
    { index: 5, key: 'fur', label: 'FUR', status: 'locked', reasons: [] },
  ],
};

beforeEach(() => {
  vi.clearAllMocks();
  mocks.listPublishedProcedureTypes.mockResolvedValue(PUBLISHED);
  mocks.getConfiguration.mockResolvedValue(CONFIG);
  mocks.createInstance.mockResolvedValue({
    id: 'inst-1',
    referenceNumber: 'TR-001',
    status: 'draft',
    procedureTypeId: 'pt-traspaso',
    tenantId: '11111111-1111-1111-1111-111111111111',
    createdAt: '2026-06-18T00:00:00Z',
  });
  mocks.patchFieldValues.mockResolvedValue({});
  mocks.getWizardState.mockResolvedValue(WIZARD);
  mocks.runPreflight.mockResolvedValue({
    overall: 'yellow',
    createdAt: '2026-06-18T00:00:00Z',
    checks: [
      { key: 'runt', label: 'RUNT', status: 'ok', source: 'RUNT', message: 'ok' },
    ],
  });
  mocks.getPreflight.mockResolvedValue(null);
  mocks.getCommercial.mockResolvedValue({
    valorVenta: null, causal: null, tasaImpuesto: null, derechos: null, metodoPago: null,
  });
  mocks.getActors.mockResolvedValue([]);
  mocks.getChecklist.mockResolvedValue({ items: [], faltanObligatorios: 0, completo: true });
  mocks.getAttachments.mockResolvedValue([]);
});

describe('AC1 — selector solo published', () => {
  it('lista únicamente los tipos publicados que devuelve el cliente', async () => {
    render(<OperacionView />);
    expect(await screen.findByText('Traspaso estándar')).toBeInTheDocument();
    expect(screen.getByText(/TRASPASO_STD/)).toBeInTheDocument();
    // sólo se consultó el endpoint de published
    expect(mocks.listPublishedProcedureTypes).toHaveBeenCalledTimes(1);
  });
});

describe('AC2 — wizard server-driven (Slice 4b)', () => {
  it('al elegir el tipo crea la instancia y pinta el sidebar server-driven', async () => {
    const user = userEvent.setup();
    render(<OperacionView />);
    await user.click(await screen.findByRole('button', { name: /Iniciar Traspaso estándar/ }));

    // El wizard crea la instancia y consulta el estado server-driven.
    await waitFor(() => expect(mocks.createInstance).toHaveBeenCalledTimes(1));
    await waitFor(() =>
      expect(mocks.getWizardState).toHaveBeenCalledWith('inst-1', expect.anything()),
    );

    // Sidebar con los 6 pasos del traspaso.
    const stepButtons = await screen.findAllByRole('button', { name: /^Paso \d+:/ });
    expect(stepButtons).toHaveLength(6);
    expect(screen.getByRole('button', { name: /^Paso 1: Consulta/ })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /^Paso 6: FUR/ })).toBeInTheDocument();
  });
});

describe('AC3 — preflight server-driven en el paso consulta', () => {
  it('Consultar RUNT corre el preflight y refresca el wizard', async () => {
    const user = userEvent.setup();
    render(<OperacionView />);
    await user.click(await screen.findByRole('button', { name: /Iniciar Traspaso estándar/ }));

    await screen.findByRole('button', { name: /^Paso 1: Consulta/ });
    await waitFor(() => expect(mocks.getWizardState).toHaveBeenCalledTimes(1));

    // El paso consulta de traspaso exige placa + documento del propietario;
    // sin ellos la consulta no persiste ni corre el preflight (DS-4B-1).
    await user.type(screen.getByLabelText(/^Placa$/), 'ABC123');
    await user.type(
      screen.getByLabelText(/Número documento propietario/),
      '1020304050',
    );

    await user.click(await screen.findByRole('button', { name: /Consultar RUNT/ }));
    await waitFor(() => expect(mocks.runPreflight).toHaveBeenCalledWith('inst-1'));
    // Tras correr el preflight, el wizard se re-consulta (refresh()).
    await waitFor(() => expect(mocks.getWizardState).toHaveBeenCalledTimes(2));
    expect(await screen.findByText('Pre-vuelo con advertencias')).toBeInTheDocument();
  });
});
