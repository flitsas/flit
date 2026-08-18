import { createRef } from 'react';
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { act, render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

import type {
  CommercialData,
  PreflightSnapshot,
  ProcedureConfiguration,
  SuggestedCommercialValue,
  WizardState,
} from '@/lib/api/types/procedure-runtime';

/**
 * INVENTARIO DE LOS DATOS COMERCIALES (`CommercialForm` + `AvaluoComercialCard`).
 *
 * Red de seguridad del rediseño visual: fija QUÉ datos tiene que seguir pidiendo la operación
 * comercial del traspaso, no cómo se ven. Estos cinco campos son los que el organismo cobra —el
 * valor de venta y la causal determinan el impuesto y los derechos del trámite—, así que perder
 * uno en una reordenación de tarjetas no se nota en pantalla: se nota en la liquidación.
 *
 * Los datos comerciales ya NO tienen paso propio: viven dentro del paso de Requisitos y solo en
 * traspaso. Por eso el archivo cierra comparando las dos modalidades a través del asistente
 * completo: es la única forma de comprobar que el traslado no los dejó por el camino, y de dejar
 * escrito que en matrícula nunca se pidieron.
 *
 * Se consulta por rótulo accesible y por región/rol a propósito: son los dos contratos que
 * sobreviven a un cambio de maquetación. No se mira ni una clase, ni una etiqueta HTML, ni el
 * texto decorativo de las tarjetas. Si un campo deja de tener rótulo el test cae — que es
 * exactamente lo que se busca, porque un campo sin rótulo ya está roto para el lector de pantalla.
 */

// ── Mock del cliente HTTP (sin red real) ───────────────────────────
//
// El mock cubre a la vez el formulario suelto y el asistente completo: `vi.mock` es por archivo,
// así que la misma superficie sirve para los dos montajes de más abajo.
const mocks = vi.hoisted(() => ({
  // Datos comerciales y avalúo (lo que este archivo inventaría).
  getCommercial: vi.fn(),
  putCommercial: vi.fn(),
  getSuggestedCommercialValue: vi.fn(),
  // Resto del wizard (el paso de Requisitos monta declaraciones, prenda, checklist y observaciones).
  createInstance: vi.fn(),
  getInstance: vi.fn(),
  getWizardState: vi.fn(),
  patchFieldValues: vi.fn(),
  setCurrentStep: vi.fn(),
  runPreflight: vi.fn(),
  getPreflight: vi.fn(),
  getConsultationConfig: vi.fn(),
  submitInstance: vi.fn(),
  transitionInstance: vi.fn(),
  finalizeDraft: vi.fn(),
  getActors: vi.fn(),
  saveActors: vi.fn(),
  getChecklist: vi.fn(),
  getAttachments: vi.fn(),
  uploadAttachment: vi.fn(),
  deleteAttachment: vi.fn(),
  listTransitOffices: vi.fn(),
  getBiometricState: vi.fn(),
  iniciarBiometric: vi.fn(),
  simulateBiometric: vi.fn(),
  ensureIdentity: vi.fn(),
  runtPersonLookup: vi.fn(),
  ruesPersonLookup: vi.fn(),
  lookupLegalRepresentativeByNit: vi.fn(),
  actorContactLookup: vi.fn(),
  getInstanceIdentityValidationAlerts: vi.fn(),
  listBiometric: vi.fn(),
  listFirmas: vi.fn(),
  listParticipantes: vi.fn(),
  generarFur: vi.fn(),
  generarImpronta: vi.fn(),
  generarConsolidado: vi.fn(),
  getPrenda: vi.fn(),
  listMandateSigners: vi.fn(),
  setMandateSigner: vi.fn(),
  listVehicleServiceTypes: vi.fn(),
  ruesPreview: vi.fn(),
}));

vi.mock('@/lib/api/tramites-client', () => ({
  tramitesClient: mocks,
  DEV_TENANT_ID: 'tenant-dev',
  DEV_USER_ID: 'user-dev',
  // Detectores por forma (duck typing) que el wizard importa junto al cliente.
  getDuplicateActiveProcedureId: () => null,
  getVehicleStateBlock: () => null,
  isTransitOfficeUnavailable: () => false,
  isRuesPreviewUnavailable: (err: unknown) =>
    !!err && typeof err === 'object' && (err as { status?: unknown }).status === 503,
}));

// El wizard usa useToast() y useRouter(); se stubean para no exigir sus providers en cada render.
vi.mock('@/components/admin/Toast', () => ({
  useToast: () => ({ show: vi.fn() }),
}));
vi.mock('next/navigation', () => ({
  useRouter: () => ({ push: vi.fn(), replace: vi.fn(), prefetch: vi.fn() }),
}));

import { CommercialForm, type CommercialFormHandle } from '@/components/operacion/CommercialForm';
import { WizardReadOnlyProvider } from '@/components/operacion/WizardReadOnlyContext';
import { TramiteWizard } from '@/components/operacion/TramiteWizard';

const INSTANCE = 'inst-1';

const VACIO: CommercialData = {
  valorVenta: null,
  causal: null,
  tasaImpuesto: null,
  derechos: null,
  metodoPago: null,
};

/** Avalúo multi-fuente (Feature #10707): una fuente con valor, una sin datos y una caída. */
const AVALUO: SuggestedCommercialValue = {
  sugerido: 48000000,
  fuentePrincipal: 'fasecolda',
  sources: [
    { source: 'fasecolda', status: 'ok', value: 48000000, currency: 'COP', message: null, muestras: null },
    { source: 'base_gravable', status: 'no_data', value: null, currency: 'COP', message: null, muestras: null },
    { source: 'mercado_libre', status: 'ok', value: 51500000, currency: 'COP', message: null, muestras: 12 },
  ],
};

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
  blockers: [],
  status: 'borrador',
  allowedTransitions: ['anulado', 'preparado'],
  steps: [
    { index: 0, key: 'consulta_vin', label: 'Consulta VIN', status: 'complete', reasons: [] },
    { index: 1, key: 'documentos', label: 'Datos y Documentos del Trámite', status: 'complete', reasons: [] },
    { index: 2, key: 'comprador', label: 'Comprador', status: 'incomplete', reasons: [] },
    { index: 3, key: 'identidad', label: 'Identidad', status: 'locked', reasons: [] },
    { index: 4, key: 'fur', label: 'FUR', status: 'locked', reasons: [] },
  ],
};

