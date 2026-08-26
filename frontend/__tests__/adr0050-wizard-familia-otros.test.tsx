import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

import type { WizardCapabilities, WizardState } from '@/lib/api/types/procedure-runtime';

/**
 * ADR-0050 — el asistente se arma con las capacidades del TIPO, no con la modalidad.
 *
 * Con dos modalidades el discriminante `modalidad === 'traspaso'` funcionaba porque las dos ramas
 * agotaban el catálogo. Con veintiún tipos deja de funcionar: un blindaje o un levantamiento de
 * prenda entraban por la rama de matrícula —pedían el VIN de un vehículo que ya tiene placa, se
 * titulaban «Matrícula Inicial» y capturaban un «comprador» de un vehículo que nadie compra— sin que
 * nada fallara. Estas pruebas fijan el comportamiento de la familia OTROS, que es la que no existía.
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
  fetchActiveDeeds: vi.fn(),
  fetchDocumentRequirementsPreview: vi.fn(),
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

vi.mock('@/components/admin/Toast', () => ({ useToast: () => ({ show: vi.fn() }) }));
vi.mock('next/navigation', () => ({
  useRouter: () => ({ push: vi.fn(), replace: vi.fn(), prefetch: vi.fn() }),
}));

import { TramiteWizard } from '@/components/operacion/TramiteWizard';

/** Perfil real de la familia OTROS (DDL 82): un titular, placa, sin valor comercial. */
const CAPS_OTROS: WizardCapabilities = {
  entryMode: 'PLATE',
  requiresSeller: false,
  requiresBuyer: true,
  allowsMultipleBuyer: false,
  requiresCommercialValue: false,
  requiresBiometrics: true,
  biometricActors: ['BUYER'],
  hasPrendaGate: false,
};

const WIZARD_BLINDAJE: WizardState = {
  modalidad: 'OTROS',
  tipologiaCodigo: 'BLINDAJE',
  typeName: 'Blindaje',
  capabilities: CAPS_OTROS,
  totalSteps: 3,
  canSubmit: false,
  blockers: [],
  status: 'borrador',
  allowedTransitions: [],
  steps: [
    { index: 1, key: 'consulta', label: 'Consulta placa', status: 'incomplete', reasons: [] },
    { index: 2, key: 'documentos', label: 'Requisitos', status: 'locked', reasons: [] },
    { index: 3, key: 'fur', label: 'FUR', status: 'locked', reasons: [] },
  ],
};

function renderBlindaje() {
  return render(
    <TramiteWizard
      procedureTypeCode="BLINDAJE"
      family="OTROS"
      title="Blindaje"
      onCreated={() => {}}
      onExit={() => {}}
    />,
  );
}

beforeEach(() => {
  vi.clearAllMocks();
  mocks.getWizardPreview.mockResolvedValue(WIZARD_BLINDAJE);
  mocks.getConsultationConfig.mockResolvedValue({
    vehiclePlate: 'verifik',
    onlyOwnVehicles: false,
    onlyOwnVehiclesByFamily: { matriculas: false, traspaso: false, otros: false },
    blockProcedureFamily: { matriculas: false, traspaso: false, otros: false },
  });
  mocks.fetchActiveDeeds.mockResolvedValue([]);
  mocks.fetchDocumentRequirementsPreview.mockResolvedValue([]);
});

describe('ADR-0050 — un trámite de la familia OTROS ya no se comporta como una matrícula', () => {
  it('se titula con el nombre del tipo, no con «Matrícula Inicial»', async () => {
    renderBlindaje();

    expect(await screen.findByText('Blindaje')).toBeInTheDocument();
    expect(screen.queryByText('Matrícula Inicial')).not.toBeInTheDocument();
  });

  it('pide la placa, porque el vehículo ya está matriculado', async () => {
    renderBlindaje();

    expect(await screen.findByLabelText('Placa del vehículo')).toBeInTheDocument();
    expect(screen.queryByLabelText('Número VIN')).not.toBeInTheDocument();
  });

  it('la consulta del vehículo nombra el trámite elegido, no «el traspaso»', async () => {
    // El copy se bifurcaba solo por VIN/placa, así que TODO lo que entra por placa —la familia OTROS
    // entera— anunciaba «antes de iniciar el traspaso».
    renderBlindaje();

    await screen.findByLabelText('Placa del vehículo');
    expect(
      screen.getByText(/antes de iniciar el trámite de blindaje/i),
    ).toBeInTheDocument();
    expect(screen.queryByText(/iniciar el traspaso/i)).not.toBeInTheDocument();
  });

  it('no ofrece elegir el organismo de tránsito: lo impone el RUNT', async () => {
    renderBlindaje();

    await screen.findByLabelText('Placa del vehículo');
    expect(
      screen.queryByRole('combobox', { name: /secretaría de tránsito/i }),
    ).not.toBeInTheDocument();
  });

  it('la tarjeta de tipo muestra el trámite elegido y no tres opciones fijas', async () => {
    renderBlindaje();

    await screen.findByText('Configuración del Trámite');
    // Las tres tarjetas de la maqueta desaparecieron: el tipo se elige en el catálogo.
    expect(screen.queryByText('Otros Trámites')).not.toBeInTheDocument();
    expect(screen.queryByText(/aún no disponible/)).not.toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Cambiar tipo' })).toBeInTheDocument();
  });

  it('el radicado de cuenta escoge secretaría, pero no dígito de placa ni prioridad', async () => {
    // La tarjeta de radicación se pinta aquí porque el trámite ELIGE organismo —llevar la cuenta a
    // otro ES el trámite—, no porque pida una placa nueva. Cuando esas dos preguntas compartían
    // condición, el radicado acababa preguntando en qué dígito prefiere que termine una placa que el
    // vehículo ya tiene, y duplicando el interruptor de prioridad que vive en la tarjeta del
    // organismo actual.
    mocks.getWizardPreview.mockResolvedValue({
      ...WIZARD_BLINDAJE,
      tipologiaCodigo: 'RADICADO_CUENTA',
      typeName: 'Radicado de cuenta',
      capabilities: { ...CAPS_OTROS, operatorChoosesTransitOffice: true },
    });
    mocks.listTransitOffices.mockResolvedValue([
      { id: 'ot-1', code: '25175000', name: 'SECRETARIA DE MOVILIDAD DE CHIA', cityCode: '25175' },
    ]);

    render(
      <TramiteWizard
        procedureTypeCode="RADICADO_CUENTA"
        family="OTROS"
        title="Radicado de cuenta"
        onCreated={() => {}}
        onExit={() => {}}
      />,
    );

    // La secretaría sí: es el destino, y se escoge antes de consultar.
    await screen.findByText('Organismo de Tránsito y Radicación');

    expect(
      screen.queryByLabelText('Dígito de preasignación de placa'),
    ).not.toBeInTheDocument();
    expect(
      screen.queryByRole('button', { name: 'Trámite prioritario' }),
    ).not.toBeInTheDocument();
  });

  it('pide el checklist informativo por el código del tipo', async () => {
    const user = userEvent.setup();
    renderBlindaje();

    await screen.findByLabelText('Placa del vehículo');
    await user.click(screen.getByRole('button', { name: 'Documentos a tener listos' }));

    // Antes se pedía por modalidad, así que un blindaje recibía los documentos de un traspaso.
    await waitFor(() =>
      expect(mocks.fetchDocumentRequirementsPreview).toHaveBeenCalledWith(
        'BLINDAJE',
        undefined,
      ),
    );
  });
});
