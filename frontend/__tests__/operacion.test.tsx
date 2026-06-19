import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

import type { ProcedureTypeSummary } from '@/lib/api/types/procedure-parametrization';
import type { ProcedureConfiguration } from '@/lib/api/types/procedure-runtime';

// ── Mock del cliente HTTP (sin red real) ───────────────────────────
const mocks = vi.hoisted(() => ({
  listPublishedProcedureTypes: vi.fn(),
  getConfiguration: vi.fn(),
  createInstance: vi.fn(),
  getInstance: vi.fn(),
  patchFieldValues: vi.fn(),
  submitInstance: vi.fn(),
  runConsultation: vi.fn(),
}));

vi.mock('@/lib/api/tramites-client', () => ({
  tramitesClient: {
    listPublishedProcedureTypes: mocks.listPublishedProcedureTypes,
    getConfiguration: mocks.getConfiguration,
    createInstance: mocks.createInstance,
    getInstance: mocks.getInstance,
    patchFieldValues: mocks.patchFieldValues,
    submitInstance: mocks.submitInstance,
    runConsultation: mocks.runConsultation,
  },
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
  mocks.runConsultation.mockResolvedValue({
    overall: 'yellow',
    createdAt: '2026-06-18T00:00:00Z',
    checks: [
      { key: 'runt', label: 'RUNT', status: 'ok', source: 'RUNT', message: 'ok' },
    ],
  });
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

describe('AC2 — wizard renderiza la config dinámica', () => {
  it('pinta los labels/inputs de los steps/sections/fields de la config', async () => {
    const user = userEvent.setup();
    render(<OperacionView />);
    await user.click(await screen.findByRole('button', { name: /Iniciar Traspaso estándar/ }));

    // step 1: heading del step + label del campo dinámico
    expect(
      await screen.findByRole('heading', { level: 2, name: 'Datos del vehículo' }),
    ).toBeInTheDocument();
    expect(screen.getByLabelText(/Placa del vehículo/)).toBeInTheDocument();
    // sidebar de progreso con ambos steps
    expect(screen.getByText('Confirmación')).toBeInTheDocument();
    expect(mocks.getConfiguration).toHaveBeenCalledWith('TRASPASO_STD');
    expect(mocks.createInstance).toHaveBeenCalledTimes(1);
  });
});

describe('AC3 — guardar borrador + consulta semáforo', () => {
  it('llama patchFieldValues con los items del step al Continuar', async () => {
    const user = userEvent.setup();
    render(<OperacionView />);
    await user.click(await screen.findByRole('button', { name: /Iniciar Traspaso estándar/ }));

    const input = await screen.findByLabelText(/Placa del vehículo/);
    await user.type(input, 'ABC123');
    await user.click(screen.getByRole('button', { name: /Continuar/ }));

    await waitFor(() => expect(mocks.patchFieldValues).toHaveBeenCalledTimes(1));
    const [instanceId, items] = mocks.patchFieldValues.mock.calls[0];
    expect(instanceId).toBe('inst-1');
    expect(items).toEqual([
      { formFieldId: 'f-placa', fieldKey: 'placa', valueText: 'ABC123', valueJson: null },
    ]);
  });

  it('muestra el panel semáforo al Consultar RUNT', async () => {
    const user = userEvent.setup();
    render(<OperacionView />);
    await user.click(await screen.findByRole('button', { name: /Iniciar Traspaso estándar/ }));

    await user.click(await screen.findByRole('button', { name: /Consultar RUNT/ }));
    await waitFor(() => expect(mocks.runConsultation).toHaveBeenCalledTimes(1));
    expect(mocks.runConsultation).toHaveBeenCalledWith('inst-1', 'RUNT_VEHICLE');
    expect(await screen.findByText('Pre-vuelo con advertencias')).toBeInTheDocument();
  });
});