const TRASPASO_WIZARD: WizardState = {
  modalidad: 'traspaso',
  tipologiaCodigo: 'traspaso',
  totalSteps: 5,
  canSubmit: false,
  blockers: [],
  status: 'borrador',
  allowedTransitions: ['anulado', 'preparado'],
  steps: [
    { index: 0, key: 'consulta', label: 'Consulta', status: 'complete', reasons: [] },
    { index: 1, key: 'documentos', label: 'Datos y Documentos del Trámite', status: 'complete', reasons: [] },
    { index: 2, key: 'vendedor', label: 'Vendedor', status: 'complete', reasons: [] },
    { index: 3, key: 'comprador', label: 'Comprador', status: 'complete', reasons: [] },
    { index: 4, key: 'fur', label: 'FUR', status: 'incomplete', reasons: [] },
  ],
};

/**
 * Borrador anterior al traslado: el backend todavía emite el paso propio `comercial`. Sirve para
 * comprobar que un trámite a medias no pierde sus datos comerciales al abrirse con el nuevo asistente.
 */
const TRASPASO_WIZARD_CON_PASO_COMERCIAL: WizardState = {
  ...TRASPASO_WIZARD,
  totalSteps: 6,
  steps: [
    ...TRASPASO_WIZARD.steps.slice(0, 4),
    { index: 4, key: 'comercial', label: 'Comercial', status: 'complete', reasons: [] },
    { index: 5, key: 'fur', label: 'FUR', status: 'incomplete', reasons: [] },
  ],
};

const GREEN_PREFLIGHT: PreflightSnapshot = {
  overall: 'green',
  checks: [{ key: 'soat', label: 'SOAT', status: 'ok', source: 'RUNT', message: 'Vigente' }],
  createdAt: '2026-06-18T00:00:00Z',
};

