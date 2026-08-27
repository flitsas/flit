import { describe, it, expect, vi, beforeEach } from 'vitest';
import { act, render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

import type {
  CommercialData,
  PrendaData,
  WizardState,
} from '@/lib/api/types/procedure-runtime';

/**
 * Bug #11614 — El wizard perdía lo capturado en el paso al navegar por el stepper.
 *
 * Los formularios embebidos (PrendaForm, CommercialForm, ActorsForm) solo persisten vía el `save()`
 * que exponen por ref, y ese `save` únicamente lo disparaba "Continuar": el stepper superior (y
 * "Anterior") cambiaban de paso llamando a `goToStep`, que solo persistía la CLAVE del paso —no su
 * contenido— y encima en fire-and-forget. Al desmontarse el formulario, lo escrito se perdía.
 *
 * Estos tests fallan si la navegación por el stepper vuelve a cambiar de paso sin persistir el
 * contenido del formulario activo (AC7).
 *
 * AC1/AC3 — saltar de paso con el stepper (adelante y atrás) persiste primero, igual que Continuar.
 * AC2 — al regresar al paso original se ve lo capturado (rehidratación desde lo persistido).
 * AC4 — aplica a todo formulario que persista por `save` de ref, no solo a prenda (CommercialForm).
 * AC5 — si el guardado falla, se avisa y NO se cambia de paso (los datos siguen en pantalla).
 * AC6 — sin cambios pendientes, la navegación no dispara guardados innecesarios.
 */
const mocks = vi.hoisted(() => ({
  createInstance: vi.fn(),
  getInstance: vi.fn(),
  getWizardState: vi.fn(),
  patchFieldValues: vi.fn(),
  setCurrentStep: vi.fn(),
  runPreflight: vi.fn(),
  getPreflight: vi.fn(),
  getConsultationConfig: vi.fn(),
  getCommercial: vi.fn(),
  putCommercial: vi.fn(),
  getPrenda: vi.fn(),
  putPrenda: vi.fn(),
  submitInstance: vi.fn(),
  transitionInstance: vi.fn(),
  finalizeDraft: vi.fn(),
  getActors: vi.fn(),
  saveActors: vi.fn(),
  runtPersonLookup: vi.fn(),
  ruesPersonLookup: vi.fn(),
  actorContactLookup: vi.fn(),
  lookupLegalRepresentativeByNit: vi.fn(),
  listVehicleServiceTypes: vi.fn(),
  getChecklist: vi.fn(),
  getAttachments: vi.fn(),
  uploadAttachment: vi.fn(),
  deleteAttachment: vi.fn(),
  fetchAttachmentPreviewUrl: vi.fn(),
  downloadAttachment: vi.fn(),
  listTransitOffices: vi.fn(),
  getBiometricState: vi.fn(),
  iniciarBiometric: vi.fn(),
  simulateBiometric: vi.fn(),
  ensureIdentity: vi.fn(),
  getInstanceIdentityValidationAlerts: vi.fn(),
  listBiometric: vi.fn(),
  listFirmas: vi.fn(),
  listParticipantes: vi.fn(),
}));

vi.mock('@/lib/api/tramites-client', () => ({
  tramitesClient: mocks,
  DEV_TENANT_ID: 'tenant-dev',
  DEV_USER_ID: 'user-dev',
  getDuplicateActiveProcedureId: () => null,
  getVehicleStateBlock: () => null,
}));

const toastShow = vi.hoisted(() => vi.fn());
vi.mock('@/components/admin/Toast', () => ({
  useToast: () => ({ show: toastShow }),
}));

const routerPush = vi.hoisted(() => vi.fn());
vi.mock('next/navigation', () => ({
  useRouter: () => ({ push: routerPush, replace: vi.fn(), prefetch: vi.fn() }),
}));

import { TramiteWizard } from '@/components/operacion/TramiteWizard';

/** Matrícula reabierta: consulta hecha, Requisitos (con la prenda embebida) es el paso activo. */
const MATRICULA_WIZARD: WizardState = {
  modalidad: 'matricula_inicial',
  tipologiaCodigo: 'matricula_inicial',
  totalSteps: 5,
  canSubmit: false,
  blockers: ['documentos_incompletos'],
  status: 'borrador',
  allowedTransitions: ['anulado', 'preparado'],
  persistedCurrentStep: 'documentos',
  steps: [
    { index: 0, key: 'consulta_vin', label: 'Consulta VIN', status: 'complete', reasons: [] },
    { index: 1, key: 'documentos', label: 'Datos y Documentos del Trámite', status: 'incomplete', reasons: ['documentos_incompletos'] },
    { index: 2, key: 'comprador', label: 'Comprador', status: 'locked', reasons: [] },
    { index: 3, key: 'identidad', label: 'Identidad', status: 'locked', reasons: [] },
    { index: 4, key: 'fur', label: 'FUR', status: 'locked', reasons: [] },
  ],
};

/** Traspaso reabierto: Requisitos (datos comerciales + prenda) es el paso activo. */
const TRASPASO_WIZARD: WizardState = {
  modalidad: 'traspaso',
  tipologiaCodigo: 'traspaso',
  totalSteps: 6,
  canSubmit: false,
  blockers: [],
  status: 'borrador',
  allowedTransitions: ['anulado', 'preparado'],
  persistedCurrentStep: 'documentos',
  steps: [
    { index: 0, key: 'consulta', label: 'Consulta', status: 'complete', reasons: [] },
    { index: 1, key: 'documentos', label: 'Datos y Documentos del Trámite', status: 'incomplete', reasons: ['comercial_valor'] },
    { index: 2, key: 'vendedor', label: 'Vendedor', status: 'locked', reasons: [] },
    { index: 3, key: 'comprador', label: 'Comprador', status: 'locked', reasons: [] },
    { index: 4, key: 'comercial', label: 'Comercial', status: 'locked', reasons: [] },
    { index: 5, key: 'fur', label: 'FUR', status: 'locked', reasons: [] },
  ],
};

const EMPTY_COMMERCIAL: CommercialData = {
  valorVenta: null,
  causal: null,
  tasaImpuesto: null,
  derechos: null,
  metodoPago: null,
};

const PRENDA_SIN_PRENDA: PrendaData = {
  id: 'prenda-1',
  decision: 'sin_prenda',
  estado: 'vigente',
  acreedorNombre: null,
  acreedorDocumento: null,
  levantamientoEntidad: null,
  createdAt: '2026-08-18T00:00:00Z',
};

beforeEach(() => {
  vi.clearAllMocks();
  mocks.getInstance.mockResolvedValue({
    id: 'inst-1',
    status: 'borrador',
    draftFinalizedAt: null,
    fieldValues: [],
    actors: [],
    currentStep: 'documentos',
  });
  mocks.getWizardState.mockResolvedValue(MATRICULA_WIZARD);
  mocks.patchFieldValues.mockResolvedValue({ id: 'inst-1', fieldValues: [] });
  mocks.setCurrentStep.mockResolvedValue({ id: 'inst-1', currentStep: 'documentos' });
  mocks.runPreflight.mockResolvedValue({ overall: 'green', checks: [], createdAt: '2026-08-18T00:00:00Z' });
  mocks.getPreflight.mockResolvedValue(null);
  mocks.getConsultationConfig.mockResolvedValue({
    vehicleVin: 'kyverum_runt',
    vehiclePlate: 'kyverum_runt',
    conductor: 'kyverum_runt_conductor',
  });
  mocks.getCommercial.mockResolvedValue(EMPTY_COMMERCIAL);
  mocks.putCommercial.mockResolvedValue(EMPTY_COMMERCIAL);
  mocks.getPrenda.mockResolvedValue(null);
  mocks.putPrenda.mockResolvedValue(PRENDA_SIN_PRENDA);
  mocks.submitInstance.mockResolvedValue({ id: 'inst-1' });
  mocks.transitionInstance.mockResolvedValue({ id: 'inst-1', status: 'preparado' });
  mocks.finalizeDraft.mockResolvedValue({ id: 'inst-1', status: 'borrador' });
  mocks.getActors.mockResolvedValue([]);
  mocks.saveActors.mockResolvedValue(undefined);
  mocks.runtPersonLookup.mockResolvedValue({ found: false, source: 'RUNT', mode: 'mock' });
  mocks.ruesPersonLookup.mockResolvedValue({ found: false, source: 'RUES', mode: 'mock' });
  mocks.actorContactLookup.mockResolvedValue({});
  mocks.lookupLegalRepresentativeByNit.mockResolvedValue(null);
  mocks.listVehicleServiceTypes.mockResolvedValue([]);
  mocks.getChecklist.mockResolvedValue({ items: [], faltanObligatorios: 0, completo: true });
  mocks.getAttachments.mockResolvedValue([]);
  mocks.listTransitOffices.mockResolvedValue([]);
  mocks.getBiometricState.mockResolvedValue({ validations: [], provider: 'mock' });
  mocks.getInstanceIdentityValidationAlerts.mockResolvedValue({ alerts: [], total: 0 });
  mocks.listBiometric.mockResolvedValue([]);
  mocks.listFirmas.mockResolvedValue([]);
  mocks.listParticipantes.mockResolvedValue([]);
  mocks.ensureIdentity.mockResolvedValue({ outcome: 'ya_vigente' });
});

// Trámite REABIERTO (`existingInstanceId`): la modalidad la manda el wizard del server, no la
// configuración de entrada — de ahí que ambos helpers rendericen igual y difieran solo en el
// `getWizardState` mockeado. `configuration` es excluyente con `existingInstanceId` en las props.
function renderMatricula() {
  return render(<TramiteWizard existingInstanceId="inst-1" onExit={() => {}} />);
}

function renderTraspaso() {
  return render(<TramiteWizard existingInstanceId="inst-1" onExit={() => {}} />);
}

describe('Bug #11614 — el stepper persiste el paso activo antes de navegar', () => {
  it('AC1/AC3 (atrás) — con prenda capturada sin guardar, saltar al paso 1 con el stepper persiste primero', async () => {
    const user = userEvent.setup();
    renderMatricula();
    await screen.findByRole('heading', { level: 2, name: 'Requisitos' });

    // Captura del gestor: decisión de prenda, sin pulsar Continuar.
    await user.click(await screen.findByRole('button', { name: 'Sin prenda' }));
    expect(mocks.putPrenda).not.toHaveBeenCalled();

    // Salto de paso por el stepper superior (hacia atrás, a un paso completo).
    await user.click(screen.getByRole('button', { name: /^Paso 1:/ }));

    await waitFor(() =>
      expect(mocks.putPrenda).toHaveBeenCalledWith(
        'inst-1',
        expect.objectContaining({ decision: 'sin_prenda' }),
      ),
    );
    // Y el paso sí cambió.
    expect(
      await screen.findByRole('heading', { level: 2, name: 'Consulta Vehículo' }),
    ).toBeInTheDocument();
  });

  it('AC2 — al regresar al paso original, el formulario muestra lo capturado', async () => {
    const user = userEvent.setup();
    renderMatricula();
    await screen.findByRole('heading', { level: 2, name: 'Requisitos' });

    await user.click(await screen.findByRole('button', { name: 'Sin prenda' }));
    // Tras persistir, el backend devuelve la decisión guardada (rehidratación del paso).
    mocks.putPrenda.mockImplementation(async () => {
      mocks.getPrenda.mockResolvedValue(PRENDA_SIN_PRENDA);
      return PRENDA_SIN_PRENDA;
    });

    await user.click(screen.getByRole('button', { name: /^Paso 1:/ }));
    await screen.findByRole('heading', { level: 2, name: 'Consulta Vehículo' });

    // Vuelta al paso de Requisitos: la decisión sigue seleccionada.
    await user.click(screen.getByRole('button', { name: /^Paso 2:/ }));
    await screen.findByRole('heading', { level: 2, name: 'Requisitos' });

    await waitFor(() =>
      expect(screen.getByRole('button', { name: 'Sin prenda' })).toHaveAttribute(
        'aria-pressed',
        'true',
      ),
    );
  });

  it('AC4 — un formulario distinto a prenda (datos comerciales del traspaso) también se persiste al navegar', async () => {
    const user = userEvent.setup();
    mocks.getWizardState.mockResolvedValue(TRASPASO_WIZARD);
    renderTraspaso();
    await screen.findByRole('heading', { level: 2, name: 'Requisitos' });

    await user.type(screen.getByLabelText(/Valor de venta/i), '15000000');
    expect(mocks.putCommercial).not.toHaveBeenCalled();

    await user.click(screen.getByRole('button', { name: /^Paso 1:/ }));

    await waitFor(() =>
      expect(mocks.putCommercial).toHaveBeenCalledWith(
        'inst-1',
        expect.objectContaining({ valorVenta: 15000000, causal: 'COMPRAVENTA' }),
      ),
    );
    expect(
      await screen.findByRole('heading', { level: 2, name: 'Consulta Vehículo' }),
    ).toBeInTheDocument();
  });

  it('AC5 — si el guardado falla, se avisa y NO se cambia de paso (nada se pierde en silencio)', async () => {
    const user = userEvent.setup();
    mocks.putPrenda.mockRejectedValue(new Error('backend caído'));
    renderMatricula();
    await screen.findByRole('heading', { level: 2, name: 'Requisitos' });

    await user.click(await screen.findByRole('button', { name: 'Sin prenda' }));
    await user.click(screen.getByRole('button', { name: /^Paso 1:/ }));

    await waitFor(() => expect(mocks.putPrenda).toHaveBeenCalled());
    // Sigue en el mismo paso, con la captura en pantalla y un aviso visible.
    expect(screen.getByRole('heading', { level: 2, name: 'Requisitos' })).toBeInTheDocument();
    expect(
      screen.queryByRole('heading', { level: 2, name: 'Consulta Vehículo' }),
    ).not.toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Sin prenda' })).toHaveAttribute(
      'aria-pressed',
      'true',
    );
    await waitFor(() =>
      expect(
        screen.getAllByRole('alert').some((el) => /no se cambió de paso/i.test(el.textContent ?? '')),
      ).toBe(true),
    );
  });

  it('AC6 — sin cambios pendientes, navegar por el stepper no dispara guardados', async () => {
    const user = userEvent.setup();
    renderMatricula();
    await screen.findByRole('heading', { level: 2, name: 'Requisitos' });
    await screen.findByRole('button', { name: 'Sin prenda' });

    await user.click(screen.getByRole('button', { name: /^Paso 1:/ }));
    await screen.findByRole('heading', { level: 2, name: 'Consulta Vehículo' });

    expect(mocks.putPrenda).not.toHaveBeenCalled();
    expect(mocks.putCommercial).not.toHaveBeenCalled();
    expect(mocks.saveActors).not.toHaveBeenCalled();
  });
  it('O1 (carrera) — la carga del formulario que resuelve DESPUÉS de la captura no borra la marca de pendiente', async () => {
    const user = userEvent.setup();
    // La decisión de prenda guardada tarda en llegar: el gestor ya está capturando cuando aterriza.
    let resolverCarga: ((valor: PrendaData | null) => void) | undefined;
    mocks.getPrenda.mockImplementation(
      () =>
        new Promise<PrendaData | null>((resolve) => {
          resolverCarga = resolve;
        }),
    );
    renderMatricula();
    await screen.findByRole('heading', { level: 2, name: 'Requisitos' });

    // Captura del gestor MIENTRAS la carga sigue en vuelo.
    await user.click(await screen.findByRole('button', { name: 'Sin prenda' }));
    // Y solo entonces aterriza la carga (sin decisión previa: no pisa lo que el gestor eligió).
    await act(async () => {
      resolverCarga?.(null);
    });

    // Navegar por el stepper debe seguir guardando: la marca de pendiente sobrevivió a la carga.
    await user.click(screen.getByRole('button', { name: /^Paso 1:/ }));

    await waitFor(() =>
      expect(mocks.putPrenda).toHaveBeenCalledWith(
        'inst-1',
        expect.objectContaining({ decision: 'sin_prenda' }),
      ),
    );
  });

  it('O2 (salida de emergencia) — tras un guardado fallido se puede descartar lo capturado y salir del paso', async () => {
    const user = userEvent.setup();
    mocks.putPrenda.mockRejectedValue(new Error('backend caído'));
    renderMatricula();
    await screen.findByRole('heading', { level: 2, name: 'Requisitos' });

    await user.click(await screen.findByRole('button', { name: 'Sin prenda' }));
    await user.click(screen.getByRole('button', { name: /^Paso 1:/ }));
    await waitFor(() => expect(mocks.putPrenda).toHaveBeenCalledTimes(1));
    // Bloqueado: sigue en el mismo paso (el guardado falló y navegar perdería la captura).
    expect(screen.getByRole('heading', { level: 2, name: 'Requisitos' })).toBeInTheDocument();

    // La vía de escape vive dentro del propio aviso y nombra el destino.
    const escape = await screen.findByRole('button', {
      name: 'Descartar lo capturado e ir a «Consulta VIN»',
    });
    expect(escape.closest('[role="alert"]')).not.toBeNull();

    await user.click(escape);

    // Sale del paso a conciencia: sin reintentar el guardado y sin quedar encerrado.
    expect(
      await screen.findByRole('heading', { level: 2, name: 'Consulta Vehículo' }),
    ).toBeInTheDocument();
    expect(mocks.putPrenda).toHaveBeenCalledTimes(1);
    // El foco no se pierde en el <body> cuando el aviso (y su botón) desaparecen.
    expect(document.activeElement).toBe(document.getElementById('tramite-wizard-root'));
  });

  it('O2 — la salida de emergencia solo aparece tras un fallo, no en una navegación normal', async () => {
    const user = userEvent.setup();
    renderMatricula();
    await screen.findByRole('heading', { level: 2, name: 'Requisitos' });
    await screen.findByRole('button', { name: 'Sin prenda' });

    expect(screen.queryByRole('button', { name: /^Descartar lo capturado/ })).toBeNull();
    await user.click(screen.getByRole('button', { name: /^Paso 1:/ }));
    await screen.findByRole('heading', { level: 2, name: 'Consulta Vehículo' });
    expect(screen.queryByRole('button', { name: /^Descartar lo capturado/ })).toBeNull();
  });
});