beforeEach(() => {
  vi.clearAllMocks();
  mocks.getCommercial.mockResolvedValue(VACIO);
  mocks.putCommercial.mockResolvedValue(VACIO);
  mocks.getSuggestedCommercialValue.mockResolvedValue(AVALUO);

  mocks.createInstance.mockResolvedValue({ id: INSTANCE });
  mocks.getInstance.mockResolvedValue({ id: INSTANCE, fieldValues: [] });
  mocks.getWizardState.mockResolvedValue(MATRICULA_WIZARD);
  mocks.patchFieldValues.mockResolvedValue({ id: INSTANCE, fieldValues: [] });
  mocks.setCurrentStep.mockResolvedValue({ id: INSTANCE, currentStep: null });
  mocks.runPreflight.mockResolvedValue(GREEN_PREFLIGHT);
  mocks.getPreflight.mockResolvedValue(null);
  mocks.getConsultationConfig.mockResolvedValue({
    vehicleVin: 'kyverum_runt',
    vehiclePlate: 'kyverum_runt',
    conductor: 'kyverum_runt_conductor',
  });
  mocks.submitInstance.mockResolvedValue({ id: INSTANCE });
  mocks.transitionInstance.mockResolvedValue({ id: INSTANCE, status: 'preparado' });
  mocks.finalizeDraft.mockResolvedValue({ id: INSTANCE, status: 'borrador' });
  mocks.getActors.mockResolvedValue([]);
  mocks.saveActors.mockResolvedValue(undefined);
  mocks.getChecklist.mockResolvedValue({ items: [], faltanObligatorios: 0, completo: true });
  mocks.getAttachments.mockResolvedValue([]);
  mocks.listTransitOffices.mockResolvedValue([]);
  mocks.getBiometricState.mockResolvedValue({ validations: [], provider: 'mock' });
  mocks.getInstanceIdentityValidationAlerts.mockResolvedValue({ alerts: [], total: 0 });
  mocks.listBiometric.mockResolvedValue([]);
  mocks.listFirmas.mockResolvedValue([]);
  mocks.listParticipantes.mockResolvedValue([]);
  mocks.generarFur.mockResolvedValue({ documents: [] });
  mocks.generarImpronta.mockResolvedValue({ attachmentId: 'imp-1', filename: 'i.pdf', sha256: 'x', radicado: 'R', hash: 'h' });
  mocks.generarConsolidado.mockResolvedValue({ regenerado: true });
  mocks.getPrenda.mockResolvedValue(null);
  mocks.listMandateSigners.mockResolvedValue({ opciones: [], elegidoId: null, editable: true });
  mocks.setMandateSigner.mockResolvedValue(undefined);
  mocks.listVehicleServiceTypes.mockResolvedValue([
    { id: 'ts-1', code: 'PARTICULAR', name: 'Particular', sortOrder: 1 },
    { id: 'ts-2', code: 'PUBLICO', name: 'Público', sortOrder: 2 },
  ]);
  mocks.ruesPreview.mockResolvedValue({ found: false });
  mocks.runtPersonLookup.mockResolvedValue({ found: false });
  mocks.ruesPersonLookup.mockResolvedValue({ found: false });
  mocks.lookupLegalRepresentativeByNit.mockResolvedValue(null);
  mocks.actorContactLookup.mockResolvedValue({ found: false });
  mocks.ensureIdentity.mockResolvedValue({ outcome: 'ya_vigente' });
});

/** Monta el formulario suelto y espera a que su región esté en pantalla. */
async function renderForm(props: Partial<Parameters<typeof CommercialForm>[0]> = {}) {
  const user = userEvent.setup();
  render(<CommercialForm instanceId={INSTANCE} {...props} />);
  await screen.findByRole('form', { name: 'Datos comerciales del trámite' });
  return user;
}

describe('CommercialForm — inventario: los cinco datos de la operación', () => {
  it('conserva valor de venta, causal, tasa, derechos y método de pago', async () => {
    await renderForm();

    // El bloque se presenta con su propio título cuando el contenedor no lo pinta.
    expect(screen.getByRole('heading', { name: 'Datos comerciales' })).toBeInTheDocument();

    // 1 · Lo que se paga por el vehículo.
    expect(screen.getByLabelText(/^Valor de venta/)).toBeInTheDocument();
    // 2 · Por qué cambia de dueño (determina el tratamiento del trámite).
    expect(screen.getByLabelText(/^Causal/)).toBeInTheDocument();
    // 3 y 4 · Liquidación: tasa de impuesto y derechos del organismo.
    expect(screen.getByLabelText(/^Tasa de impuesto/)).toBeInTheDocument();
    expect(screen.getByLabelText('Derechos')).toBeInTheDocument();
    // 5 · Cómo se pagó.
    expect(screen.getByLabelText('Método de pago')).toBeInTheDocument();
  });

  it('la causal ofrece el catálogo completo de motivos del traspaso', async () => {
    await renderForm();

    const causal = screen.getByLabelText(/^Causal/);
    expect(within(causal).getAllByRole('option').map((o) => o.textContent)).toEqual([
      'Selecciona una causal…',
      'Compraventa',
      'Donación',
      'Dación en pago',
      'Adjudicación',
    ]);
  });

  // El rediseño puede mover el asterisco, cambiarlo por un texto o por un color: lo que no puede es
  // dejar de anunciar cuáles son los dos datos sin los que el trámite no se puede liquidar.
  it('marca como obligatorios exactamente el valor de venta y la causal', async () => {
    await renderForm();

    expect(screen.getAllByLabelText('obligatorio')).toHaveLength(2);
  });

  it('el valor de venta se captura en pesos y viaja como entero, no como texto formateado', async () => {
    const user = await renderForm();

    const valor = screen.getByLabelText(/^Valor de venta/);
    await user.type(valor, '50000000');
    // Lo que ve el gestor va agrupado con separador de miles colombiano…
    expect(valor).toHaveValue('50.000.000');

    await user.selectOptions(screen.getByLabelText(/^Causal/), 'COMPRAVENTA');
    await user.click(screen.getByRole('button', { name: 'Guardar datos comerciales' }));

    // …y lo que se persiste es el número de pesos.
    await waitFor(() =>
      expect(mocks.putCommercial).toHaveBeenCalledWith(
        INSTANCE,
        expect.objectContaining({ valorVenta: 50000000, causal: 'COMPRAVENTA' }),
      ),
    );
  });

  it('los cinco campos llegan completos al guardar desde el pie del asistente', async () => {
    // Embebido, el guardado lo dispara la shell vía el handle imperativo: es el camino real del
    // wizard y el que tiene que seguir arrastrando los cinco datos.
    const ref = createRef<CommercialFormHandle>();
    const user = userEvent.setup();
    render(<CommercialForm ref={ref} instanceId={INSTANCE} embeddedInWizard />);
    await screen.findByRole('form', { name: 'Datos comerciales del trámite' });

    await user.type(screen.getByLabelText(/^Valor de venta/), '30000000');
    await user.selectOptions(screen.getByLabelText(/^Causal/), 'DONACION');
    await user.type(screen.getByLabelText(/^Tasa de impuesto/), '1.5');
    await user.type(screen.getByLabelText('Derechos'), '120000');
    await user.type(screen.getByLabelText('Método de pago'), 'Transferencia');

    // Sin botón propio: el guardado cuelga del "Guardar y continuar" de la shell.
    expect(screen.queryByRole('button', { name: 'Guardar datos comerciales' })).toBeNull();

    await act(async () => {
      await ref.current!.save();
    });

    expect(mocks.putCommercial).toHaveBeenCalledWith(
      INSTANCE,
      expect.objectContaining({
        valorVenta: 30000000,
        causal: 'DONACION',
        tasaImpuesto: 1.5,
        derechos: 120000,
        metodoPago: 'Transferencia',
      }),
    );
  });

  it('sin valor de venta ni causal no guarda y lo dice en voz alta', async () => {
    // Con el botón propio la puerta es el `disabled`: no hay nada que enviar.
    await renderForm();
    expect(screen.getByRole('button', { name: 'Guardar datos comerciales' })).toBeDisabled();

    // Embebido no hay botón propio, así que el aviso tiene que aparecer al intentar continuar desde
    // el pie del asistente. Es el único camino por el que el gestor llega a este mensaje.
    const ref = createRef<CommercialFormHandle>();
    render(<CommercialForm ref={ref} instanceId={INSTANCE} embeddedInWizard hideHeader />);
    await waitFor(() =>
      expect(screen.getAllByRole('form', { name: 'Datos comerciales del trámite' })).toHaveLength(2),
    );

    let guardado: boolean | undefined;
    await act(async () => {
      guardado = await ref.current!.save();
    });

    expect(guardado).toBe(false);
    expect(await screen.findByRole('alert')).toHaveTextContent(
      /Ingresa el valor de venta y la causal para continuar/,
    );
    expect(mocks.putCommercial).not.toHaveBeenCalled();
  });

  it('en solo lectura conserva los datos visibles pero sin acciones', async () => {
    render(
      <WizardReadOnlyProvider readOnly>
        <CommercialForm instanceId={INSTANCE} />
      </WizardReadOnlyProvider>,
    );
    await screen.findByRole('form', { name: 'Datos comerciales del trámite' });

    expect(screen.getByLabelText(/^Valor de venta/)).toBeDisabled();
    expect(screen.getByLabelText(/^Causal/)).toBeDisabled();
    expect(screen.queryByRole('button', { name: 'Guardar datos comerciales' })).toBeNull();
    // El avalúo es una ayuda para capturar: sin captura no se ofrece.
    expect(screen.queryByRole('region', { name: 'Avalúo comercial sugerido' })).toBeNull();
  });
});

describe('CommercialForm — inventario: tarjeta de avalúo comercial sugerido', () => {
  it('conserva el valor sugerido, su fuente y las tres fuentes consultadas', async () => {
    await renderForm();

    const avaluo = await screen.findByRole('region', { name: 'Avalúo comercial sugerido' });
    expect(within(avaluo).getByRole('heading', { name: 'Avalúo comercial' })).toBeInTheDocument();

    // Valor sugerido y de qué fuente sale.
    expect(within(avaluo).getByText('Sugerido')).toBeInTheDocument();
    expect(within(avaluo).getAllByText(/48\.000\.000/).length).toBeGreaterThan(0);

    // Las tres fuentes siguen listadas aunque alguna no traiga dato.
    expect(within(avaluo).getAllByText('Fasecolda').length).toBeGreaterThan(0);
    expect(within(avaluo).getByText('Base gravable')).toBeInTheDocument();
    expect(within(avaluo).getByText('Mercado Libre')).toBeInTheDocument();
    // Mercado Libre explica de cuántas publicaciones sale su mediana.
    expect(within(avaluo).getByText('Mediana de 12 publicaciones')).toBeInTheDocument();
    // La fuente sin datos se rotula como tal y no ofrece "Usar".
    expect(within(avaluo).getByText('Sin datos')).toBeInTheDocument();
    expect(within(avaluo).getAllByRole('button', { name: 'Usar' })).toHaveLength(2);
  });

  it('aceptar el sugerido lleva el valor al campo de venta', async () => {
    const user = await renderForm();

    await user.click(await screen.findByRole('button', { name: 'Aceptar valor sugerido' }));

    expect(screen.getByLabelText(/^Valor de venta/)).toHaveValue('48.000.000');
    // Aceptado: ya no hay nada que elegir, la tarjeta pasa a informativa.
    expect(await screen.findByText('Valor sugerido aceptado.')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Aceptar valor sugerido' })).toBeNull();
  });

  it('"Usar" de una fuente concreta lleva ESE valor, no el sugerido', async () => {
    const user = await renderForm();

    const avaluo = await screen.findByRole('region', { name: 'Avalúo comercial sugerido' });
    const filas = within(avaluo).getAllByRole('listitem');
    const mercadoLibre = filas.find((li) => within(li).queryByText('Mercado Libre'))!;
    await user.click(within(mercadoLibre).getByRole('button', { name: 'Usar' }));

    expect(screen.getByLabelText(/^Valor de venta/)).toHaveValue('51.500.000');
  });

  it('si el avalúo no se puede consultar, el paso sigue capturable a mano', async () => {
    mocks.getSuggestedCommercialValue.mockRejectedValue(new Error('502'));
    await renderForm();

    expect(
      await screen.findByText(/No se pudo consultar el avalúo en este momento\./),
    ).toBeInTheDocument();
    expect(screen.getByLabelText(/^Valor de venta/)).toBeEnabled();
  });
});

/**
 * PARIDAD ENTRE MODALIDADES — dónde vive hoy la operación comercial.
 *
 * Los datos comerciales perdieron su paso propio y se mudaron dentro de Requisitos, pero solo en
 * traspaso: en matrícula inicial no hay compraventa que liquidar y nunca se pidieron. La comparación
 * se hace montando el asistente entero en las dos modalidades porque es la única manera de
 * comprobar la mudanza: el formulario suelto no sabe en qué trámite está.
 */
describe('Paridad entre modalidades — los datos comerciales dentro de Requisitos', () => {
  /** Abre el paso 2 (Requisitos) del asistente con la modalidad indicada. */
  async function irARequisitos(wizard: WizardState) {
    mocks.getWizardState.mockResolvedValue(wizard);
    const user = userEvent.setup();
    render(<TramiteWizard configuration={CONFIG} procedureTypeId="type-1" onExit={() => {}} />);
    await user.click(await screen.findByRole('button', { name: /^Paso 2: Requisitos/ }));
    return user;
  }

  it('traspaso: Requisitos conserva los cinco campos y la tarjeta de avalúo', async () => {
    await irARequisitos(TRASPASO_WIZARD);

    expect(await screen.findByRole('form', { name: 'Datos comerciales del trámite' })).toBeInTheDocument();
    expect(screen.getByLabelText(/^Valor de venta/)).toBeInTheDocument();
    expect(screen.getByLabelText(/^Causal/)).toBeInTheDocument();
    expect(screen.getByLabelText(/^Tasa de impuesto/)).toBeInTheDocument();
    expect(screen.getByLabelText('Derechos')).toBeInTheDocument();
    expect(screen.getByLabelText('Método de pago')).toBeInTheDocument();
    expect(screen.getByRole('region', { name: 'Avalúo comercial sugerido' })).toBeInTheDocument();
  });

  it('matrícula: Requisitos no pide ningún dato comercial', async () => {
    await irARequisitos(MATRICULA_WIZARD);

    // Se espera a que el paso esté montado por una pieza que sí es común a las dos modalidades.
    expect(await screen.findByRole('region', { name: 'Documentos del trámite' })).toBeInTheDocument();
    expect(screen.queryByRole('form', { name: 'Datos comerciales del trámite' })).toBeNull();
    expect(screen.queryByLabelText(/^Valor de venta/)).toBeNull();
    expect(screen.queryByLabelText(/^Causal/)).toBeNull();
    expect(screen.queryByLabelText('Método de pago')).toBeNull();
    expect(screen.queryByRole('region', { name: 'Avalúo comercial sugerido' })).toBeNull();
  });

  // Convivencia: un borrador anterior al traslado sigue trayendo del backend su paso propio. El
  // asistente lo redirige al contenido de Requisitos, así que el gestor encuentra lo que dejó a
  // medias — con sus datos comerciales dentro.
  //
  // Y se rotula "Requisitos", no "Datos Comerciales": el panel pinta Requisitos entero, así que el
  // nombre viejo hacía que el paso mintiera sobre su contenido.
  it('un borrador con el paso propio de datos comerciales no pierde sus campos', async () => {
    const user = userEvent.setup();
    mocks.getWizardState.mockResolvedValue(TRASPASO_WIZARD_CON_PASO_COMERCIAL);
    render(<TramiteWizard configuration={CONFIG} procedureTypeId="type-1" onExit={() => {}} />);

    // Cuarto y no quinto: el asistente funde vendedor + comprador en un solo paso visual (Actores).
    await user.click(await screen.findByRole('button', { name: /^Paso 4: Requisitos/ }));

    expect(await screen.findByRole('form', { name: 'Datos comerciales del trámite' })).toBeInTheDocument();
    expect(screen.getByLabelText(/^Valor de venta/)).toBeInTheDocument();
    expect(screen.getByLabelText(/^Causal/)).toBeInTheDocument();
  });
});
